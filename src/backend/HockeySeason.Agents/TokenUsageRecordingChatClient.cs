using System.Runtime.CompilerServices;
using HockeySeason.Application;
using Microsoft.Extensions.AI;

namespace HockeySeason.Agents;

/// <summary>
/// Reports the token usage of every model call to the current
/// <see cref="AgentUsageSession"/>, so the middleware can persist it once the
/// call ends. A thin decorator over the real client: it never changes the
/// response, and a call made outside a session (a test, a tool, a host without
/// persistence) simply goes unrecorded.
/// </summary>
/// <remarks>
/// Streaming responses carry their counts as <see cref="UsageContent"/> items,
/// non-streamed ones as <see cref="ChatResponse.Usage"/>; both surfaces are
/// read here so the accounting does not depend on how the agent consumes the
/// client.
/// </remarks>
public sealed class TokenUsageRecordingChatClient(IChatClient innerClient, AgentUsageSessionAccessor sessions)
    : DelegatingChatClient(innerClient)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        Record(response.Usage);
        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var usages = new UsageDetails();
        var recorded = false;

        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
        {
            foreach (var content in update.Contents)
            {
                if (content is UsageContent usage)
                {
                    usages.Add(usage.Details);
                    recorded = true;
                }
            }
            yield return update;
        }

        if (recorded)
        {
            Record(usages);
        }
    }

    private void Record(UsageDetails? usage)
    {
        if (usage is null || sessions.Current is not { } session)
        {
            return;
        }

        session.Record(new AgentTokenUsage(
            usage.InputTokenCount,
            usage.OutputTokenCount,
            usage.TotalTokenCount,
            usage.ReasoningTokenCount));
    }
}
