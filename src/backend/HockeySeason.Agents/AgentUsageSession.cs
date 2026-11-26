using HockeySeason.Application;

namespace HockeySeason.Agents;

/// <summary>
/// Token usage accumulated during one agent call. A run may make several model
/// calls (one per tool-calling round), so each call appends its own entry and
/// <see cref="Aggregate"/> sums them into the single row the accounting writes.
/// </summary>
public sealed class AgentUsageSession(string agentName)
{
    private readonly List<AgentTokenUsage> _usages = [];
    private readonly object _gate = new();

    /// <summary>Logical agent name written to <c>agent.agent_name</c>.</summary>
    public string AgentName { get; } = agentName;

    /// <summary>The user's question: set by the caller once the request is parsed, never the answer.</summary>
    public string? UserMessage { get; set; }

    /// <summary>Local <c>users</c> id of the caller, when it could be resolved.</summary>
    public int? UserId { get; set; }

    /// <summary>Appends the usage of one model call.</summary>
    public void Record(AgentTokenUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);
        lock (_gate)
        {
            _usages.Add(usage);
        }
    }

    /// <summary>
    /// Whether the call reported any usage. Without a count there is nothing to
    /// account, so the middleware skips the database entirely.
    /// </summary>
    public bool HasUsage
    {
        get
        {
            lock (_gate)
            {
                return _usages.Any(usage => usage.InputTokenCount is not null
                    || usage.OutputTokenCount is not null
                    || usage.TotalTokenCount is not null
                    || usage.ReasoningTokens is not null);
            }
        }
    }

    /// <summary>
    /// Sums the recorded entries. A count stays null when no call reported it;
    /// <c>total</c> falls back to input + output when the provider omits it.
    /// </summary>
    public AgentTokenUsage Aggregate()
    {
        lock (_gate)
        {
            var input = Sum(_usages, usage => usage.InputTokenCount);
            var output = Sum(_usages, usage => usage.OutputTokenCount);
            var total = Sum(_usages, usage => usage.TotalTokenCount)
                ?? (input is not null && output is not null ? input + output : null);
            return new AgentTokenUsage(input, output, total, Sum(_usages, usage => usage.ReasoningTokens));
        }
    }

    private static long? Sum(List<AgentTokenUsage> usages, Func<AgentTokenUsage, long?> selector)
    {
        long total = 0;
        var any = false;
        foreach (var usage in usages)
        {
            if (selector(usage) is not { } value)
            {
                continue;
            }
            total += value;
            any = true;
        }
        return any ? total : null;
    }
}

/// <summary>
/// The agent-usage session of the current invocation, in the flavour of
/// <c>IHttpContextAccessor</c>: the chat client is a host-wide singleton while
/// the session is per call, so the current session rides on the async flow the
/// Functions middleware opens and closes.
/// </summary>
public sealed class AgentUsageSessionAccessor
{
    private static readonly AsyncLocal<AgentUsageSession?> Ambient = new();

    /// <summary>The session of the current call, or null when usage is not recorded.</summary>
    public AgentUsageSession? Current => Ambient.Value;

    /// <summary>
    /// Makes <paramref name="session"/> current for the rest of the call; the
    /// returned scope restores the previous value.
    /// </summary>
    public IDisposable Begin(AgentUsageSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var previous = Ambient.Value;
        Ambient.Value = session;
        return new Scope(previous);
    }

    private sealed class Scope(AgentUsageSession? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            Ambient.Value = previous;
        }
    }
}
