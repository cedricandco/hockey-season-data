using HockeySeason.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HockeySeason.Infrastructure.Data;

/// <summary>
/// Stores the token usage of an agent call: the question (a <c>message</c>
/// row, the answer is never persisted), then the <c>token_usage</c> row that
/// references it. The <c>agent</c> row is resolved by name and created when
/// unknown, so a renamed agent keeps its accounting instead of losing it.
/// </summary>
public sealed class AgentUsageService(
    IDbContextFactory<HockeySeasonDbContext> dbContextFactory,
    ILogger<AgentUsageService> logger) : IAgentUsageService
{
    public async Task RecordAsync(AgentUsageRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.AgentName);
        ArgumentNullException.ThrowIfNull(record.Usage);

        var question = string.IsNullOrWhiteSpace(record.UserMessage) ? null : record.UserMessage.Trim();
        if (question is null && !HasAnyCount(record.Usage))
        {
            // Nothing was asked and no provider count was reported: an empty
            // row would be noise, not accounting.
            return;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var agentId = await GetOrCreateAgentIdAsync(db, record.AgentName, cancellationToken).ConfigureAwait(false);

        AgentMessage? message = null;
        if (question is not null)
        {
            message = new AgentMessage { AgentId = agentId, Text = question };
            db.Messages.Add(message);
        }

        if (HasAnyCount(record.Usage))
        {
            var usage = record.Usage;
            db.TokenUsages.Add(new TokenUsage
            {
                AgentId = agentId,
                UserId = record.UserId,
                Message = message,
                InputTokenCount = usage.InputTokenCount,
                OutputTokenCount = usage.OutputTokenCount,
                TotalTokenCount = usage.TotalTokenCount,
                ReasoningTokens = usage.ReasoningTokens,
            });
        }

        // A single SaveChanges keeps the message and its usage atomic: an
        // unlinked usage row would be an accounting entry with no question.
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogDebug(
            "Recorded agent call of {AgentName} (question {HasQuestion}, input {InputTokenCount}, output {OutputTokenCount}).",
            record.AgentName, question is not null, record.Usage.InputTokenCount, record.Usage.OutputTokenCount);
    }

    private static bool HasAnyCount(AgentTokenUsage usage) =>
        usage.InputTokenCount is not null
        || usage.OutputTokenCount is not null
        || usage.TotalTokenCount is not null
        || usage.ReasoningTokens is not null;

    private static async Task<int> GetOrCreateAgentIdAsync(
        HockeySeasonDbContext db, string agentName, CancellationToken cancellationToken)
    {
        var agentId = await db.Agents
            .Where(a => a.AgentName == agentName)
            .Select(a => (int?)a.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (agentId is not null)
        {
            return agentId.Value;
        }

        var agent = new Agent { AgentName = agentName };
        db.Agents.Add(agent);
        try
        {
            // Flush the agent first: the message and the usage reference it, and
            // the unique index makes a concurrent creation resolve to one row.
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return agent.Id;
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            db.ChangeTracker.Clear();
            return await db.Agents
                .Where(a => a.AgentName == agentName)
                .Select(a => a.Id)
                .FirstAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation };
}
