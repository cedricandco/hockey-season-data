using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using HockeySeason.Application;
using HockeySeason.Functions;
using HockeySeason.LeagueApi;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace HockeySeason.Tests;

/// <summary>
/// Coverage of the local-account side of the Functions host: user provisioning
/// middleware, user / subscription functions, scope enforcement and the admin
/// import guard.
/// </summary>
public sealed class UserFunctionsTests
{
    private static IConfiguration WithDatabase(string connectionString = "Host=test") =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = connectionString })
            .Build();

    // ─── Provisioning middleware ───────────────────────────────────────────

    [Fact]
    public async Task Middleware_ProvisionsAuthenticatedCaller_BeforeNext()
    {
        var provisioning = new FakeProvisioning();
        var (context, _) = CreateContext(provisioning, claims:
        [
            new(JwtRegisteredClaimNames.Email, "m@example.com"),
            new(JwtRegisteredClaimNames.GivenName, "Marc"),
        ]);
        var middleware = new UserProvisioningMiddleware(WithDatabase(), NullLogger<UserProvisioningMiddleware>.Instance);

        var nextCalled = false;
        await middleware.Invoke(context, _ => { nextCalled = true; return Task.CompletedTask; });

        Assert.True(nextCalled);
        var claim = Assert.Single(provisioning.Provisioned);
        Assert.Equal("auth0|middleware-user", claim.Subject);
        Assert.Equal("m@example.com", claim.Email);
        Assert.Equal("Marc", claim.FirstName);
    }

    [Fact]
    public async Task Middleware_SkipsAnonymousCallers()
    {
        var provisioning = new FakeProvisioning();
        var httpContext = new DefaultHttpContext();
        var context = FunctionsTests.CreateContextWith(httpContext, BuildServices(provisioning));
        var middleware = new UserProvisioningMiddleware(WithDatabase(), NullLogger<UserProvisioningMiddleware>.Instance);

        var nextCalled = false;
        await middleware.Invoke(context, _ => { nextCalled = true; return Task.CompletedTask; });

        Assert.True(nextCalled);
        Assert.Empty(provisioning.Provisioned);
    }

    [Fact]
    public async Task Middleware_SkipsProvisioning_WhenPersistenceIsNotConfigured()
    {
        var provisioning = new FakeProvisioning();
        var (context, _) = CreateContext(provisioning);
        // An empty connection string counts as not configured (test hosts).
        var middleware = new UserProvisioningMiddleware(WithDatabase(""), NullLogger<UserProvisioningMiddleware>.Instance);

        var nextCalled = false;
        await middleware.Invoke(context, _ => { nextCalled = true; return Task.CompletedTask; });

        Assert.True(nextCalled);
        Assert.Empty(provisioning.Provisioned);
    }

    [Fact]
    public async Task Middleware_ProvisioningFailure_DoesNotBlockTheInvocation()
    {
        var provisioning = new ThrowingProvisioning();
        var (context, _) = CreateContext(provisioning);
        var middleware = new UserProvisioningMiddleware(WithDatabase(), NullLogger<UserProvisioningMiddleware>.Instance);

        var nextCalled = false;
        await middleware.Invoke(context, _ => { nextCalled = true; return Task.CompletedTask; });

        Assert.True(nextCalled);
        Assert.Equal(1, provisioning.Attempts);
    }

    // ─── User functions ────────────────────────────────────────────────────

    [Fact]
    public async Task Account_WithoutToken_Returns401()
    {
        var (function, _) = CreateUserFunctions(new FakeUsers());
        var request = new DefaultHttpContext().Request;
        var context = FunctionsTests.CreateContextWith((DefaultHttpContext)request.HttpContext, BuildServices(new FakeProvisioning()));

        var result = await function.Account(request, context, CancellationToken.None);

        Assert.Equal(StatusCodes.Status401Unauthorized, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public async Task Account_ProvisionsTheCallerAndReturnsTheProfile()
    {
        var provisioning = new FakeProvisioning();
        var (function, _) = CreateUserFunctions(new FakeUsers(), provisioning);
        var (context, request) = CreateContext(provisioning);

        var result = Assert.IsType<OkObjectResult>(await function.Account(request, context, CancellationToken.None));

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(7, json.RootElement.GetProperty("id").GetInt32());
        Assert.Equal("COACH", json.RootElement.GetProperty("roles")[0].GetProperty("code").GetString()!.ToUpperInvariant());
        Assert.Single(provisioning.Provisioned);
    }

    [Fact]
    public async Task UpdateSettings_ForwardsTheBodyAndRejectsInvalidJson()
    {
        var users = new FakeUsers();
        var provisioning = new FakeProvisioning();
        var (function, _) = CreateUserFunctions(users, provisioning);
        var (context, request) = CreateContext(provisioning);

        request.Body = Json("""{"primaryColor":"#112233","extra":{"k":"v"}}""");
        Assert.IsType<OkObjectResult>(await function.UpdateSettings(request, context, CancellationToken.None));
        Assert.Equal("#112233", users.LastSettings?.PrimaryColor);
        Assert.Equal("v", users.LastSettings?.Extra?["k"]);

        request.Body = Json("{ not json");
        Assert.IsType<BadRequestObjectResult>(await function.UpdateSettings(request, context, CancellationToken.None));
    }

    [Theory]
    [InlineData(SubscriptionError.None, StatusCodes.Status204NoContent)]
    [InlineData(SubscriptionError.UnknownEntity, StatusCodes.Status404NotFound)]
    [InlineData(SubscriptionError.LimitReached, StatusCodes.Status409Conflict)]
    [InlineData(SubscriptionError.Duplicate, StatusCodes.Status409Conflict)]
    [InlineData(SubscriptionError.OutOfScope, StatusCodes.Status403Forbidden)]
    public async Task AddTeamSubscription_MapsServiceErrorsToHttpStatuses(SubscriptionError error, int expectedStatus)
    {
        var users = new FakeUsers { AddResult = error };
        var provisioning = new FakeProvisioning();
        var (function, _) = CreateUserFunctions(users, provisioning);
        var (context, request) = CreateContext(provisioning);
        request.Body = Json("""{"entityId":181303}""");

        var result = await function.AddTeam(request, context, CancellationToken.None);

        Assert.Equal(expectedStatus, Assert.IsAssignableFrom<IStatusCodeActionResult>(result).StatusCode);
        Assert.Equal(181303, users.LastAddedEntityId);
    }

    [Fact]
    public async Task RemoveTeamSubscription_LetsAdminsBypassTheLock()
    {
        var users = new FakeUsers();
        var provisioning = new FakeProvisioning();
        var (function, _) = CreateUserFunctions(users, provisioning);
        var (adminContext, request) = CreateContext(provisioning, claims: [new("roles", "admin")]);
        var (memberContext, _) = CreateContext(provisioning);

        await function.RemoveTeam(request, 5, adminContext, CancellationToken.None);
        Assert.True(users.LastRemoveWasAdmin);

        await function.RemoveTeam(request, 5, memberContext, CancellationToken.None);
        Assert.False(users.LastRemoveWasAdmin);
    }

    [Fact]
    public async Task UnlockRequest_ReturnsAccepted()
    {
        var provisioning = new FakeProvisioning();
        var (function, _) = CreateUserFunctions(new FakeUsers(), provisioning);
        var (context, request) = CreateContext(provisioning);

        Assert.IsType<AcceptedResult>(await function.RequestTeamUnlock(request, 5, context, CancellationToken.None));
    }

    // ─── Scope enforcement ─────────────────────────────────────────────────

    [Fact]
    public async Task TeamFunctions_RejectOutOfScopeLeaguesAndTeams()
    {
        var provisioning = new FakeProvisioning();
        var scope = new CallerScope(new RequestUserResolver(provisioning), new FakeScopes(leagues: [100], teams: [1]), WithDatabase());
        var function = new TeamFunctions(null!, null!, Options.Create(new LeagueApiOptions()), scope);
        var (context, request) = CreateContext(provisioning);

        Assert.Equal(StatusCodes.Status403Forbidden,
            Assert.IsType<StatusCodeResult>(await function.LeagueTeams(request, 999, context, CancellationToken.None)).StatusCode);
        Assert.Equal(StatusCodes.Status403Forbidden,
            Assert.IsType<StatusCodeResult>(await function.TeamDetail(request, 100, 2, context, CancellationToken.None)).StatusCode);
    }

    [Fact]
    public async Task WeeklyReport_DropsOutOfScopeIdsAndForbidsFullyOutOfScopeRequests()
    {
        var provisioning = new FakeProvisioning();
        var scope = new CallerScope(new RequestUserResolver(provisioning), new FakeScopes(offices: [194899], teams: [182497]), WithDatabase());
        var reports = new CapturingReports();
        var function = new WeeklyReportFunction(reports, scope);
        var (context, request) = CreateContext(provisioning);

        request.QueryString = new QueryString("?startDate=2026-11-02&endDate=2026-11-09&divisionId=194899&divisionId=194901&teamId=182497&teamId=999");
        Assert.IsType<OkObjectResult>(await function.Run(request, context, CancellationToken.None));
        Assert.Equal([194899], reports.Request!.DivisionIds);
        Assert.Equal([182497], reports.Request.TeamIds);

        request.QueryString = new QueryString("?startDate=2026-11-02&endDate=2026-11-09&divisionId=194901&teamId=999");
        Assert.Equal(StatusCodes.Status403Forbidden,
            Assert.IsType<StatusCodeResult>(await function.Run(request, context, CancellationToken.None)).StatusCode);
    }

    // ─── Admin import guard ────────────────────────────────────────────────

    [Fact]
    public async Task AdminImport_RejectsNonAdminsAndAnonymousCallers()
    {
        var provisioning = new FakeProvisioning();
        // Configuration without Authorization:OpenAccess — the admin guard applies.
        var function = new AdminImportFunctions(null!, null!, Options.Create(new LeagueApiOptions()),
            new ConfigurationBuilder().Build());
        var (memberContext, request) = CreateContext(provisioning);

        Assert.Equal(StatusCodes.Status403Forbidden,
            Assert.IsType<StatusCodeResult>(await function.ImportOffices(request, memberContext, CancellationToken.None)).StatusCode);
        Assert.Equal(StatusCodes.Status403Forbidden,
            Assert.IsType<StatusCodeResult>(await function.ImportLeagues(request, memberContext, CancellationToken.None)).StatusCode);
        Assert.Equal(StatusCodes.Status403Forbidden,
            Assert.IsType<StatusCodeResult>(await function.ImportTeams(request, memberContext, CancellationToken.None)).StatusCode);

        var anonymousRequest = new DefaultHttpContext().Request;
        var anonymousContext = FunctionsTests.CreateContextWith((DefaultHttpContext)anonymousRequest.HttpContext, BuildServices(provisioning));
        Assert.Equal(StatusCodes.Status401Unauthorized,
            Assert.IsType<ObjectResult>(await function.ImportOffices(anonymousRequest, anonymousContext, CancellationToken.None)).StatusCode);
    }

    [Fact]
    public async Task OpenAccess_LetsAnyAuthenticatedCallerSkipScopeAndAdminGuards()
    {
        var provisioning = new FakeProvisioning();

        // Scope: division/team ids outside the fake scope are kept unfiltered.
        var reports = new CapturingReports();
        var scope = new CallerScope(new RequestUserResolver(provisioning), new FakeScopes(offices: [194899], teams: [182497]), WithOpenAccess());
        var reportFunction = new WeeklyReportFunction(reports, scope);
        var (context, request) = CreateContext(provisioning);
        request.QueryString = new QueryString("?startDate=2026-11-02&endDate=2026-11-09&divisionId=999&teamId=999");
        Assert.IsType<OkObjectResult>(await reportFunction.Run(request, context, CancellationToken.None));
        Assert.Equal([999], reports.Request!.DivisionIds);
        Assert.Equal([999], reports.Request.TeamIds);

        // Admin import: an admin-less caller passes the guard and reaches the import.
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var dbOptions = new DbContextOptionsBuilder<HockeySeason.Infrastructure.Data.HockeySeasonDbContext>()
            .UseSqlite(connection)
            .Options;
        await using (var db = new HockeySeason.Infrastructure.Data.HockeySeasonDbContext(dbOptions))
        {
            await db.Database.EnsureCreatedAsync();
        }
        var imports = new AdminImportFunctions(null!, new TestDbContextFactory(dbOptions), Options.Create(new LeagueApiOptions()), WithOpenAccess());
        Assert.IsType<OkObjectResult>(await imports.ImportOffices(request, context, CancellationToken.None)); // not 401/403
    }

    // ─── Helpers ───────────────────────────────────────────────────────────

    /// <summary>DbContext factory over fixed SQLite options (admin import test).</summary>
    private sealed class TestDbContextFactory(DbContextOptions<HockeySeason.Infrastructure.Data.HockeySeasonDbContext> options)
        : IDbContextFactory<HockeySeason.Infrastructure.Data.HockeySeasonDbContext>
    {
        public HockeySeason.Infrastructure.Data.HockeySeasonDbContext CreateDbContext() => new(options);
        public ValueTask<HockeySeason.Infrastructure.Data.HockeySeasonDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new HockeySeason.Infrastructure.Data.HockeySeasonDbContext(options));
    }

    private static IConfiguration WithOpenAccess() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Authorization:OpenAccess"] = "true" })
            .Build();

    private static MemoryStream Json(string value) => new(Encoding.UTF8.GetBytes(value));

    private static IServiceProvider BuildServices(IUserProvisioningService provisioning) =>
        new ServiceCollection()
            .AddSingleton(provisioning)
            .AddScoped<RequestUserResolver>()
            .BuildServiceProvider();

    /// <summary>An authenticated invocation (principal set by the middleware-style contract).</summary>
    private static (FunctionContext Context, HttpRequest Request) CreateContext(
        IUserProvisioningService provisioning, IEnumerable<Claim>? claims = null)
    {
        var httpContext = new DefaultHttpContext();
        var identityClaims = new List<Claim> { new(ClaimTypes.NameIdentifier, "auth0|middleware-user") };
        identityClaims.AddRange(claims ?? []);
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(identityClaims, authenticationType: "Bearer"));
        httpContext.Request.Headers["X-Authorization"] = "Bearer test";
        return (FunctionsTests.CreateContextWith(httpContext, BuildServices(provisioning)), httpContext.Request);
    }

    private static (UserFunctions Function, RequestUserResolver Resolver) CreateUserFunctions(
        FakeUsers users, IUserProvisioningService? provisioning = null)
    {
        var resolver = new RequestUserResolver(provisioning ?? new FakeProvisioning());
        return (new UserFunctions(resolver, users), resolver);
    }

    private sealed class FakeProvisioning : IUserProvisioningService
    {
        public List<ApplicationUserClaims> Provisioned { get; } = [];

        public Task<UserProvisioningResult> ProvisionAsync(ApplicationUserClaims claims, CancellationToken cancellationToken = default)
        {
            Provisioned.Add(claims);
            return Task.FromResult(new UserProvisioningResult(UserId: 7, Created: true));
        }
    }

    private sealed class ThrowingProvisioning : IUserProvisioningService
    {
        public int Attempts { get; private set; }

        public Task<UserProvisioningResult> ProvisionAsync(ApplicationUserClaims claims, CancellationToken cancellationToken = default)
        {
            Attempts++;
            throw new HttpRequestException("db unreachable");
        }
    }

    private sealed class FakeScopes(int[]? leagues = null, int[]? teams = null, int[]? offices = null) : IScopeService
    {
        public Task<UserScope> GetScopeAsync(int userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new UserScope(
                (teams ?? []).ToHashSet(), (leagues ?? []).ToHashSet(), (offices ?? []).ToHashSet()));
    }

    private sealed class CapturingReports : IWeeklyReportService
    {
        public WeeklyReportRequest? Request { get; private set; }

        public Task<WeeklyReportResponse> GetAsync(WeeklyReportRequest request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new WeeklyReportResponse(request.StartDate, request.EndDate, request.TeamIds, []));
        }
    }

    private sealed class FakeUsers : IUserService
    {
        public SubscriptionError AddResult { get; init; } = SubscriptionError.None;
        public int? LastAddedEntityId { get; private set; }
        public bool LastRemoveWasAdmin { get; private set; }
        public UserSettingsUpdate? LastSettings { get; private set; }

        public Task<UserSubscriptions> GetSubscriptionsAsync(int userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new UserSubscriptions([], []));

        public Task<CurrentUserProfile?> GetProfileAsync(int userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<CurrentUserProfile?>(new(
                userId, "auth0|middleware-user", null, null, null, false,
                [new UserRoleSummary(1, UserRoleCode.Coach, "Coach", 5)], null, null, new Dictionary<string, string?>()));

        public Task UpdateSettingsAsync(int userId, UserSettingsUpdate update, CancellationToken cancellationToken = default)
        {
            LastSettings = update;
            return Task.CompletedTask;
        }

        public Task<SubscriptionResult> AddTeamSubscriptionAsync(int userId, int teamId, CancellationToken cancellationToken = default)
        {
            LastAddedEntityId = teamId;
            return Task.FromResult(new SubscriptionResult(AddResult, null));
        }

        public Task<SubscriptionError> RemoveTeamSubscriptionAsync(int userId, int teamId, bool isAdmin, CancellationToken cancellationToken = default)
        {
            LastRemoveWasAdmin = isAdmin;
            return Task.FromResult(SubscriptionError.None);
        }

        public Task RequestTeamUnlockAsync(int userId, int teamId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<SubscriptionResult> AddLeagueSubscriptionAsync(int userId, int leagueId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SubscriptionResult(AddResult, null));

        public Task<SubscriptionError> RemoveLeagueSubscriptionAsync(int userId, int leagueId, CancellationToken cancellationToken = default) =>
            Task.FromResult(SubscriptionError.None);

        public Task<SubscriptionResult> AddOrganizationSubscriptionAsync(int userId, int organizationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SubscriptionResult(AddResult, null));

        public Task<SubscriptionError> RemoveOrganizationSubscriptionAsync(int userId, int organizationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(SubscriptionError.None);
    }
}
