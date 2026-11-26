using HockeySeason.Agents;
using HockeySeason.Application;
using HockeySeason.Infrastructure.Data;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HockeySeason.Functions;

/// <summary>
/// Records the token usage of every AI agent call once the call has ended.
///
/// The middleware opens an <see cref="AgentUsageSession"/> for the invocation,
/// so the host-wide recording chat client (<see cref="TokenUsageRecordingChatClient"/>)
/// attributes its model calls to this invocation; when the trigger returns,
/// the aggregated counts and the user's question are written to
/// <c>token_usage</c> / <c>message</c>.
///
/// Only agent invocations are accounted, and only when persistence is
/// configured. Never rejects or fails the invocation: a recording error is
/// logged and the caller keeps its response.
/// </summary>
public sealed class AgentUsageMiddleware(
    IConfiguration configuration,
    IOptions<HockeySeasonDataAgentOptions> agentOptions,
    AgentUsageSessionAccessor sessions,
    ILogger<AgentUsageMiddleware> logger) : IFunctionsWorkerMiddleware
{
    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var session = await CreateSessionAsync(context).ConfigureAwait(false);
        if (session is null)
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        using var scope = sessions.Begin(session);
        try
        {
            await next(context).ConfigureAwait(false);
        }
        finally
        {
            await PersistAsync(context, session).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Creates the session of the invocation: null when the trigger is not an
    /// agent call, or when there is no database to write to.
    /// </summary>
    private async Task<AgentUsageSession?> CreateSessionAsync(FunctionContext context)
    {
        if (!DatabaseOptions.IsPersistenceConfigured(configuration))
        {
            return null;
        }

        if (context.GetHttpContext() is not { } httpContext)
        {
            return null;
        }

        var agentName = agentOptions.Value.AgentName;
        var path = httpContext.Request.Path.Value;
        if (string.IsNullOrEmpty(agentName)
            || string.IsNullOrEmpty(path)
            || !path.EndsWith(agentName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var session = new AgentUsageSession(agentName);
        try
        {
            // The provisioning middleware runs first: when it accepted a
            // caller, the principal is available and its scoped resolver
            // already holds the user id (no extra database round trip).
            if (FunctionsAuthorization.GetPrincipal(context) is not null)
            {
                var resolver = context.InstanceServices.GetRequiredService<RequestUserResolver>();
                session.UserId = await resolver.GetOrCreateUserIdAsync(context, context.CancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Usage is accounting: an unresolved caller still gets its tokens
            // recorded, just without the user link.
            logger.LogWarning(exception, "Could not resolve the caller of the agent invocation; recording usage without it.");
        }

        return session;
    }

    private async Task PersistAsync(FunctionContext context, AgentUsageSession session)
    {
        // A call is worth recording when the provider reported counts or when a
        // question was asked: either alone is real accounting information.
        if (!session.HasUsage && session.UserMessage is null)
        {
            return;
        }

        if (context.InstanceServices.GetService<IAgentUsageService>() is not { } usageService)
        {
            return;
        }

        try
        {
            await usageService.RecordAsync(
                new AgentUsageRecord(
                    session.AgentName, session.UserMessage, session.UserId,
                    session.Aggregate(), session.UserMessageId),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not record the token usage of the agent invocation.");
        }
    }
}
