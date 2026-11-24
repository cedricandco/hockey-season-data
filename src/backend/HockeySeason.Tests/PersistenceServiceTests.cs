using HockeySeason.Application;
using HockeySeason.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace HockeySeason.Tests;

/// <summary>
/// Persistence use cases exercised over a SQL Server... SQLite in-memory
/// database through the same DbContext the PostgreSQL host uses. The model is
/// provider-agnostic (the extras column adapts per provider), so business
/// rules — idempotent provisioning, licence limits, the 15-day lock — are
/// covered without a PostgreSQL server.
/// </summary>
public sealed class PersistenceServiceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<HockeySeasonDbContext> _options;
    private readonly FixedTimeProvider _time = new(new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero));

    public PersistenceServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _options = new DbContextOptionsBuilder<HockeySeasonDbContext>()
            .UseSqlite(_connection)
            .Options;
    }

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        await using var db = new HockeySeasonDbContext(_options);
        await db.Database.EnsureCreatedAsync();
        // Roles already seeded by the model (HasData): coach/director x5, president x0.
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private HockeySeasonDbContext CreateContext() => new(_options);

    private IDbContextFactory<HockeySeasonDbContext> CreateFactory() =>
        new TestDbContextFactory(_options);

    private ScopeService CreateScopeService(IDbContextFactory<HockeySeasonDbContext> factory) =>
        new(factory, new MemoryCache(new MemoryCacheOptions()));

    private UserService CreateUserService(IDbContextFactory<HockeySeasonDbContext> factory) =>
        new(factory, _time, Options.Create(new AuthorizationOptions()));

    [Fact]
    public async Task Provisioning_CreatesUserOnceAndIsIdempotent()
    {
        var provisioning = new UserProvisioningService(CreateFactory(), NullLogger<UserProvisioningService>.Instance);

        var first = await provisioning.ProvisionAsync(new("auth0|user-a", "a@example.com", "Alice", "A"));
        var second = await provisioning.ProvisionAsync(new("auth0|user-a", "a@example.com", "Alice", "A"));

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(first.UserId, second.UserId);
        await using var db = CreateContext();
        Assert.Equal(1, await db.Users.CountAsync());
        var user = await db.Users.SingleAsync();
        Assert.Equal("Alice", user.FirstName);
        Assert.Equal("A", user.LastName);
        Assert.Equal("a@example.com", user.Email);
    }

    [Fact]
    public async Task Provisioning_GrantsTheCoachRoleToANewAccount()
    {
        var provisioning = new UserProvisioningService(CreateFactory(), NullLogger<UserProvisioningService>.Instance);

        var result = await provisioning.ProvisionAsync(new("auth0|new-coach", null, null, null));

        await using var db = CreateContext();
        var role = await db.UserRoles.SingleAsync(userRole => userRole.UserId == result.UserId);
        Assert.Equal(1, role.RoleId);
        var users = CreateUserService(CreateFactory());
        var limits = (await users.GetLimitsAsync(result.UserId)).ToDictionary(limit => limit.Kind);
        Assert.Equal(5, limits["team"].Max);
        Assert.True(limits["team"].CanAddMore);
    }

    [Fact]
    public async Task Provisioning_FillsMissingProfileClaimsWithoutOverwriting()
    {
        var provisioning = new UserProvisioningService(CreateFactory(), NullLogger<UserProvisioningService>.Instance);

        // First request: the token carried nothing but the subject.
        var created = await provisioning.ProvisionAsync(new("auth0|profile", null, null, null));
        Assert.True(created.Created);

        // The userinfo profile arrives later: names and email are stored.
        await provisioning.ProvisionAsync(new("auth0|profile", "p@example.com", "Patrick", "Roy"));
        await using (var db = CreateContext())
        {
            var user = await db.Users.SingleAsync();
            Assert.Equal("p@example.com", user.Email);
            Assert.Equal("Patrick", user.FirstName);
            Assert.Equal("Roy", user.LastName);
        }

        // A later profile never overwrites what is stored.
        await provisioning.ProvisionAsync(new("auth0|profile", "other@example.com", "Autre", "Nom"));
        await using (var db = CreateContext())
        {
            var user = await db.Users.SingleAsync();
            Assert.Equal("p@example.com", user.Email);
            Assert.Equal("Patrick", user.FirstName);
            Assert.Equal("Roy", user.LastName);
            // The COACH role is granted once only.
            Assert.Equal(1, await db.UserRoles.CountAsync(userRole => userRole.UserId == created.UserId));
        }
    }

    [Fact]
    public async Task UserService_RejectsTeamSubscriptionWithoutCoachRole()
    {
        var factory = CreateFactory();
        var provisioning = new UserProvisioningService(factory, NullLogger<UserProvisioningService>.Instance);
        var userId = (await provisioning.ProvisionAsync(new("auth0|no-role", null, null, null))).UserId;
        // The default COACH licence is removed: the caller holds no team role.
        await using (var db = CreateContext())
        {
            db.UserRoles.RemoveRange(db.UserRoles.Where(userRole => userRole.UserId == userId));
            await db.SaveChangesAsync();
        }
        await SeedTeamAsync(factory, "200", leagueExternalId: "100", divisionExternalId: "10");
        var users = CreateUserService(factory);

        var result = await users.AddTeamSubscriptionAsync(userId, 200);

        Assert.Equal(SubscriptionError.LimitReached, result.Error);
    }

    [Fact]
    public async Task UserService_CoachLimitOfFiveIsEnforced()
    {
        var factory = CreateFactory();
        var users = CreateUserService(factory);
        var userId = await SeedCoachAsync(factory);
        for (var i = 1; i <= 6; i++)
        {
            await SeedTeamAsync(factory, $"{100 + i}", leagueExternalId: "100", divisionExternalId: "10");
        }

        for (var i = 1; i <= 5; i++)
        {
            var result = await users.AddTeamSubscriptionAsync(userId, 100 + i);
            Assert.Equal(SubscriptionError.None, result.Error);
            Assert.NotNull(result.LockedUntil);
        }
        Assert.Equal(SubscriptionError.LimitReached, (await users.AddTeamSubscriptionAsync(userId, 106)).Error);
    }

    [Fact]
    public async Task UserService_TeamLockBlocksRemovalAndAdminBypasses()
    {
        var factory = CreateFactory();
        var users = CreateUserService(factory);
        var userId = await SeedCoachAsync(factory);
        await SeedTeamAsync(factory, "300", leagueExternalId: "100", divisionExternalId: "10");
        Assert.Equal(SubscriptionError.None, (await users.AddTeamSubscriptionAsync(userId, 300)).Error);

        var beforeLockExpiry = await users.RemoveTeamSubscriptionAsync(userId, 300, isAdmin: false);
        Assert.Equal(SubscriptionError.Locked, beforeLockExpiry);

        Assert.Equal(SubscriptionError.None, await users.RemoveTeamSubscriptionAsync(userId, 300, isAdmin: true));
    }

    [Fact]
    public async Task UserService_LimitsReportHeadroomPerKind()
    {
        var factory = CreateFactory();
        var users = CreateUserService(factory);
        var userId = await SeedCoachAsync(factory);
        for (var i = 1; i <= 6; i++)
        {
            await SeedTeamAsync(factory, $"{200 + i}", leagueExternalId: "100", divisionExternalId: "10");
        }

        Assert.Equal(SubscriptionError.None, (await users.AddTeamSubscriptionAsync(userId, 201)).Error);

        var limits = (await users.GetLimitsAsync(userId)).ToDictionary(limit => limit.Kind);
        Assert.Equal(5, limits["team"].Max);
        Assert.Equal(1, limits["team"].Used);
        Assert.True(limits["team"].CanAddMore);
        // No DIRECTOR licence: leagues cannot be followed individually.
        Assert.Equal(0, limits["league"].Max);
        Assert.False(limits["league"].CanAddMore);

        // The add call reports the licence cap with the headroom, so the page
        // can disable the selector without a second round trip.
        for (var i = 2; i <= 5; i++)
        {
            Assert.Equal(SubscriptionError.None, (await users.AddTeamSubscriptionAsync(userId, 200 + i)).Error);
        }
        var rejected = await users.AddTeamSubscriptionAsync(userId, 206);
        Assert.Equal(SubscriptionError.LimitReached, rejected.Error);
        Assert.Equal(5, rejected.Limit!.Used);
        Assert.False(rejected.Limit.CanAddMore);
    }

    [Fact]
    public async Task CallerDataScope_FollowedTeamOpensItsWholeLeague()
    {
        var factory = CreateFactory();
        // One league of division 6193 with two teams; another league of division
        // 6194. Only team 410 is followed.
        await SeedTeamAsync(factory, "410", leagueExternalId: "followed-team-league", divisionExternalId: "6193");
        await SeedTeamAsync(factory, "411", leagueExternalId: "followed-team-league", divisionExternalId: "6193");
        await SeedTeamAsync(factory, "420", leagueExternalId: "other-league", divisionExternalId: "6194");
        await SeedScheduleAsync(factory, "followed-team-league", 194908);
        await SeedScheduleAsync(factory, "other-league", 194910);
        var userId = await SeedFollowerAsync(factory, followedTeamExternalId: "410");
        var service = new CallerDataScopeService(factory);

        var scope = await service.GetAsync(userId);

        Assert.True(scope.IsRestricted);
        // The followed team and its opponents of the same league are readable,
        // with the schedule id read from the database.
        Assert.Equal([410, 411], scope.Teams.Select(team => team.Id).OrderBy(id => id));
        Assert.Equal(194908, scope.Teams.Single(team => team.Id == 410).ScheduleId);
        Assert.Contains(scope.Leagues, league => league.ExternalId == "followed-team-league");
        Assert.Contains(scope.Divisions, division => division.Id == 6193);
        // Another division is out of reach: following a team opens its league,
        // not the whole division.
        Assert.DoesNotContain(scope.Divisions, division => division.Id == 6194);
        Assert.False(scope.ContainsDivision(6194));
        Assert.False(scope.ContainsTeam(420));
    }

    [Fact]
    public async Task GetLeagueTeams_ListsAllTeamsOfAFollowedTeamLeagueAndNothingOtherwise()
    {
        var factory = CreateFactory();
        await SeedTeamAsync(factory, "410", leagueExternalId: "followed-team-league", divisionExternalId: "6193");
        await SeedTeamAsync(factory, "411", leagueExternalId: "followed-team-league", divisionExternalId: "6193");
        await SeedTeamAsync(factory, "420", leagueExternalId: "other-league", divisionExternalId: "6193");
        await SeedTeamAsync(factory, "430", leagueExternalId: "unreached-league", divisionExternalId: "6194");
        await SeedScheduleAsync(factory, "followed-team-league", 194908);
        await SeedScheduleAsync(factory, "other-league", 194910);
        var userId = await SeedFollowerAsync(factory, followedTeamExternalId: "410");
        var service = new CallerDataScopeService(factory);

        var followedLeagueId = await LeagueIdAsync(factory, "followed-team-league");
        var otherLeagueId = await LeagueIdAsync(factory, "other-league");
        var databaseTeamId = await TeamIdAsync(factory, "411");

        // One followed team opens its whole league: every team of it is listed,
        // keyed on database ids with the Spordle id kept for the live calls.
        var teams = await service.GetLeagueTeamsAsync(userId, followedLeagueId);
        Assert.Equal([410, 411], teams.Select(team => team.ExternalId).OrderBy(id => id));
        Assert.Equal(databaseTeamId, teams.Single(team => team.ExternalId == 411).Id);
        Assert.Equal([194908], teams.Single(team => team.ExternalId == 410).Schedules.Select(schedule => schedule.Id));

        // A league without a follow lists nothing, and an unknown league id too.
        Assert.Empty(await service.GetLeagueTeamsAsync(userId, otherLeagueId));
        Assert.Empty(await service.GetLeagueTeamsAsync(userId, 999999));

        // A team of an opened league is selectable directly; one of a closed
        // league is not (the page answers 403).
        Assert.NotNull(await service.GetSelectableTeamAsync(userId, databaseTeamId));
        Assert.Null(await service.GetSelectableTeamAsync(userId, await TeamIdAsync(factory, "420")));

        // A caller who follows nothing sees no league team at all.
        var stranger = (await new UserProvisioningService(factory, NullLogger<UserProvisioningService>.Instance)
            .ProvisionAsync(new($"auth0|stranger-{Guid.NewGuid():N}", null, null, null))).UserId;
        Assert.Empty(await service.GetLeagueTeamsAsync(stranger, followedLeagueId));
        Assert.Null(await service.GetSelectableTeamAsync(stranger, databaseTeamId));
    }

    private static async Task<int> LeagueIdAsync(IDbContextFactory<HockeySeasonDbContext> factory, string externalId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Leagues.Where(league => league.ExternalId == externalId).Select(league => league.Id).SingleAsync();
    }

    private static async Task<int> TeamIdAsync(IDbContextFactory<HockeySeasonDbContext> factory, string externalId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Teams.Where(team => team.ExternalId == externalId).Select(team => team.Id).SingleAsync();
    }

    [Fact]
    public async Task CallerDataScope_ExposesAllTeamsOfAFollowedLeague()
    {
        var factory = CreateFactory();
        await SeedTeamAsync(factory, "430", leagueExternalId: "followed-league", divisionExternalId: "6193");
        await SeedTeamAsync(factory, "431", leagueExternalId: "followed-league", divisionExternalId: "6193");
        await SeedTeamAsync(factory, "440", leagueExternalId: "unfollowed-league", divisionExternalId: "6193");
        await SeedScheduleAsync(factory, "followed-league", 194899);
        var userId = await SeedFollowerAsync(factory, followedLeagueExternalId: "followed-league");
        var service = new CallerDataScopeService(factory);

        var scope = await service.GetAsync(userId);

        Assert.Equal([430, 431], scope.Teams.Select(team => team.Id).OrderBy(id => id));
        Assert.DoesNotContain(scope.Teams, team => team.Id == 440);
        Assert.True(scope.ContainsLeague("followed-league"));
        Assert.False(scope.ContainsLeague("unfollowed-league"));
    }

    [Fact]
    public async Task CallerDataScope_FollowsNothingIsEmptyAndUnrestrictedIsWholeCatalogue()
    {
        var factory = CreateFactory();
        await SeedTeamAsync(factory, "450", leagueExternalId: "some-league", divisionExternalId: "6193");
        await SeedScheduleAsync(factory, "some-league", 194908);
        var provisioning = new UserProvisioningService(factory, NullLogger<UserProvisioningService>.Instance);
        var userId = (await provisioning.ProvisionAsync(new("auth0|nothing", null, null, null))).UserId;
        var service = new CallerDataScopeService(factory);

        var empty = await service.GetAsync(userId);
        Assert.True(empty.IsRestricted);
        Assert.Empty(empty.Teams);
        Assert.Empty(empty.Leagues);
        Assert.Empty(empty.Associations);

        // The temporary open-access phase reads the whole reference data.
        var whole = await service.GetAsync(userId, unrestricted: true);
        Assert.False(whole.IsRestricted);
        Assert.Contains(whole.Teams, team => team.Id == 450);
        Assert.NotEmpty(whole.Associations);
    }

    [Fact]
    public async Task CallerDataScope_FollowedTeamsCarryDatabaseIdsAndParents()
    {
        var factory = CreateFactory();
        await SeedTeamAsync(factory, "410", leagueExternalId: "followed-team-league", divisionExternalId: "6193");
        await SeedTeamAsync(factory, "411", leagueExternalId: "followed-team-league", divisionExternalId: "6193");
        await SeedTeamAsync(factory, "420", leagueExternalId: "other-league", divisionExternalId: "6194");
        await SeedScheduleAsync(factory, "followed-team-league", 194908);
        var userId = await SeedFollowerAsync(factory, followedTeamExternalId: "410");
        var service = new CallerDataScopeService(factory);

        var team = Assert.Single(await service.GetFollowedTeamsAsync(userId));

        // The team is addressed by its database id; the Spordle team id stays
        // available for the live read endpoints.
        await using (var db = CreateContext())
        {
            Assert.Equal(await db.Teams.Where(t => t.ExternalId == "410").Select(t => t.Id).SingleAsync(), team.Id);
        }
        Assert.Equal(410, team.ExternalId);
        Assert.Equal("League", team.LeagueName);
        Assert.Equal("Div", team.DivisionName);
        Assert.Equal("Org", team.AssociationName);
        Assert.True(team.LeagueId > 0);
        Assert.True(team.DivisionId > 0);
        Assert.True(team.AssociationId > 0);
        // The schedule the detail call is keyed on comes from the database.
        Assert.Equal(194908, Assert.Single(team.Schedules).Id);
        // A sibling team of the same league is not a follow, and the other
        // division is out of reach.
        Assert.DoesNotContain(await service.GetFollowedTeamsAsync(userId), item => item.ExternalId == 411);
    }

    [Fact]
    public async Task CallerDataScope_FollowedTeamsIgnoreOpenAccess()
    {
        var factory = CreateFactory();
        await SeedTeamAsync(factory, "460", leagueExternalId: "some-league", divisionExternalId: "6193");
        await SeedScheduleAsync(factory, "some-league", 194908);
        var provisioning = new UserProvisioningService(factory, NullLogger<UserProvisioningService>.Instance);
        var userId = (await provisioning.ProvisionAsync(new("auth0|nothing", null, null, null))).UserId;
        var service = new CallerDataScopeService(factory);

        // The team page and the weekly report are about my teams: a caller who
        // follows nothing never gets the whole catalogue, even under open access.
        Assert.Empty(await service.GetFollowedTeamsAsync(userId));
        Assert.Contains((await service.GetAsync(userId, unrestricted: true)).Teams, team => team.Id == 460);
    }

    [Fact]
    public async Task SubscriptionCatalog_WalksAssociationDivisionLeagueTeam()    {
        var factory = CreateFactory();
        await SeedTeamAsync(factory, "410", leagueExternalId: "13c38dd1-e464-4835-af5f-75be8561daf6", divisionExternalId: "6193");
        var catalog = new SubscriptionCatalogService(factory);

        var association = Assert.Single(await catalog.GetAssociationsAsync());
        var division = Assert.Single(await catalog.GetDivisionsAsync(association.Id));
        Assert.Equal(6193, division.Id);

        var leagues = await catalog.GetLeaguesAsync(division.Id);
        Assert.Equal("2026-27", leagues.SeasonId);
        var league = Assert.Single(leagues.Leagues);
        Assert.Equal("league", league.Name.ToLowerInvariant());

        var team = Assert.Single(await catalog.GetTeamsAsync(league.Id));
        Assert.Equal(410, team.Id);
    }

    [Fact]
    public async Task ScopeService_UnionsDirectTeamsLeaguesAndOrganizations()
    {
        var factory = CreateFactory();
        var users = CreateUserService(factory);
        var provisioning = new UserProvisioningService(factory, NullLogger<UserProvisioningService>.Instance);
        var userId = (await provisioning.ProvisionAsync(new("auth0|scope-user", null, null, null))).UserId;

        // Organizations: org 1 → division 10 → league 100 → team 900.
        await using (var db = CreateContext())
        {
            var organization = new Organization { Name = "Org", ExternalId = "111" };
            db.Organizations.Add(organization);
            await db.SaveChangesAsync();

            var divisionA = new Division { OrganizationId = organization.Id, Name = "Div 10", ExternalId = "10" };
            var divisionB = new Division { OrganizationId = organization.Id, Name = "Div 20", ExternalId = "20" };
            db.Divisions.AddRange(divisionA, divisionB);
            await db.SaveChangesAsync();

            var season = new Season { Name = "2026-27", ExternalId = "2026-27" };
            db.Seasons.Add(season);
            await db.SaveChangesAsync();

            db.Leagues.AddRange(
                new League { DivisionId = divisionA.Id, SeasonId = season.Id, Name = "League 100", ExternalId = "100" },
                new League { DivisionId = divisionB.Id, SeasonId = season.Id, Name = "League 200", ExternalId = "200" });
            await db.SaveChangesAsync();

            db.Teams.Add(new Team { LeagueId = 1, Name = "Team 900", ExternalId = "900" });
            db.Teams.Add(new Team { LeagueId = 2, Name = "Team 901", ExternalId = "901" });
            db.UserRoles.Add(new UserRole { UserId = userId, RoleId = 3 }); // president
            await db.SaveChangesAsync();
        }

        // Follow one team directly (coach) + one league (director) + the org (president).
        var roles = new[]
        {
            new { RoleId = 1, Max = 5 },
            new { RoleId = 2, Max = 5 },
            new { RoleId = 3, Max = 0 },
        };
        await using (var db = CreateContext())
        {
            db.UserRoles.RemoveRange(db.UserRoles.Where(ur => ur.UserId == userId));
            db.UserRoles.AddRange(roles.Select(role => new UserRole { UserId = userId, RoleId = (short)role.RoleId }));
            await db.SaveChangesAsync();
        }

        Assert.Equal(SubscriptionError.None, (await users.AddTeamSubscriptionAsync(userId, 900)).Error);
        Assert.Equal(SubscriptionError.None, (await users.AddLeagueSubscriptionAsync(userId, "200")).Error);
        Assert.Equal(SubscriptionError.None, (await users.AddOrganizationSubscriptionAsync(userId, 111)).Error);

        var scope = await CreateScopeService(factory).GetScopeAsync(userId);

        Assert.Contains(900, scope.SpordleTeamIds);
        Assert.Contains(901, scope.SpordleTeamIds); // via league 200
        Assert.Contains("100", scope.SpordleLeagueIds); // owning league of team 900
        Assert.Contains("200", scope.SpordleLeagueIds); // followed league
        Assert.Contains(10, scope.SpordleOfficeIds);  // division of league 100
        Assert.Contains(20, scope.SpordleOfficeIds);  // division of league 200 (org scope)
    }

    [Fact]
    public async Task UserService_SettingsRoundTrip()
    {
        var factory = CreateFactory();
        var provisioning = new UserProvisioningService(factory, NullLogger<UserProvisioningService>.Instance);
        var userId = (await provisioning.ProvisionAsync(new("auth0|settings", null, null, null))).UserId;
        var users = CreateUserService(factory);

        await users.UpdateSettingsAsync(userId, new("#0af", null, new Dictionary<string, string?> { ["lang"] = "fr" }));
        var profile = await users.GetProfileAsync(userId);

        Assert.NotNull(profile);
        Assert.Equal("#0af", profile.PrimaryColor);
        Assert.Null(profile.SecondaryColor); // untouched
        Assert.Equal("fr", profile.Extra["lang"]);

        await users.UpdateSettingsAsync(userId, new(null, "#abc", null));
        var updated = await users.GetProfileAsync(userId);
        Assert.Equal("#0af", updated!.PrimaryColor); // remains
        Assert.Equal("#abc", updated.SecondaryColor);
    }

    private async Task<int> SeedCoachAsync(IDbContextFactory<HockeySeasonDbContext> factory)
    {
        var provisioning = new UserProvisioningService(factory, NullLogger<UserProvisioningService>.Instance);
        // Provisioning already grants the default COACH licence.
        return (await provisioning.ProvisionAsync(new("auth0|coach", null, null, null))).UserId;
    }

    private static async Task SeedTeamAsync(IDbContextFactory<HockeySeasonDbContext> factory, string teamExternalId, string leagueExternalId, string divisionExternalId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var season = await db.Seasons.FirstOrDefaultAsync(s => s.ExternalId == "2026-27");
        if (season is null)
        {
            season = new() { Name = "2026-27", ExternalId = "2026-27" };
            db.Seasons.Add(season);
            await db.SaveChangesAsync();
        }

        var organization = await db.Organizations.FirstOrDefaultAsync(o => o.ExternalId == "1");
        if (organization is null)
        {
            organization = new() { Name = "Org", ExternalId = "1" };
            db.Organizations.Add(organization);
            await db.SaveChangesAsync();
        }

        var division = await db.Divisions.FirstOrDefaultAsync(d => d.ExternalId == divisionExternalId);
        if (division is null)
        {
            division = new() { OrganizationId = organization.Id, Name = "Div", ExternalId = divisionExternalId };
            db.Divisions.Add(division);
            await db.SaveChangesAsync();
        }

        var league = await db.Leagues.FirstOrDefaultAsync(l => l.ExternalId == leagueExternalId);
        if (league is null)
        {
            league = new() { DivisionId = division.Id, SeasonId = season.Id, Name = "League", ExternalId = leagueExternalId };
            db.Leagues.Add(league);
            await db.SaveChangesAsync();
        }

        if (!await db.Teams.AnyAsync(t => t.ExternalId == teamExternalId))
        {
            db.Teams.Add(new() { LeagueId = league.Id, Name = $"Team {teamExternalId}", ExternalId = teamExternalId });
        }
        await db.SaveChangesAsync();
    }

    /// <summary>Stores a Spordle schedule under the league with the given external id.</summary>
    private static async Task SeedScheduleAsync(IDbContextFactory<HockeySeasonDbContext> factory, string leagueExternalId, int scheduleId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var league = await db.Leagues.SingleAsync(league => league.ExternalId == leagueExternalId);
        if (!await db.LeagueSchedules.AnyAsync(schedule => schedule.ExternalId == scheduleId.ToString()))
        {
            db.LeagueSchedules.Add(new LeagueSchedule
            {
                LeagueId = league.Id,
                ExternalId = scheduleId.ToString(),
                Name = $"Schedule {scheduleId}",
            });
            await db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Provisions a user and records the requested follows (team, league or
    /// organization, by Spordle external id).
    /// </summary>
    private static async Task<int> SeedFollowerAsync(
        IDbContextFactory<HockeySeasonDbContext> factory,
        string? followedTeamExternalId = null,
        string? followedLeagueExternalId = null)
    {
        var provisioning = new UserProvisioningService(factory, NullLogger<UserProvisioningService>.Instance);
        var userId = (await provisioning.ProvisionAsync(new($"auth0|follower-{Guid.NewGuid():N}", null, null, null))).UserId;

        await using var db = await factory.CreateDbContextAsync();
        if (followedTeamExternalId is not null)
        {
            var teamId = await db.Teams.Where(team => team.ExternalId == followedTeamExternalId).Select(team => team.Id).SingleAsync();
            db.TeamSubscriptions.Add(new TeamSubscription
            {
                UserId = userId,
                TeamId = teamId,
                CreatedAt = DateTimeOffset.UnixEpoch,
                UpdatedAt = DateTimeOffset.UnixEpoch,
                LockedUntil = DateTimeOffset.UnixEpoch,
            });
        }
        if (followedLeagueExternalId is not null)
        {
            var leagueId = await db.Leagues.Where(league => league.ExternalId == followedLeagueExternalId).Select(league => league.Id).SingleAsync();
            db.LeagueSubscriptions.Add(new LeagueSubscription
            {
                UserId = userId,
                LeagueId = leagueId,
                CreatedAt = DateTimeOffset.UnixEpoch,
                UpdatedAt = DateTimeOffset.UnixEpoch,
            });
        }
        await db.SaveChangesAsync();
        return userId;
    }

    private sealed class TestDbContextFactory(DbContextOptions<HockeySeasonDbContext> options)
        : IDbContextFactory<HockeySeasonDbContext>
    {
        public HockeySeasonDbContext CreateDbContext() => new(options);
        public ValueTask<HockeySeasonDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new HockeySeasonDbContext(options));
    }
}
