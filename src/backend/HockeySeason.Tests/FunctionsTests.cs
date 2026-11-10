using System.Runtime.CompilerServices;
using System.Text;
using System.IdentityModel.Tokens.Jwt;
using HockeySeason.Agents;
using HockeySeason.Application;
using HockeySeason.Functions;
using HockeySeason.LeagueApi;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Azure.Functions.Worker;
using Xunit;

namespace HockeySeason.Tests;

public sealed class FunctionsTests
{
    [Fact]
    public async Task WeeklyReport_ValidatesAndRequiresAuthentication()
    {
        var service = new TestWeeklyReportService();
        var function = new WeeklyReportFunction(service);
        var request = new DefaultHttpContext().Request;
        request.QueryString = new QueryString("?startDate=2026-11-02&endDate=2026-11-09&divisionId=194899&divisionId=194901&teamId=182497");

        Assert.IsType<UnauthorizedResult>(await function.Run(request, CreateAnonymousContext(), CancellationToken.None));
        Assert.Null(service.Request);
        var result = Assert.IsType<OkObjectResult>(await function.Run(request, CreateAuthenticatedContext(), CancellationToken.None));
        Assert.IsType<WeeklyReportResponse>(result.Value);
        Assert.Equal(new[] { 194899, 194901 }, service.Request!.DivisionIds);

        request.QueryString = new QueryString("?startDate=2026-11-09&endDate=2026-11-02&divisionId=194899&teamId=182497");
        Assert.IsType<BadRequestObjectResult>(await function.Run(request, CreateAuthenticatedContext(), CancellationToken.None));
    }

    private sealed class TestWeeklyReportService : IWeeklyReportService
    {
        public WeeklyReportRequest? Request { get; private set; }
        public Task<WeeklyReportResponse> GetAsync(WeeklyReportRequest request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new WeeklyReportResponse(request.StartDate, request.EndDate, request.TeamIds, []));
        }
    }

    [Fact]
    public void Health_ReturnsProductionReport()
    {
        var time = new DateTimeOffset(2026, 11, 9, 0, 0, 0, TimeSpan.Zero);
        var function = new HealthFunction(new HealthService(new FixedTimeProvider(time)), new TestEnvironment());

        var result = Assert.IsType<OkObjectResult>(function.Run(
            new DefaultHttpContext().Request,
            CreateAuthenticatedContext()));
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

        var result = await CreateAgentFunction().Run(context.Request, CreateAuthenticatedContext(), CancellationToken.None);

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
        var result = await CreateAgentFunction().Run(context.Request, CreateAuthenticatedContext(), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Agent_RejectsAnonymousCallerWithNoToken()
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(
            """{"threadId":"thread","runId":"run","messages":[{"id":"message","role":"user","content":"Bonjour"}]}"""));
        Assert.IsType<UnauthorizedResult>(await CreateAgentFunction().Run(context.Request, CreateAnonymousContext(), CancellationToken.None));
    }

    [Fact]
    public async Task Agent_AcceptsValidBearerHeader_WhenMiddlewareAuthenticationIsMissing()
    {
        // Reproduces the Static Web Apps managed-host behavior: UseAuthentication
        // is not applied to the trigger invocation, HttpContext.User stays
        // anonymous, but the raw Authorization header carries a valid JWT.
        const string signingKey = "test-signing-key-that-is-long-enough-1234567890";
        var token = CreateToken(signingKey);
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = $"Bearer {token}";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(
            """{"threadId":"thread","runId":"run","messages":[{"id":"message","role":"user","content":"Bonjour"}]}"""));
        context.Response.Body = new MemoryStream();

        var result = await CreateAgentFunction(ConfigureAuthServices(signingKey)).Run(context.Request, CreateAnonymousBearerContext(context, token), CancellationToken.None);

        Assert.IsType<EmptyResult>(result);
        context.Response.Body.Position = 0;
        var output = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Contains("\"type\":\"RUN_STARTED\"", output);
        Assert.Contains("\"type\":\"RUN_FINISHED\"", output);
    }

    [Fact]
    public void AgentAuthorization_RejectsExpiredBearerHeader_WhenMiddlewareAuthenticationIsMissing()
    {
        const string signingKey = "test-signing-key-that-is-long-enough-1234567890";
        var token = CreateToken(signingKey, expires: DateTimeOffset.UtcNow.AddHours(-1));
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = $"Bearer {token}";

        Assert.False(FunctionsAuthorization.IsAuthenticated(CreateAnonymousBearerContext(context, token)));
    }

    [Fact]
    public void AgentAuthorization_RejectsForgedSignatures()
    {
        var token = CreateToken("forged-signing-key-that-is-long-enough-9876543210");
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = $"Bearer {token}";

        Assert.False(FunctionsAuthorization.IsAuthenticated(CreateAnonymousBearerContext(context, token, signingKey: "test-signing-key-that-is-long-enough-1234567890")));
    }

    [Fact]
    public void AgentAuthorization_RejectsHeaderWithoutBearerScheme()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Basic dXNlcjpwYXNz";

        Assert.False(FunctionsAuthorization.IsAuthenticated(CreateAnonymousBearerContext(context, "dXNlcjpwYXNz")));
    }

    private static FunctionContext CreateAnonymousBearerContext(
        DefaultHttpContext httpContext,
        string token,
        string signingKey = "test-signing-key-that-is-long-enough-1234567890")
    {
        var stub = (FunctionContextStub)CreateContext(httpContext);
        stub.InstanceServices = ConfigureAuthServices(signingKey);
        return stub;
    }

    private static IServiceProvider ConfigureAuthServices(string signingKey) =>
        new ServiceCollection()
            .Configure<HockeySeason.Auth.AuthOptions>(options =>
            {
                options.Issuer = "https://tests.hockey-season-data";
                options.Audience = "hockey-season-api-tests";
                options.SigningKey = signingKey;
                options.PinCode = "654321";
            })
            .AddSingleton<HockeySeason.Auth.IJwtValidator, HockeySeason.Auth.JwtValidator>()
            .BuildServiceProvider();

    private static string CreateToken(
        string signingKey,
        DateTimeOffset? expires = null,
        string issuer = "https://tests.hockey-season-data",
        string audience = "hockey-season-api-tests")
    {
        var handler = new JwtSecurityTokenHandler();
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            SecurityAlgorithms.HmacSha256);
        var now = DateTimeOffset.UtcNow;
        var notBefore = expires is null ? now : expires.Value.AddHours(-5);
        var jwt = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: [new(JwtRegisteredClaimNames.Sub, "pin-user")],
            notBefore: notBefore.UtcDateTime,
            expires: (expires ?? now.AddMinutes(60)).UtcDateTime,
            signingCredentials: credentials);
        return handler.WriteToken(jwt);
    }

    private static FunctionContext CreateAuthenticatedContext()
    {
        var httpContext = new DefaultHttpContext();
        var identity = new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "pin-user")],
            authenticationType: "Bearer");
        httpContext.User = new System.Security.Claims.ClaimsPrincipal(identity);
        return CreateContext(httpContext);
    }

    private static FunctionContext CreateAnonymousContext() => CreateContext(new DefaultHttpContext());

    private static FunctionContext CreateContext(DefaultHttpContext httpContext)
    {
        // FunctionContextExtensions.GetHttpContext resolves the HttpContext
        // from Items["HttpRequestContext"] (ASP.NET Core integration contract).
        var serviceProvider = new ServiceCollection().AddSingleton(httpContext).BuildServiceProvider();
        return new FunctionContextStub
        {
            InstanceServices = serviceProvider,
            Items =
            {
                ["HttpRequestContext"] = httpContext,
            },
        };
    }

    private sealed class FunctionContextStub : FunctionContext
    {
        public override IServiceProvider InstanceServices { get; set; } = new ServiceCollection().BuildServiceProvider();
        public override string InvocationId => "test-invocation";
        public override string FunctionId => "test-function";
        public override TraceContext TraceContext => new TraceContextStub();
        public override BindingContext BindingContext => throw new NotImplementedException();
        public override RetryContext RetryContext => throw new NotImplementedException();
        public override FunctionDefinition FunctionDefinition => throw new NotImplementedException();
        public override IDictionary<object, object> Items { get; set; } = new Dictionary<object, object>();
        public override IInvocationFeatures Features => throw new NotImplementedException();
    }

    private sealed class TraceContextStub : TraceContext
    {
        public override string TraceParent => "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";
        public override string TraceState => string.Empty;
    }

    private static AgentFunction CreateAgentFunction(IServiceProvider? services = null)
    {
        services ??= new ServiceCollection().AddLogging().AddAGUIServer().BuildServiceProvider();
        var options = services.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>();
        var agent = HockeySeasonDataAgentFactory.Create(new TestChatClient(), "Translate.", "test");
        return new AgentFunction(agent, options, NullLogger<AgentFunction>.Instance);
    }

    [Fact]
    public void Health_RequiresAuthenticatedUser()
    {
        var function = new HealthFunction(new HealthService(new FixedTimeProvider(DateTimeOffset.UtcNow)), new TestEnvironment());
        Assert.IsType<UnauthorizedResult>(function.Run(new DefaultHttpContext().Request, CreateAnonymousContext()));
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
