using System.Runtime.CompilerServices;
using System.Text;
using HockeySeason.Agents;
using HockeySeason.Application;
using HockeySeason.Functions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace HockeySeason.Tests;

public sealed class FunctionsTests
{
    [Fact]
    public void Health_ReturnsProductionReport()
    {
        var time = new DateTimeOffset(2026, 11, 9, 0, 0, 0, TimeSpan.Zero);
        var function = new HealthFunction(new HealthService(new FixedTimeProvider(time)), new TestEnvironment());

        var result = Assert.IsType<OkObjectResult>(function.Run(new DefaultHttpContext().Request));
        var report = Assert.IsType<HealthReport>(result.Value);
        Assert.Equal("Production", report.Environment);
        Assert.Equal(time, report.Time.UtcNow);
        Assert.Equal("UTC", report.Time.TimeZoneId);
    }

    [Fact]
    public async Task Agent_ReturnsAguiEvents()
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(
            """{"threadId":"thread","runId":"run","messages":[{"id":"message","role":"user","content":"Bonjour"}]}"""));
        context.Response.Body = new MemoryStream();

        var result = await CreateAgentFunction().Run(context.Request, CancellationToken.None);

        Assert.IsType<EmptyResult>(result);
        Assert.Equal("text/event-stream", context.Response.ContentType);
        context.Response.Body.Position = 0;
        var output = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Contains("\"type\":\"RUN_STARTED\"", output);
        Assert.Contains("\"type\":\"TEXT_MESSAGE_CONTENT\"", output);
        Assert.Contains("\"delta\":\"Hello\"", output);
        Assert.Contains("\"type\":\"RUN_FINISHED\"", output);
    }

    [Fact]
    public async Task Agent_RejectsInvalidJson()
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{"));
        var result = await CreateAgentFunction().Run(context.Request, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    private static AgentFunction CreateAgentFunction()
    {
        var services = new ServiceCollection().AddLogging().AddAGUIServer().BuildServiceProvider();
        var options = services.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>();
        var agent = HockeySeasonDataAgentFactory.Create(new TestChatClient(), "Translate.", "test");
        return new AgentFunction(agent, options, NullLogger<AgentFunction>.Instance);
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class TestChatClient : IChatClient
    {
        public void Dispose() { }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Hello")));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, "Hello");
        }
    }
}
