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
    }

    [Fact]
    public async Task UserService_RejectsTeamSubscriptionWithoutCoachRole()
    {
        var factory = CreateFactory();
        var provisioning = new UserProvisioningService(factory, NullLogger<UserProvisioningService>.Instance);
        var userId = (await provisioning.ProvisionAsync(new("auth0|no-role", null, null, null))).UserId;
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
        Assert.Equal(SubscriptionError.LimitReached, beforeLockExpiry);

        Assert.Equal(SubscriptionError.None, await users.RemoveTeamSubscriptionAsync(userId, 300, isAdmin: true));
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
        Assert.Equal(SubscriptionError.None, (await users.AddLeagueSubscriptionAsync(userId, 200)).Error);
        Assert.Equal(SubscriptionError.None, (await users.AddOrganizationSubscriptionAsync(userId, 111)).Error);

        var scope = await CreateScopeService(factory).GetScopeAsync(userId);

        Assert.Contains(900, scope.SpordleTeamIds);
        Assert.Contains(901, scope.SpordleTeamIds); // via league 200
        Assert.Contains(100, scope.SpordleLeagueIds); // owning league of team 900
        Assert.Contains(200, scope.SpordleLeagueIds); // followed league
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
        var userId = (await provisioning.ProvisionAsync(new("auth0|coach", null, null, null))).UserId;
        await using var db = await factory.CreateDbContextAsync();
        db.UserRoles.Add(new() { UserId = userId, RoleId = 1 });
        await db.SaveChangesAsync();
        return userId;
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

    private sealed class TestDbContextFactory(DbContextOptions<HockeySeasonDbContext> options)
        : IDbContextFactory<HockeySeasonDbContext>
    {
        public HockeySeasonDbContext CreateDbContext() => new(options);
        public ValueTask<HockeySeasonDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new HockeySeasonDbContext(options));
    }
}
