namespace HockeySeason.Application;

/// <summary>
/// Logical names of the AI agents whose usage is accounted. The value is what
/// the <c>agent.agent_name</c> column stores, so it must match the name the
/// agent is published under (AG-UI id / <c>HockeySeasonDataAgentOptions</c>).
/// </summary>
public static class AgentNames
{
    public const string HockeySeasonData = "hockey-season-data.agent";
}

/// <summary>
/// Token usage of one agent call, in the Microsoft.Extensions.AI accounting
/// (the C# counterpart of the Agent Framework <c>UsageDetails</c> the same
/// fields describe): input and output are totals, and reasoning is part of the
/// output — never an addition to it. Values are null when the provider does not
/// report them.
/// </summary>
public sealed record AgentTokenUsage(
    long? InputTokenCount,
    long? OutputTokenCount,
    long? TotalTokenCount,
    long? ReasoningTokens);

/// <summary>
/// One accounting entry to persist: which agent ran, what the caller asked and
/// what the call consumed.
/// </summary>
/// <param name="AgentName">Logical agent name, e.g. <see cref="AgentNames.HockeySeasonData"/>.</param>
/// <param name="UserMessage">The user's question; null when the run carried none.</param>
/// <param name="UserId">Local <c>users</c> id of the caller; null when unresolved.</param>
/// <param name="Usage">Token counts of the call.</param>
/// <param name="UserMessageId">
/// Identifier of the question in the AG-UI run. A run that calls a client-side
/// tool is followed by a second run replaying the same history, so the question
/// arrives twice with the same id: the message row is reused instead of
/// duplicated (the usage rows stay distinct, those tokens were really spent).
/// </param>
public sealed record AgentUsageRecord(
    string AgentName,
    string? UserMessage,
    int? UserId,
    AgentTokenUsage Usage,
    string? UserMessageId = null);

/// <summary>
/// Use case: persist the token usage of an agent call, with the question it
/// answered and the caller it belongs to. Called once per call, at its end.
/// </summary>
public interface IAgentUsageService
{
    /// <summary>
    /// Stores one usage entry: the <c>message</c> row (the question, never the
    /// answer), then the <c>token_usage</c> row referencing it. The agent row is
    /// resolved by name, and created when unknown so a renamed agent never
    /// loses its accounting.
    /// </summary>
    Task RecordAsync(AgentUsageRecord record, CancellationToken cancellationToken = default);
}
