using System.Text.Json;
using AGUI.Abstractions;
using AGUI.Server;
using HockeySeason.Agents;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HockeySeason.Functions;

public sealed class AgentFunction(
    AIAgent agent,
    IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions> jsonOptions,
    ILogger<AgentFunction> logger,
    AgentUsageSessionAccessor? sessions = null)
{
    [Function("HockeySeasonDataAgent")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "agui/hockey-season-data.agent")] HttpRequest request,
        FunctionContext context,
        CancellationToken cancellationToken)
    {
        var authorization = FunctionsAuthorization.IsAuthenticatedV2(context);
        if (!authorization.IsAuthenticated)
        {
            return FunctionsAuthorization.UnauthorizedResult(authorization.Reason);
        }

        var options = jsonOptions.Value.SerializerOptions;
        RunAgentInput? input;
        try
        {
            input = await JsonSerializer.DeserializeAsync<RunAgentInput>(request.Body, options, cancellationToken);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Invalid AG-UI request body.");
            return new BadRequestObjectResult("Invalid AG-UI request body.");
        }
        if (input is null)
        {
            return new BadRequestObjectResult("A request body is required.");
        }

        // Skip unknown "reasoning" history entries: the library conversion only
        // knows system/user/assistant/developer/tool, but AG-UI 1.x clients
        // replay reasoning messages they received in earlier runs.
        AGUIReasoningMessageMapper.DropReasoningHistory(input);

        // The question is what the accounting stores (never the answer): the
        // middleware writes it, with the token usage, once the call ends.
        if (sessions?.Current is { } usageSession)
        {
            usageSession.UserMessage = LastUserMessage(input);
        }
        var chatContext = input.ToChatRequestContext(options);
#pragma warning disable MAAI001
        // The Agent Framework's chat-client adapter is currently experimental.
        var updates = agent.AsIChatClient().GetStreamingResponseAsync(chatContext.Messages, chatContext.ChatOptions, cancellationToken);
#pragma warning restore MAAI001
        var response = request.HttpContext.Response;
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        await foreach (var item in updates.AsAGUIEventStreamAsync(chatContext, cancellationToken))
        {
            await response.WriteAsync($"data: {JsonSerializer.Serialize<BaseEvent>(item, options)}\n\n", cancellationToken);
            await response.Body.FlushAsync(cancellationToken);
        }
        return new EmptyResult();
    }

    /// <summary>
    /// Text of the last user entry of the run, i.e. the question being asked
    /// now (earlier turns replay the whole conversation). Null when the run
    /// carries no user message.
    /// </summary>
    private static string? LastUserMessage(RunAgentInput input)
    {
        for (var index = input.Messages.Count - 1; index >= 0; index--)
        {
            if (input.Messages[index] is AGUI.Abstractions.AGUIUserMessage user)
            {
                var text = user.Content.ToString();
                return string.IsNullOrWhiteSpace(text) ? null : text;
            }
        }
        return null;
    }
}
