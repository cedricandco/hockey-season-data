using System.Net;
using System.Text;
using HockeySeason.Application;
using HockeySeason.Infrastructure.Data;
using HockeySeason.LeagueApi;
using HockeySeason.LeagueApi.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace HockeySeason.Tests;

/// <summary>
/// Reference-data import: the Spordle categories become leagues (French name),
/// their teams are grouped under them, and a second run neither duplicates nor
/// rewrites anything. Deletions cascade to the user subscriptions.
/// </summary>
public sealed class ReferenceDataTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Id of the association seeded by <see cref="SeedOrganizationAsync"/>.</summary>
    private const int OrganizationId = 1;

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<HockeySeasonDbContext> _options;

    public ReferenceDataTests() =>
        _options = new DbContextOptionsBuilder<HockeySeasonDbContext>().UseSqlite(_connection).Options;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        await using var db = new HockeySeasonDbContext(_options);
        await db.Database.EnsureCreatedAsync();
        await SeedOrganizationAsync(db);
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    /// <summary>The association picked in the page: the import targets its id.</summary>
    private static async Task SeedOrganizationAsync(HockeySeasonDbContext db)
    {
        db.Organizations.Add(new Organization
        {
            Id = OrganizationId,
            Name = "HOCKEY RICHELIEU",
            ExternalId = "6157",
            CreatedAt = Now,
            UpdatedAt = Now,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Import_CreatesLeaguesWithTheFrenchNameAndTheirTeams()
    {
        var handler = new SpordleHandler();
        var service = CreateService(handler);

        var result = await service.ImportAsync(new(OrganizationId, "6193", "2026-27"));

        Assert.Equal("HOCKEY RICHELIEU", result.OrganizationName);
        Assert.Equal("LIGUE DE HOCKEY RÉCRÉATIVE DU RICHELIEU", result.OfficeName);
        Assert.Equal(["M13 B", "M15 A"], result.CreatedLeagues); // French names, sorted by category
        Assert.Equal(3, result.CreatedTeams);

        await using var db = new HockeySeasonDbContext(_options);
        var league = await db.Leagues
            .Include(l => l.Teams)
            .Include(l => l.Season)
            .Include(l => l.Division)
            .SingleAsync(l => l.ExternalId == "13c38dd1-e464-4835-af5f-75be8561daf6");
        Assert.Equal("M13 B", league.Name);
        Assert.Equal("6193", league.Division.ExternalId);
        Assert.Equal("2026-27", league.Season.ExternalId);
        Assert.Equal(["Alpha", "Beta"], league.Teams.Select(team => team.Name).OrderBy(name => name));

        // Two schedules of the category are unioned by team id: no duplicate.
        Assert.Equal(2, league.Teams.Count);
        Assert.Equal("HOCKEY RICHELIEU", (await db.Organizations.SingleAsync()).Name);
    }

    [Fact]
    public async Task Import_ToleratesSchedulesWithoutSpecialTeamStats()
    {
        // A tournament schedule publishes standings rows without
        // powerplayPct/penaltyKillPct (and without division/gender/shortName):
        // its teams must still be imported instead of aborting the whole import.
        var handler = new SpordleHandler();
        handler.SchedulesWithoutSpecialTeams.Add(194909);
        var service = CreateService(handler);

        var result = await service.ImportAsync(new(OrganizationId, "6193", "2026-27"));

        Assert.Equal(["M13 B", "M15 A"], result.CreatedLeagues);
        Assert.Equal(3, result.CreatedTeams);
        Assert.Empty(result.Warnings);

        await using var db = new HockeySeasonDbContext(_options);
        var league = await db.Leagues
            .Include(l => l.Teams)
            .SingleAsync(l => l.ExternalId == "13c38dd1-e464-4835-af5f-75be8561daf6");
        // The tournament schedule and the regular one are unioned by team id.
        Assert.Equal(["Alpha", "Beta"], league.Teams.Select(team => team.Name).OrderBy(name => name));
    }

    [Fact]
    public async Task Import_SkipsAStandingsPlaceholderWithoutATeam()
    {
        // Placeholder and bracket rows carry no team: they must be ignored
        // instead of failing the schedule (and the import with it).
        var handler = new SpordleHandler();
        handler.SchedulesWithPlaceholderRows.Add(194909);
        var service = CreateService(handler);

        var result = await service.ImportAsync(new(OrganizationId, "6193", "2026-27"));

        Assert.Equal(3, result.CreatedTeams);
        Assert.Empty(result.Warnings);

        await using var db = new HockeySeasonDbContext(_options);
        var league = await db.Leagues
            .Include(l => l.Teams)
            .SingleAsync(l => l.ExternalId == "13c38dd1-e464-4835-af5f-75be8561daf6");
        Assert.Equal(["Alpha", "Beta"], league.Teams.Select(team => team.Name).OrderBy(name => name));
    }

    [Fact]
    public async Task Import_SkipsAScheduleWhoseStandingsCannotBeRead()
    {
        // An unreadable payload costs that schedule alone: the other schedules of
        // the category still import, with a warning naming what was skipped.
        var handler = new SpordleHandler();
        handler.SchedulesWithUnreadableStandings.Add(194909);
        var service = CreateService(handler);

        var result = await service.ImportAsync(new(OrganizationId, "6193", "2026-27"));

        Assert.Equal(["M13 B", "M15 A"], result.CreatedLeagues);
        Assert.Contains(result.Warnings, warning => warning.Contains("194909", StringComparison.Ordinal));

        await using var db = new HockeySeasonDbContext(_options);
        var league = await db.Leagues
            .Include(l => l.Teams)
            .SingleAsync(l => l.ExternalId == "13c38dd1-e464-4835-af5f-75be8561daf6");
        Assert.Equal(["Alpha", "Beta"], league.Teams.Select(team => team.Name).OrderBy(name => name));
    }

    [Fact]
    public async Task Import_IsIdempotentAndUpdatesInsteadOfDuplicating()
    {
        var handler = new SpordleHandler();
        var service = CreateService(handler);
        await service.ImportAsync(new(OrganizationId, "6193", "2026-27"));

        var second = await service.ImportAsync(new(OrganizationId, "6193", "2026-27"));

        Assert.Empty(second.CreatedLeagues);
        Assert.Empty(second.UpdatedLeagues);
        Assert.Equal(["M13 B", "M15 A"], second.UnchangedLeagues);
        Assert.Equal(0, second.CreatedTeams);
        Assert.Equal(0, second.DeletedTeams);

        await using var db = new HockeySeasonDbContext(_options);
        Assert.Equal(2, await db.Leagues.CountAsync());
        Assert.Equal(3, await db.Teams.CountAsync());

        // A renamed team is updated in place, never re-created.
        handler.TeamName = "Alpha Renamed";
        var third = await service.ImportAsync(new(OrganizationId, "6193", "2026-27"));
        Assert.Equal(1, third.UpdatedTeams);
        Assert.Equal("Alpha Renamed",
            (await db.Teams.SingleAsync(team => team.ExternalId == "101")).Name);
        Assert.Equal(3, await db.Teams.CountAsync());
    }

    [Fact]
    public async Task Import_DeletesVanishedLeaguesAndTeamsWithTheirSubscriptions()
    {
        var handler = new SpordleHandler();
        var service = CreateService(handler);
        await service.ImportAsync(new(OrganizationId, "6193", "2026-27"));

        var userId = await SeedUserFollowingAsync(teamExternalId: "102", leagueExternalId: "21c7f8f9-e0a4-416d-902a-50a6e68bf834");

        // Beta (102) disappears and the whole M15 A category is withdrawn.
        handler.Categories.RemoveAll(category => category.Id == "21c7f8f9-e0a4-416d-902a-50a6e68bf834");
        handler.Schedules.Remove(194910);
        handler.Remove("102");

        var result = await service.ImportAsync(new(OrganizationId, "6193", "2026-27"));

        Assert.Equal(["M15 A"], result.DeletedLeagues);
        Assert.Equal(2, result.DeletedTeams); // Beta + the single team of M15 A

        await using var db = new HockeySeasonDbContext(_options);
        Assert.Single(await db.Leagues.ToListAsync());
        Assert.Equal("101", (await db.Teams.SingleAsync()).ExternalId);
        // The subscription of a vanished team cannot stay in a scope, and a
        // league that disappeared takes its own subscriptions with it.
        Assert.Empty(await db.TeamSubscriptions.ToListAsync());
        Assert.Empty(await db.LeagueSubscriptions.ToListAsync());
        Assert.True(await db.Users.AnyAsync(user => user.Id == userId));
    }

    [Fact]
    public async Task Import_KeepsLeaguesOfOtherSeasons()
    {
        var handler = new SpordleHandler();
        var service = CreateService(handler);
        await service.ImportAsync(new(OrganizationId, "6193", "2026-27"));

        await using (var db = new HockeySeasonDbContext(_options))
        {
            var previous = new Season { Name = "2025-26", ExternalId = "2025-26" };
            db.Seasons.Add(previous);
            await db.SaveChangesAsync();
            var division = await db.Divisions.SingleAsync();
            var oldLeague = new League
            {
                DivisionId = division.Id,
                SeasonId = previous.Id,
                Name = "M13 B",
                ExternalId = "13c38dd1-e464-4835-af5f-75be8561daf6",
            };
            db.Leagues.Add(oldLeague);
            await db.SaveChangesAsync();
            db.Teams.Add(new Team { LeagueId = oldLeague.Id, Name = "Legacy", ExternalId = "900" });
            await db.SaveChangesAsync();
        }

        await service.ImportAsync(new(OrganizationId, "6193", "2026-27"));

        await using var check = new HockeySeasonDbContext(_options);
        // The same category id in two seasons is two leagues, and importing
        // the current season never prunes the previous one.
        Assert.Equal(3, await check.Leagues.CountAsync());
        Assert.True(await check.Teams.AnyAsync(team => team.ExternalId == "900"));
    }

    [Fact]
    public async Task Import_NeverPrunesASiblingDivisionOfTheSameAssociation()
    {
        var handler = new SpordleHandler();
        var service = CreateService(handler);
        await service.ImportAsync(new(OrganizationId, "6193", "2026-27"));

        // A second division of the same association, same season: importing
        // 6193 must leave its leagues and teams untouched.
        int siblingLeagueId;
        await using (var db = new HockeySeasonDbContext(_options))
        {
            var sibling = new Division
            {
                OrganizationId = OrganizationId,
                Name = "LIGUE DE HOCKEY ÉLITE DU RICHELIEU",
                ExternalId = "6194",
            };
            db.Divisions.Add(sibling);
            await db.SaveChangesAsync();
            var season = await db.Seasons.SingleAsync(candidate => candidate.ExternalId == "2026-27");
            var siblingLeague = new League
            {
                DivisionId = sibling.Id,
                SeasonId = season.Id,
                Name = "M13 AA",
                ExternalId = "b10a5abe-aaad-493d-b16a-633550ca5edd",
            };
            db.Leagues.Add(siblingLeague);
            await db.SaveChangesAsync();
            siblingLeagueId = siblingLeague.Id;
            db.Teams.Add(new Team { LeagueId = siblingLeagueId, Name = "Sibling", ExternalId = "7001" });
            await db.SaveChangesAsync();
        }

        // The sibling category is not published by office 6193, so it is absent
        // from the import — but it belongs to another division and must survive.
        var result = await service.ImportAsync(new(OrganizationId, "6193", "2026-27"));

        Assert.Empty(result.DeletedLeagues);
        Assert.Equal(0, result.DeletedTeams);
        await using var check = new HockeySeasonDbContext(_options);
        Assert.True(await check.Leagues.AnyAsync(league => league.Id == siblingLeagueId));
        Assert.True(await check.Teams.AnyAsync(team => team.ExternalId == "7001"));
        Assert.Equal(4, await check.Teams.CountAsync()); // 3 of 6193 + 1 sibling
    }

    [Fact]
    public async Task Import_RejectsAnUnknownOffice()
    {
        var service = CreateService(new SpordleHandler { OfficeExists = false });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ImportAsync(new(OrganizationId, "9999", "2026-27")));
    }

    [Fact]
    public async Task Seasons_MergeWhatSpordlePublishesWithWhatWasImported()
    {
        var service = CreateService(new SpordleHandler());
        await service.ImportAsync(new(OrganizationId, "6193", "2026-27"));

        var seasons = await service.GetSeasonsAsync(6193);

        Assert.Equal(["2026-27", "2025-26"], seasons.Select(season => season.ExternalId));
        Assert.True(seasons[0].IsCurrent);
    }

    [Fact]
    public async Task Organizations_ReadTheOrganizationsTableOnly()
    {
        var service = CreateService(new SpordleHandler());

        // The page lists exactly the organizations table, seeded once.
        var organization = Assert.Single(await service.GetOrganizationsAsync());
        Assert.Equal(OrganizationId, organization.Id);
        Assert.Equal("HOCKEY RICHELIEU", organization.Name);
        Assert.Equal("6157", organization.ExternalId);
    }

    [Fact]
    public async Task Import_RejectsAnUnknownAssociation()
    {
        var service = CreateService(new SpordleHandler());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ImportAsync(new(4242, "6193", "2026-27")));
    }

    // ─── Helpers ───────────────────────────────────────────────────────────

    private ReferenceDataService CreateService(SpordleHandler handler) =>
        new(new TestDbContextFactory(_options), CreateLeagueApi(handler),
            new FixedTimeProvider(Now), NullLogger<ReferenceDataService>.Instance);

    private static LeagueApiService CreateLeagueApi(SpordleHandler handler)
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/api/") };
        var options = Options.Create(new LeagueApiOptions { BaseAddress = client.BaseAddress, ApiKey = "test" });
        return new LeagueApiService(client, options, NullLogger<LeagueApiService>.Instance);
    }

    private async Task<int> SeedUserFollowingAsync(string teamExternalId, string leagueExternalId)
    {
        await using var db = new HockeySeasonDbContext(_options);
        var user = new User
        {
            Auth0UserId = "auth0|follower",
            IsAdmin = false,
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var team = await db.Teams.SingleAsync(candidate => candidate.ExternalId == teamExternalId);
        var league = await db.Leagues.SingleAsync(candidate => candidate.ExternalId == leagueExternalId);
        db.TeamSubscriptions.Add(new TeamSubscription
        {
            UserId = user.Id,
            TeamId = team.Id,
            CreatedAt = Now,
            UpdatedAt = Now,
            LockedUntil = Now.AddDays(15),
        });
        db.LeagueSubscriptions.Add(new LeagueSubscription
        {
            UserId = user.Id,
            LeagueId = league.Id,
            CreatedAt = Now,
            UpdatedAt = Now,
        });
        await db.SaveChangesAsync();
        return user.Id;
    }

    private sealed class TestDbContextFactory(DbContextOptions<HockeySeasonDbContext> options)
        : IDbContextFactory<HockeySeasonDbContext>
    {
        public HockeySeasonDbContext CreateDbContext() => new(options);
        public ValueTask<HockeySeasonDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new HockeySeasonDbContext(options));
    }

    private sealed record Category(string Id, string Name, string? NameFr);

    /// <summary>Minimal Spordle API double: offices, categories, schedules and standings.</summary>
    private sealed class SpordleHandler : HttpMessageHandler
    {
        public bool OfficeExists { get; init; } = true;

        public string TeamName { get; set; } = "Alpha";

        public List<Category> Categories { get; } =
        [
            new("13c38dd1-e464-4835-af5f-75be8561daf6", "U13 B", "M13 B"),
            new("21c7f8f9-e0a4-416d-902a-50a6e68bf834", "U15 A", "M15 A"),
        ];

        /// <summary>Schedules of the categories, keyed by schedule id.</summary>
        public Dictionary<int, string> Schedules { get; } = new()
        {
            [194909] = "13c38dd1-e464-4835-af5f-75be8561daf6",
            [194910] = "13c38dd1-e464-4835-af5f-75be8561daf6",
            [194931] = "21c7f8f9-e0a4-416d-902a-50a6e68bf834",
        };

        private readonly Dictionary<int, List<(int Id, string Name)>> _teams = new()
        {
            [194909] = [(101, "Alpha")],
            [194910] = [(101, "Alpha"), (102, "Beta")],
            [194931] = [(201, "Gamma")],
        };

        /// <summary>
        /// Tournaments that publish no special-team figures, mirroring a
        /// Spordle schedule whose standings omit powerplayPct/penaltyKillPct
        /// (and, for a division-less tournament, the division/gender members).
        /// </summary>
        public HashSet<int> SchedulesWithoutSpecialTeams { get; } = [];

        /// <summary>
        /// Schedules whose standings payload has a shape the DTO does not
        /// expect (an unexpected JSON member on a tournament, for example).
        /// </summary>
        public HashSet<int> SchedulesWithUnreadableStandings { get; } = [];

        /// <summary>Schedules whose standings include a placeholder row with no team.</summary>
        public HashSet<int> SchedulesWithPlaceholderRows { get; } = [];

        public void Remove(string id) => _teams.Remove(int.Parse(id));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            var path = uri.AbsolutePath;
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            if (path.EndsWith("/offices", StringComparison.Ordinal))
            {
                if (!OfficeExists)
                {
                    return Json("[]");
                }

                var isAssociation = query["filter"]?.Contains("6157") == true;
                return Json(System.Text.Json.JsonSerializer.Serialize(new[]
                {
                    isAssociation
                        ? new { id = 6157, name = "HOCKEY RICHELIEU", type = "Region", parentId = 2377 }
                        : new { id = 6193, name = "LIGUE DE HOCKEY RÉCRÉATIVE DU RICHELIEU", type = "League", parentId = 6157 },
                }));
            }
            if (path.EndsWith("/seasons", StringComparison.Ordinal))
                return Json("""[{"seasonId":"2026-27","name":"2026-27","isCurrent":true},{"seasonId":"2025-26","name":"2025-26","isCurrent":false}]""");
            if (path.EndsWith("/categories", StringComparison.Ordinal))
                return Json(System.Text.Json.JsonSerializer.Serialize(Categories.Select(category => new
                {
                    category.Id,
                    category.Name,
                    nameFr = category.NameFr,
                })));
            if (path.EndsWith("/schedules", StringComparison.Ordinal))
            {
                var categoryId = query["categoryId"];
                if (!string.IsNullOrEmpty(categoryId))
                {
                    return Json(System.Text.Json.JsonSerializer.Serialize(
                        Schedules.Where(schedule => schedule.Value == categoryId)
                            .Select(schedule => new { id = schedule.Key, name = $"Schedule {schedule.Key}" })));
                }

                return Json(System.Text.Json.JsonSerializer.Serialize(
                    Schedules.Select(schedule => new { id = schedule.Key, name = $"Schedule {schedule.Key}" })));
            }

            if (path.EndsWith("/teamStats", StringComparison.Ordinal))
            {
                // /schedules/{id}/teamStats: the standings drive the team discovery.
                var scheduleId = int.Parse(path.Split('/')[^2]);
                if (!_teams.TryGetValue(scheduleId, out var teams))
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

                if (SchedulesWithUnreadableStandings.Contains(scheduleId))
                {
                    // A payload the DTO cannot read must cost the schedule, not
                    // the whole import.
                    return Json("""[{"teamId":101,"ranking":"first"}]""");
                }

                if (SchedulesWithPlaceholderRows.Contains(scheduleId))
                {
                    // Placeholder/bracket row without a team, next to a real one.
                    return Json("""
                        [{"teamId":101,"ranking":1,"team":{"id":101,"name":"Alpha","shortName":null}},
                         {"teamId":0,"ranking":2}]
                        """);
                }

                if (SchedulesWithoutSpecialTeams.Contains(scheduleId))
                {
                    // Wire shape of a tournament: no special-team percentages, no
                    // division/gender on the team, and "shortName": null.
                    return Json(System.Text.Json.JsonSerializer.Serialize(teams.Select((team, index) => new
                    {
                        teamId = team.Id,
                        ranking = index + 1,
                        gamesPlayed = 0,
                        wins = 0,
                        losses = 0,
                        ties = 0,
                        points = 0,
                        goalFor = 0,
                        goalAgainst = 0,
                        diff = 0,
                        pim = 0,
                        team = new { id = team.Id, name = team.Name, shortName = (string?)null },
                    })));
                }

                return Json(System.Text.Json.JsonSerializer.Serialize(teams.Select((team, index) => new
                {
                    teamId = team.Id,
                    ranking = index + 1,
                    gamesPlayed = 0,
                    wins = 0,
                    losses = 0,
                    ties = 0,
                    points = 0,
                    goalFor = 0,
                    goalAgainst = 0,
                    diff = 0,
                    pim = 0,
                    powerplayPct = 0,
                    penaltyKillPct = 0,
                    team = new { id = team.Id, name = team.Id == 101 ? TeamName : team.Name, shortName = team.Name, division = "U13", gender = "Integrated" },
                })));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static Task<HttpResponseMessage> Json(string payload) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            });
    }
}

