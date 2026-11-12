using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace HockeySeason.Functions;

/// <summary>
/// Streamable HTTP MCP endpoint (<c>POST /api/mcp</c>) exposing the tools
/// registered with <c>AddMcpServer()</c>. <c>MapMcp</c> cannot run inside the
/// Functions host (routing belongs to the host), so every request is served
/// statelessly: a short-lived server is bound to a stateless
/// <see cref="StreamableHttpServerTransport"/> and disposed once the response
/// has been written. No session state and no storage account are required.
/// </summary>
public sealed class McpFunction(
    IOptions<McpServerOptions> serverOptions,
    ILoggerFactory loggerFactory,
    IServiceProvider services)
{
    [Function("Mcp")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", "delete", Route = "mcp")] HttpRequest request,
        FunctionContext context,
        CancellationToken cancellationToken)
    {
        // MCP clients send the standard Authorization header; the shared
        // authorization helper reads the mirrored X-Authorization header.
        if (!request.Headers.ContainsKey("X-Authorization") && request.Headers.TryGetValue("Authorization", out var bearer))
        {
            request.Headers["X-Authorization"] = bearer;
        }

        var authorization = FunctionsAuthorization.IsAuthenticatedV2(context);
        if (!authorization.IsAuthenticated)
        {
            return FunctionsAuthorization.UnauthorizedResult(authorization.Reason);
        }

        // Stateless transport: no session to resume (GET) or terminate (DELETE).
        if (!HttpMethods.IsPost(request.Method))
        {
            return new StatusCodeResult(StatusCodes.Status405MethodNotAllowed);
        }

        JsonRpcMessage? message;
        try
        {
            message = await JsonSerializer.DeserializeAsync(
                request.Body, McpJsonUtilities.DefaultOptions.GetTypeInfo<JsonRpcMessage>(), cancellationToken);
        }
        catch (JsonException)
        {
            message = null;
        }
        if (message is null)
        {
            return new BadRequestObjectResult(new { error = "A valid JSON-RPC message is required." });
        }

        await using var transport = new StreamableHttpServerTransport(loggerFactory) { Stateless = true };
        await using var server = McpServer.Create(transport, serverOptions.Value, loggerFactory, services);
        using var serverCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var serverRun = server.RunAsync(serverCancellation.Token);

        var response = request.HttpContext.Response;
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache,no-store";
        var hasResponse = await transport.HandlePostRequestAsync(message, response.Body, cancellationToken);

        await serverCancellation.CancelAsync();
        try
        {
            await serverRun;
        }
        catch (OperationCanceledException)
        {
            // Expected: the per-request server is stopped once the response is written.
        }

        if (!hasResponse)
        {
            // JSON-RPC notifications and responses have no reply.
            response.ContentType = null;
            return new StatusCodeResult(StatusCodes.Status202Accepted);
        }
        return new Microsoft.AspNetCore.Mvc.EmptyResult();
    }
}
