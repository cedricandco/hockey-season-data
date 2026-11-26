using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text;
using HockeySeason.Agents;
using HockeySeason.Application;
using HockeySeason.Functions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Azure.Functions.Worker;
using Xunit;

namespace HockeySeason.Tests;

/// <summary>
/// The agent usage middleware is the trigger-side half of the accounting: it
/// opens the session of an agent invocation, and writes what the recording chat
/// client captured once the call ends — only for agent calls, never failing the
/// invocation when the write fails.
/// </summary>
public sealed class AgentUsageMiddlewareTests
{
    private const string AgentPath = "/api/agui/hockey-season-data.agent";

    [Fact]
    public async Task AgentCall_RecordsTheCapturedUsageAndTheQuestion()
    {
        var recorded = new CapturingAgentUsage();
        var accessor = new AgentUsageSessionAccessor();
        var (context, _) = CreateContext(AgentPath, recorded);
        var middleware = CreateMiddleware(accessor, recorded, WithDatabase());

        await middleware.Invoke(context, _ =>
        {
            var session = Assert.IsType<AgentUsageSession>(accessor.Current);
            session.UserMessage = "Quel est le classement ?";
            session.Record(new AgentTokenUsage(100, 20, 120, 5));
            return Task.CompletedTask;
        });

        var record = Assert.Single(recorded.Records);
        Assert.Equal(AgentNames.HockeySeasonData, record.AgentName);
        Assert.Equal("Quel est le classement ?", record.UserMessage);
        Assert.Equal(7, record.UserId);
        Assert.Equal(100, record.Usage.InputTokenCount);
        Assert.Equal(20, record.Usage.OutputTokenCount);
        Assert.Equal(120, record.Usage.TotalTokenCount);
        Assert.Equal(5, record.Usage.ReasoningTokens);
        Assert.Null(accessor.Current);
    }

    [Fact]
    public async Task NonAgentCall_OpensNoSessionAndRecordsNothing()
    {
        var recorded = new CapturingAgentUsage();
        var accessor = new AgentUsageSessionAccessor();
        var (context, _) = CreateContext("/api/users/me");
        var middleware = CreateMiddleware(accessor, recorded, WithDatabase());

        await middleware.Invoke(context, _ =>
        {
            Assert.Null(accessor.Current);
            return Task.CompletedTask;
        });

        Assert.Empty(recorded.Records);
    }

    [Fact]
    public async Task WithoutPersistence_NoSessionIsOpened()
    {
        var recorded = new CapturingAgentUsage();
        var accessor = new AgentUsageSessionAccessor();
        var (context, _) = CreateContext(AgentPath);
        var middleware = CreateMiddleware(accessor, recorded, WithDatabase(""));

        await middleware.Invoke(context, _ =>
        {
            Assert.Null(accessor.Current);
            return Task.CompletedTask;
        });

        Assert.Empty(recorded.Records);
    }

    [Fact]
    public async Task RecordingFailure_DoesNotFailTheInvocation()
    {
        var accessor = new AgentUsageSessionAccessor();
        var (context, _) = CreateContext(AgentPath);
        var middleware = CreateMiddleware(accessor, new ThrowingAgentUsage(), WithDatabase());

        var nextCompleted = false;
        await middleware.Invoke(context, _ =>
        {
            accessor.Current!.Record(new AgentTokenUsage(1, 1, 2, null));
            nextCompleted = true;
            return Task.CompletedTask;
        });

        Assert.True(nextCompleted);
        Assert.Null(accessor.Current);
    }

    /// <summary>
    /// The trigger half of the question: the last user entry of the AG-UI run
    /// is what the accounting stores, and the recording client on the same
    /// accessor captures the provider counts.
    /// </summary>
    [Fact]
    public async Task AgentFunction_PutsTheQuestionOnTheSessionOfTheCall()
    {
        var accessor = new AgentUsageSessionAccessor();
        var session = new AgentUsageSession(AgentNames.HockeySeasonData);
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "auth0|agent-user")], authenticationType: "Bearer"));
        httpContext.Request.Headers["X-Authorization"] = "Bearer mirror-token";
        httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(
            """{"threadId":"t","runId":"r","state":{},"tools":[],"context":[],"forwardedProps":{},"messages":[{"id":"m1","role":"user","content":"Ancienne question"},{"id":"m2","role":"assistant","content":"Réponse"},{"id":"m3","role":"user","content":"Nouvelle question"}]}"""));
        httpContext.Response.Body = new MemoryStream();

        var agent = HockeySeasonDataAgentFactory.Create(
            new TokenUsageRecordingChatClient(new UsageReportingChatClient(), accessor), "Translate.", "test");
        var function = new AgentFunction(
            agent,
            Options.Create(new Microsoft.AspNetCore.Http.Json.JsonOptions()),
            NullLogger<AgentFunction>.Instance,
            accessor);

        using (accessor.Begin(session))
        {
            await function.Run(httpContext.Request, CreateAnonymousContext(httpContext), CancellationToken.None);
        }

        Assert.Equal("Nouvelle question", session.UserMessage);
        Assert.True(session.HasUsage);
        Assert.Equal(42, session.Aggregate().TotalTokenCount);
    }

    private static AgentUsageMiddleware CreateMiddleware(
        AgentUsageSessionAccessor accessor, IAgentUsageService recorder, IConfiguration configuration) =>
        new(configuration,
            Options.Create(new HockeySeasonDataAgentOptions()),
            accessor,
            NullLogger<AgentUsageMiddleware>.Instance);

    private static IConfiguration WithDatabase(string connectionString = "Host=test") =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = connectionString })
            .Build();

    /// <summary>Authenticated invocation on the given path, with the agent and recorder in its services.</summary>
    private static (FunctionContext Context, HttpRequest Request) CreateContext(string path, IAgentUsageService? recorder = null)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = path;
        httpContext.Request.Headers["X-Authorization"] = "Bearer mirror-token";
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "auth0|agent-user")], authenticationType: "Bearer"));

        var services = new ServiceCollection()
            .AddSingleton<IUserProvisioningService>(new FakeProvisioning())
            .AddScoped<RequestUserResolver>()
            .AddSingleton<IAgentUsageService>(recorder ?? new CapturingAgentUsage())
            .BuildServiceProvider();
        return (FunctionsTests.CreateContextWith(httpContext, services), httpContext.Request);
    }

    private static FunctionContext CreateAnonymousContext(DefaultHttpContext httpContext) =>
        FunctionsTests.CreateContextWith(
            httpContext, new ServiceCollection().AddLogging().AddAGUIServer().BuildServiceProvider());

    private sealed class CapturingAgentUsage : IAgentUsageService
    {
        public List<AgentUsageRecord> Records { get; } = [];

        public Task RecordAsync(AgentUsageRecord record, CancellationToken cancellationToken = default)
        {
            Records.Add(record);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingAgentUsage : IAgentUsageService
    {
        public Task RecordAsync(AgentUsageRecord record, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("database is down");
    }

    private sealed class FakeProvisioning : IUserProvisioningService
    {
        public Task<UserProvisioningResult> ProvisionAsync(
            ApplicationUserClaims claims, CancellationToken cancellationToken = default) =>
            Task.FromResult(new UserProvisioningResult(7, Created: false));
    }

    /// <summary>Reports usage on the final streamed update, as Ollama does.</summary>
    private sealed class UsageReportingChatClient : IChatClient
    {
        public void Dispose()
        {
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Hello")));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, "Bonjour");
            yield return new ChatResponseUpdate(ChatRole.Assistant,
                [new UsageContent(new UsageDetails { InputTokenCount = 30, OutputTokenCount = 12 })]);
        }
    }
}
