using System.Runtime.CompilerServices;
using HockeySeason.Agents;
using HockeySeason.Application;
using Microsoft.Extensions.AI;
using Xunit;

namespace HockeySeason.Tests;

/// <summary>
/// The recording chat client is what turns provider usage into the entry the
/// middleware persists: it must capture streaming usage (Ollama reports it in a
/// final <c>UsageContent</c>), non-streamed usage, several calls of one run, and
/// stay silent when no session is open.
/// </summary>
public sealed class TokenUsageRecordingChatClientTests
{
    [Fact]
    public async Task StreamingUsage_IsRecordedOnTheCurrentSession()
    {
        var sessions = new AgentUsageSessionAccessor();
        var session = new AgentUsageSession(AgentNames.HockeySeasonData);
        using var scope = sessions.Begin(session);
        var client = new TokenUsageRecordingChatClient(
            new FakeChatClient(streamUsage: new UsageDetails { InputTokenCount = 11, OutputTokenCount = 4 }), sessions);

        await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "Bonjour")]))
        {
        }

        Assert.True(session.HasUsage);
        var usage = session.Aggregate();
        Assert.Equal(11, usage.InputTokenCount);
        Assert.Equal(4, usage.OutputTokenCount);
        Assert.Equal(15, usage.TotalTokenCount);
        Assert.Null(usage.ReasoningTokens);
    }

    [Fact]
    public async Task SeveralCallsOfOneRun_AreSummed()
    {
        var sessions = new AgentUsageSessionAccessor();
        var session = new AgentUsageSession(AgentNames.HockeySeasonData);
        var inner = new FakeChatClient(
            streamUsage: new UsageDetails { InputTokenCount = 10, OutputTokenCount = 2 },
            nonStreamUsage: new UsageDetails { InputTokenCount = 5, OutputTokenCount = 3, ReasoningTokenCount = 1 });
        var client = new TokenUsageRecordingChatClient(inner, sessions);

        using (sessions.Begin(session))
        {
            await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "q")]))
            {
            }
            await client.GetResponseAsync([new ChatMessage(ChatRole.User, "q2")]);
        }

        var usage = session.Aggregate();
        Assert.Equal(15, usage.InputTokenCount);
        Assert.Equal(5, usage.OutputTokenCount);
        // The provider total is preferred when it reports one; otherwise the
        // aggregate falls back to input + output.
        Assert.Equal(20, usage.TotalTokenCount);
        Assert.Equal(1, usage.ReasoningTokens);
    }

    [Fact]
    public async Task WithoutSession_UsageIsIgnoredAndTheResponseIsUnchanged()
    {
        var sessions = new AgentUsageSessionAccessor();
        var client = new TokenUsageRecordingChatClient(
            new FakeChatClient(streamUsage: new UsageDetails { InputTokenCount = 7, OutputTokenCount = 3 }), sessions);

        var texts = new List<string>();
        await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "q")]))
        {
            texts.Add(update.Text);
        }

        Assert.Null(sessions.Current);
        Assert.Equal(["Bonjour", string.Empty], texts);
    }

    [Fact]
    public async Task Aggregate_KeepsUnknownCountsNull()
    {
        var session = new AgentUsageSession(AgentNames.HockeySeasonData);
        session.Record(new AgentTokenUsage(null, null, null, null));

        var usage = session.Aggregate();

        Assert.Null(usage.InputTokenCount);
        Assert.Null(usage.OutputTokenCount);
        Assert.Null(usage.TotalTokenCount);
        Assert.Null(usage.ReasoningTokens);
    }

    /// <summary>Reports usage the way a provider does: on the final streamed update, or on the response.</summary>
    private sealed class FakeChatClient(UsageDetails? streamUsage = null, UsageDetails? nonStreamUsage = null) : IChatClient
    {
        public void Dispose()
        {
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "Hello"))
            {
                Usage = nonStreamUsage,
            };
            return Task.FromResult(response);
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, "Bonjour");
            if (streamUsage is not null)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, [new UsageContent(streamUsage)]);
            }
        }
    }
}
