using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using HockeySeason.Application;
using HockeySeason.Auth;
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
        // The team count tells the SPA whether the account still has to pick a
        // team on first run (0 here).
        Assert.Equal(0, json.RootElement.GetProperty("teamCount").GetInt32());
        Assert.Single(provisioning.Provisioned);
    }

    [Fact]
    public async Task Resolver_CompletesTheProfileFromUserinfo_WhenTheStoredProfileIsIncomplete()
    {
        var provisioning = new FakeProvisioning { ProfileIncomplete = true };
        var userContext = new FakeUserContext(new UserInfo(
            "auth0|middleware-user", "jdoe", "Jean Tremblay", null, null, "jean@example.com", true));
        var resolver = new RequestUserResolver(provisioning, userContext);
        var (context, _) = CreateContext(provisioning);

        await resolver.GetOrCreateUserIdAsync(context, CancellationToken.None);

        Assert.Equal(2, provisioning.Provisioned.Count);
        var claims = provisioning.Provisioned[^1];
        Assert.Equal("jean@example.com", claims.Email);
        // Auth0 fills "name" with "Prénom Nom" for database signups.
        Assert.Equal("Jean", claims.FirstName);
        Assert.Equal("Tremblay", claims.LastName);
        Assert.Equal(1, userContext.Calls);
    }

    [Fact]
    public async Task Resolver_SkipsUserinfo_WhenTheStoredProfileIsComplete()
    {
        var provisioning = new FakeProvisioning { ProfileIncomplete = false };
        var userContext = new FakeUserContext(new UserInfo(
            "auth0|middleware-user", null, "Autre Nom", null, null, "other@example.com", true));
        var resolver = new RequestUserResolver(provisioning, userContext);
        var (context, _) = CreateContext(provisioning, claims:
        [
            new(JwtRegisteredClaimNames.Email, "m@example.com"),
            new(JwtRegisteredClaimNames.GivenName, "Marc"),
            new(JwtRegisteredClaimNames.FamilyName, "Gagnon"),
        ]);

        await resolver.GetOrCreateUserIdAsync(context, CancellationToken.None);

        var claims = Assert.Single(provisioning.Provisioned);
        Assert.Equal("m@example.com", claims.Email);
        Assert.Equal("Marc", claims.FirstName);
        Assert.Equal("Gagnon", claims.LastName);
        Assert.Equal(0, userContext.Calls);
    }

    [Fact]
    public async Task Resolver_DoesNotSplitADisplayNameThatIsAnEmail()
    {
        var provisioning = new FakeProvisioning { ProfileIncomplete = true };
        var userContext = new FakeUserContext(new UserInfo(
            "auth0|middleware-user", "jdoe", "jdoe@example.com", null, null, null, null));
        var resolver = new RequestUserResolver(provisioning, userContext);
        var (context, _) = CreateContext(provisioning);

        await resolver.GetOrCreateUserIdAsync(context, CancellationToken.None);

        // The profile brought nothing usable: the row is not re-provisioned.
        var claims = Assert.Single(provisioning.Provisioned);
        Assert.Null(claims.FirstName);
        Assert.Null(claims.LastName);
        Assert.Equal(1, userContext.Calls);
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
    [InlineData(SubscriptionError.Locked, StatusCodes.Status423Locked)]
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

    [Fact]
    public async Task Subscriptions_ExposeFollowsAndLicenceLimits()
    {
        var provisioning = new FakeProvisioning();
        var (function, _) = CreateUserFunctions(new FakeUsers(), provisioning);
        var (context, request) = CreateContext(provisioning);

        var result = Assert.IsType<OkObjectResult>(await function.Subscriptions(request, context, CancellationToken.None));

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var limit = json.RootElement.GetProperty("limits")[0];
        Assert.Equal("team", limit.GetProperty("kind").GetString());
        Assert.Equal(5, limit.GetProperty("max").GetInt32());
        Assert.Equal(0, limit.GetProperty("used").GetInt32());
        Assert.True(limit.GetProperty("canAddMore").GetBoolean());
        Assert.Equal(JsonValueKind.Array, json.RootElement.GetProperty("organizations").ValueKind);
    }

    [Fact]
    public async Task SubscriptionCatalog_IsReadableByAnyAuthenticatedCaller()
    {
        var provisioning = new FakeProvisioning();
        var (function, _) = CreateUserFunctions(new FakeUsers(), provisioning);
        var (context, request) = CreateContext(provisioning);

        var associations = Assert.IsType<OkObjectResult>(await function.Associations(request, context, CancellationToken.None));
        using var body = JsonDocument.Parse(JsonSerializer.Serialize(associations.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(1, body.RootElement.GetProperty("associations")[0].GetProperty("id").GetInt32());

        Assert.IsType<OkObjectResult>(await function.Divisions(request, 1, context, CancellationToken.None));
        Assert.IsType<OkObjectResult>(await function.LeagueCatalog(request, 6193, context, CancellationToken.None));
        Assert.IsType<OkObjectResult>(await function.TeamCatalog(request, "13c38dd1-e464-4835-af5f-75be8561daf6", context, CancellationToken.None));

        var anonymousRequest = new DefaultHttpContext().Request;
        var anonymousContext = FunctionsTests.CreateContextWith((DefaultHttpContext)anonymousRequest.HttpContext, BuildServices(provisioning));
        Assert.Equal(StatusCodes.Status401Unauthorized,
            Assert.IsType<ObjectResult>(await function.Associations(anonymousRequest, anonymousContext, CancellationToken.None)).StatusCode);
    }

    // ─── Scope enforcement ─────────────────────────────────────────────────

    [Fact]
    public async Task TeamFunctions_RejectOutOfScopeLeaguesAndTeams()
    {
        var provisioning = new FakeProvisioning();
        var function = new TeamFunctions(
            null!, null!, Options.Create(new LeagueApiOptions()),
            new FakeDataScope(FollowedScope()), new RequestUserResolver(provisioning), WithDatabase());
        var (context, request) = CreateContext(provisioning);

        // Schedule 999 is not followed, and team 2 is not one of the follows.
        Assert.Equal(StatusCodes.Status403Forbidden,
            Assert.IsType<StatusCodeResult>(await function.LeagueTeams(request, 999, context, CancellationToken.None)).StatusCode);
        Assert.Equal(StatusCodes.Status403Forbidden,
            Assert.IsType<StatusCodeResult>(await function.TeamDetail(request, 100, 2, context, CancellationToken.None)).StatusCode);
        Assert.Equal(StatusCodes.Status403Forbidden,
            Assert.IsType<StatusCodeResult>(await function.Leagues(request, 6194, context, CancellationToken.None)).StatusCode);
    }

    [Fact]
    public async Task WeeklyReport_DropsOutOfScopeIdsAndForbidsFullyOutOfScopeRequests()
    {
        var provisioning = new FakeProvisioning();
        var reports = new CapturingReports();
        var function = new WeeklyReportFunction(
            reports, new FakeDataScope(FollowedScope()), new RequestUserResolver(provisioning), WithDatabase());
        var (context, request) = CreateContext(provisioning);

        // The report is derived from the follows: no client-supplied ids.
        request.QueryString = new QueryString("?startDate=2026-11-02&endDate=2026-11-09");
        Assert.IsType<OkObjectResult>(await function.Run(request, context, CancellationToken.None));
        Assert.Equal([194899], reports.Request!.Value.Leagues.Select(league => league.ScheduleId));
        Assert.Equal([182497], reports.Request.Value.Leagues.SelectMany(league => league.TeamIds));

        request.QueryString = new QueryString("?startDate=2026-11-09&endDate=2026-11-02");
        Assert.IsType<BadRequestObjectResult>(await function.Run(request, context, CancellationToken.None));
    }

    [Fact]
    public async Task WeeklyReport_IsEmptyWhenTheCallerFollowsNothing()
    {
        var provisioning = new FakeProvisioning();
        var reports = new CapturingReports();
        var function = new WeeklyReportFunction(
            reports, new FakeDataScope(CallerDataScope.NothingFollowed()), new RequestUserResolver(provisioning), WithDatabase());
        var (context, request) = CreateContext(provisioning);
        request.QueryString = new QueryString("?startDate=2026-11-02&endDate=2026-11-09");

        Assert.IsType<OkObjectResult>(await function.Run(request, context, CancellationToken.None));
        Assert.Empty(reports.Request!.Value.Leagues);
    }

    // ─── Admin import guard ────────────────────────────────────────────────

    [Fact]
    public async Task AdminImport_RejectsNonAdminsAndAnonymousCallers()
    {
        var provisioning = new FakeProvisioning();
        // Configuration without Authorization:OpenAccess — the admin guard applies.
        var function = new AdminImportFunctions(new FakeReferenceData(), Options.Create(new LeagueApiOptions()),
            new ConfigurationBuilder().Build(), NullLogger<AdminImportFunctions>.Instance);
        var (memberContext, request) = CreateContext(provisioning);

        Assert.Equal(StatusCodes.Status403Forbidden,
            Assert.IsType<StatusCodeResult>(await function.ImportOffices(request, memberContext, CancellationToken.None)).StatusCode);
        Assert.Equal(StatusCodes.Status403Forbidden,
            Assert.IsType<StatusCodeResult>(function.ImportLeagues(request, memberContext)).StatusCode);
        Assert.Equal(StatusCodes.Status403Forbidden,
            Assert.IsType<StatusCodeResult>(function.ImportTeams(request, memberContext)).StatusCode);

        var anonymousRequest = new DefaultHttpContext().Request;
        var anonymousContext = FunctionsTests.CreateContextWith((DefaultHttpContext)anonymousRequest.HttpContext, BuildServices(provisioning));
        Assert.Equal(StatusCodes.Status401Unauthorized,
            Assert.IsType<ObjectResult>(await function.ImportOffices(anonymousRequest, anonymousContext, CancellationToken.None)).StatusCode);
    }

    [Fact]
    public async Task OpenAccess_LetsAnyAuthenticatedCallerSkipScopeAndAdminGuards()
    {
        var provisioning = new FakeProvisioning();

        // Open access: the whole reference data is readable, whoever the caller is.
        var reports = new CapturingReports();
        var wholeCatalogue = new CallerDataScope(
            false,
            [new ScopedAssociation(1, "Hockey Richelieu")],
            [new ScopedDivision(6193, "Simple lettre", 1)],
            [new ScopedLeague(1, "league-guid", "M13 B", "2026-27", 6193, [194899])],
            [new ScopedTeam(182497, "Grizzlys", "league-guid", 194899)]);
        var reportFunction = new WeeklyReportFunction(
            reports, new FakeDataScope(wholeCatalogue), new RequestUserResolver(provisioning), WithOpenAccessAndDatabase());
        var (context, request) = CreateContext(provisioning);
        request.QueryString = new QueryString("?startDate=2026-11-02&endDate=2026-11-09");
        Assert.IsType<OkObjectResult>(await reportFunction.Run(request, context, CancellationToken.None));
        Assert.Equal([194899], reports.Request!.Value.Leagues.Select(league => league.ScheduleId));
        Assert.Equal([182497], reports.Request.Value.Leagues.SelectMany(league => league.TeamIds));

        // The report is driven by the followed teams, never by open access: a
        // caller who follows nothing gets an empty report even here.
        var emptyReports = new CapturingReports();
        var emptyFunction = new WeeklyReportFunction(
            emptyReports, new FakeDataScope(CallerDataScope.Unrestricted([], [], [], [])),
            new RequestUserResolver(provisioning), WithOpenAccessAndDatabase());
        Assert.IsType<OkObjectResult>(await emptyFunction.Run(request, context, CancellationToken.None));
        Assert.Empty(emptyReports.Request!.Value.Leagues);

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
        var imports = new AdminImportFunctions(new FakeReferenceData(), Options.Create(new LeagueApiOptions()),
            WithOpenAccess(), NullLogger<AdminImportFunctions>.Instance);
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

    private static IConfiguration WithOpenAccessAndDatabase() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authorization:OpenAccess"] = "true",
                ["ConnectionStrings:Postgres"] = "Host=test",
            })
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
        FakeUsers users, IUserProvisioningService? provisioning = null, ISubscriptionCatalogService? catalog = null)
    {
        var resolver = new RequestUserResolver(provisioning ?? new FakeProvisioning());
        return (new UserFunctions(resolver, users, catalog ?? new FakeCatalog()), resolver);
    }

    private sealed class FakeCatalog : ISubscriptionCatalogService
    {
        public Task<IReadOnlyList<SubscriptionAssociation>> GetAssociationsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SubscriptionAssociation>>([new(1, "Hockey Richelieu")]);

        public Task<IReadOnlyList<SubscriptionDivision>> GetDivisionsAsync(int associationId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SubscriptionDivision>>([new(6193, "Simple lettre")]);

        public Task<SubscriptionLeagueOptions> GetLeaguesAsync(int divisionId, string? seasonId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SubscriptionLeagueOptions(seasonId ?? "2026-27", [new("13c38dd1-e464-4835-af5f-75be8561daf6", "M13 B", seasonId ?? "2026-27")]));

        public Task<IReadOnlyList<SubscriptionTeam>> GetTeamsAsync(string leagueId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SubscriptionTeam>>([new(101, "Alpha")]);
    }

    private sealed class FakeProvisioning : IUserProvisioningService
    {
        public List<ApplicationUserClaims> Provisioned { get; } = [];

        /// <summary>Whether the stored profile is reported incomplete (drives the userinfo call).</summary>
        public bool ProfileIncomplete { get; init; }

        public Task<UserProvisioningResult> ProvisionAsync(ApplicationUserClaims claims, CancellationToken cancellationToken = default)
        {
            Provisioned.Add(claims);
            return Task.FromResult(new UserProvisioningResult(UserId: 7, Created: true, ProfileIncomplete));
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

    /// <summary>Identity-provider profile stub: the token carried no names.</summary>
    private sealed class FakeUserContext(UserInfo profile) : IUserContext
    {
        public int Calls { get; private set; }

        public Task<UserInfo?> GetCurrentUserAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult<UserInfo?>(profile);
        }
    }


    private sealed class FakeScopes(int[]? leagues = null, int[]? teams = null, int[]? offices = null) : IScopeService
    {
        public Task<UserScope> GetScopeAsync(int userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new UserScope(
                (teams ?? []).ToHashSet(),
                (leagues ?? []).Select(id => id.ToString()).ToHashSet(),
                (offices ?? []).ToHashSet(),
                (leagues ?? []).Select(id => id.ToString()).ToHashSet()));
    }

    /// <summary>The caller's followed catalogue, as the report function consumes it.</summary>
    private static CallerDataScope FollowedScope() => new(
        true,
        [new ScopedAssociation(1, "Hockey Richelieu")],
        [new ScopedDivision(6193, "Simple lettre", 1)],
        [new ScopedLeague(1, "league-guid", "M13 B", "2026-27", 6193, [194899])],
        [new ScopedTeam(182497, "Grizzlys", "league-guid", 194899)]);

    private sealed class FakeDataScope(CallerDataScope scope) : ICallerDataScopeService
    {
        public Task<CallerDataScope> GetAsync(int userId, bool unrestricted = false, CancellationToken cancellationToken = default) =>
            Task.FromResult(scope);

        public Task<CallerDataScope> GetForSubjectAsync(string? auth0Subject, bool unrestricted = false, CancellationToken cancellationToken = default) =>
            Task.FromResult(scope);

        public Task<IReadOnlyList<FollowedTeam>> GetFollowedTeamsAsync(
            int userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(FunctionsTests.FollowedTeamsFrom(scope));
    }

    private sealed class FakeReferenceData : IReferenceDataService
    {
        public Task<IReadOnlyList<ReferenceOrganization>> GetOrganizationsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ReferenceOrganization>>([]);

        public Task<IReadOnlyList<ReferenceSeason>> GetSeasonsAsync(int officeId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ReferenceSeason>>([]);

        public Task<ReferenceOrganization> EnsureOrganizationAsync(
            string externalId, string name, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ReferenceOrganization(1, name, externalId));

        public Task<ReferenceImportResult> ImportAsync(ReferenceImportRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ReferenceImportResult(
                "Association", "Office", request.SeasonId, [], [], [], [], 0, 0, 0, []));
    }

    private sealed class CapturingReports : IWeeklyReportService
    {
        public (DateOnly Start, DateOnly End, IReadOnlyList<WeeklyReportLeague> Leagues)? Request { get; private set; }

        public Task<WeeklyReportResponse> GetAsync(
            DateOnly startDate, DateOnly endDate, IReadOnlyList<WeeklyReportLeague> leagues,
            CancellationToken cancellationToken)
        {
            Request = (startDate, endDate, leagues);
            return Task.FromResult(new WeeklyReportResponse(
                startDate, endDate, [.. leagues.SelectMany(league => league.TeamIds).Distinct()], []));
        }
    }

    private sealed class FakeUsers : IUserService
    {
        public SubscriptionError AddResult { get; init; } = SubscriptionError.None;
        public int? LastAddedEntityId { get; private set; }
        public bool LastRemoveWasAdmin { get; private set; }
        public UserSettingsUpdate? LastSettings { get; private set; }

        public Task<UserSubscriptions> GetSubscriptionsAsync(int userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new UserSubscriptions([], [], []));

        public Task<IReadOnlyList<SubscriptionLimit>> GetLimitsAsync(int userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SubscriptionLimit>>([new("team", 5, 0)]);

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

        public Task<SubscriptionResult> AddLeagueSubscriptionAsync(int userId, string leagueId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SubscriptionResult(AddResult, null));

        public Task<SubscriptionError> RemoveLeagueSubscriptionAsync(int userId, string leagueId, CancellationToken cancellationToken = default) =>
            Task.FromResult(SubscriptionError.None);

        public Task<SubscriptionResult> AddOrganizationSubscriptionAsync(int userId, int organizationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SubscriptionResult(AddResult, null));

        public Task<SubscriptionError> RemoveOrganizationSubscriptionAsync(int userId, int organizationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(SubscriptionError.None);
    }
}
