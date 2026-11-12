using System.Text;
using System.Text.Json;
using HockeySeason.Application;
using HockeySeason.Domain;
using HockeySeason.Functions;
using HockeySeason.Infrastructure;
using HockeySeason.Mcp;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using Xunit;

namespace HockeySeason.Tests;

/// <summary>
/// The MCP endpoint is served by a Functions HTTP trigger over a stateless
/// Streamable HTTP transport (MapMcp cannot run inside the Functions host).
/// </summary>
public sealed class McpFunctionTests
{
    [Fact]
    public async Task ToolsList_IsServedStatelesslyToAnAuthenticatedCaller()
    {
        var (function, services) = CreateFunction();
        using var _ = services;
        var (request, context, body) = CreateRequest(
            """{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}""");

        var result = await function.Run(request, context, CancellationToken.None);

        Assert.IsType<EmptyResult>(result);
        var output = Encoding.UTF8.GetString(body.ToArray());
        Assert.Contains("get-server-time", output);
    }

    [Fact]
    public async Task ToolCall_ExecutesTheTool()
    {
        var (function, services) = CreateFunction();
        using var _ = services;
        var (request, context, body) = CreateRequest(
            """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"get-server-time","arguments":{}}}""");

        await function.Run(request, context, CancellationToken.None);

        var output = Encoding.UTF8.GetString(body.ToArray());
        Assert.Contains("\"id\":2", output);
        Assert.DoesNotContain("\"error\"", output);
        Assert.Contains("utcNow", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Notifications_AreAcknowledgedWithoutABody()
    {
        var (function, services) = CreateFunction();
        using var _ = services;
        var (request, context, body) = CreateRequest("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");

        var result = await function.Run(request, context, CancellationToken.None);

        Assert.Equal(StatusCodes.Status202Accepted, Assert.IsType<StatusCodeResult>(result).StatusCode);
        Assert.Equal(0, body.Length);
    }

    [Fact]
    public async Task Get_IsNotAllowedAndInvalidBodiesAreRejected()
    {
        var (function, services) = CreateFunction();
        using var _ = services;

        var (getRequest, getContext, _) = CreateRequest("", method: "GET");
        Assert.Equal(StatusCodes.Status405MethodNotAllowed,
            Assert.IsType<StatusCodeResult>(await function.Run(getRequest, getContext, CancellationToken.None)).StatusCode);

        var (badRequest, badContext, _) = CreateRequest("{ not json");
        Assert.IsType<BadRequestObjectResult>(await function.Run(badRequest, badContext, CancellationToken.None));
    }

    [Fact]
    public async Task Anonymous_CallersAreRejectedWith401()
    {
        var (function, services) = CreateFunction();
        using var _ = services;
        var request = new DefaultHttpContext().Request;
        var context = FunctionsTests.CreateContextWith((DefaultHttpContext)request.HttpContext, new ServiceCollection().BuildServiceProvider());

        var result = await function.Run(request, context, CancellationToken.None);

        Assert.Equal(StatusCodes.Status401Unauthorized, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    private static (McpFunction Function, ServiceProvider Services) CreateFunction()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<ITimeProvider, UtcTimeProvider>()
            .AddSingleton<IHealthService, HealthService>()
            .AddMcpServer()
            .WithTools<TimeTool>()
            .Services
            .BuildServiceProvider();
        return (new McpFunction(
            services.GetRequiredService<IOptions<McpServerOptions>>(),
            services.GetRequiredService<ILoggerFactory>(),
            services), services);
    }

    private static (HttpRequest Request, Microsoft.Azure.Functions.Worker.FunctionContext Context, MemoryStream Body) CreateRequest(
        string json, string method = "POST")
    {
        var request = FunctionsTests.CreateAuthenticatedRequest();
        request.Method = method;
        request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var response = new MemoryStream();
        request.HttpContext.Response.Body = response;
        var context = FunctionsTests.CreateFunctionContextForRequest(request);
        return (request, context, response);
    }
}
