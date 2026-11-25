using System.Text.Json;
using HockeySeason.Application;
using HockeySeason.Functions;
using HockeySeason.LeagueApi;
using HockeySeason.LeagueApi.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Xunit;

namespace HockeySeason.Tests;

/// <summary>
/// The team page navigation returns database ids and lists only the values that
/// own at least one followed team: an association, division or league with no
/// follow is never offered, so nothing outside the follows can be selected.
/// The team-detail route of the page resolves the published schedule from the
/// database instead of trusting a Spordle id in the URL.
/// </summary>
public sealed class TeamPageNavigationTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Navigation_ListsOnlyTheValuesWithAFollowedTeam()
    {
        var function = CreateNavigation(FollowedTeams());
        var (context, request) = CreateContext();

        // Both associations of the follows are offered, ordered by name.
        Assert.Equal([2, 1], await ReadAsync(function.Associations(request, context, CancellationToken.None), "associationId"));
        Assert.Equal([10], await ReadAsync(function.Divisions(request, 1, context, CancellationToken.None), "divisionId"));
        Assert.Equal([100], await ReadAsync(function.Leagues(request, 10, context, CancellationToken.None), "leagueId"));
        // One followed team opens its whole league: every team of league 100 is
        // listed, so the selector offers the opponents too.
        Assert.Equal([500], await ReadAsync(function.Teams(request, 100, context, CancellationToken.None), "teamId"));

        // An association, division or league without a followed team is not
        // part of the follows: the level comes back empty rather than offering
        // a path to it.
        Assert.Empty(await ReadAsync(function.Divisions(request, 999, context, CancellationToken.None), "divisionId"));
        Assert.Empty(await ReadAsync(function.Leagues(request, 999, context, CancellationToken.None), "leagueId"));
        Assert.Empty(await ReadAsync(function.Teams(request, 999, context, CancellationToken.None), "teamId"));
    }

    [Fact]
    public async Task TeamLevel_ListsEveryTeamOfALeagueThatHoldsAFollowedTeam()
    {
        // Follow list: one followed team in league 100; the league holds two
        // more teams the user does not follow.
        var function = CreateNavigation(
            [new FollowedTeam(500, 181303, "Grizzly 1", 100, "M11A",
                [new FollowedSchedule(194908, "M11A Saison")], 10, "Simple lettre", 1, "Hockey Richelieu")],
            [
                new SelectableTeam(500, 181303, "Grizzly 1", 100, "M11A", [new FollowedSchedule(194908, "M11A Saison")]),
                new SelectableTeam(502, 181305, "Grizzly 2", 100, "M11A", [new FollowedSchedule(194908, "M11A Saison")]),
                new SelectableTeam(503, 181306, "Falcon", 100, "M11A", [new FollowedSchedule(194908, "M11A Saison")]),
            ]);
        var (context, request) = CreateContext();

        // Every team of the opened league is listed, followed or not, ordered
        // by name (Falcon < Grizzly 1 < Grizzly 2).
        Assert.Equal([503, 500, 502], await ReadAsync(function.Teams(request, 100, context, CancellationToken.None), "teamId"));

        // A league nobody follows lists nothing: its teams can neither be
        // reached nor guessed.
        Assert.Empty(await ReadAsync(function.Teams(request, 999, context, CancellationToken.None), "teamId"));
    }

    [Fact]
    public async Task FollowedTeams_ReturnTheWholeListWithItsParents()
    {
        var function = CreateNavigation(FollowedTeams());
        var (context, request) = CreateContext();

        using var json = await ReadJsonAsync(function.FollowedTeams(request, context, CancellationToken.None));
        var teams = json.RootElement.EnumerateArray().ToArray();
        Assert.Equal(2, teams.Length);

        var team = teams[0];
        Assert.Equal(500, team.GetProperty("teamId").GetInt32());
        Assert.Equal("Grizzly 1", team.GetProperty("name").GetString());
        Assert.Equal(100, team.GetProperty("leagueId").GetInt32());
        Assert.Equal(10, team.GetProperty("divisionId").GetInt32());
        Assert.Equal(1, team.GetProperty("associationId").GetInt32());
        Assert.Equal("M11A", team.GetProperty("leagueName").GetString());
        Assert.Equal("Simple lettre", team.GetProperty("divisionName").GetString());
        Assert.Equal("Hockey Richelieu", team.GetProperty("associationName").GetString());
    }

    [Fact]
    public async Task TeamDetail_ResolvesTheStoredScheduleAndRefusesAnUnfollowedTeam()
    {
        var details = new RecordingTeamDetails();
        // null LeagueApiService: the page route must read the schedule from the
        // database, never call Spordle from a league id in the URL.
        var function = new TeamFunctions(
            null!, details, Options.Create(new LeagueApiOptions()),
            new StubScope(FollowedTeams()), new RequestUserResolver(new StubProvisioning()),
            FunctionsTests.WithTestDatabase());
        var (context, request) = CreateContext();

        Assert.IsType<OkObjectResult>(await function.FollowedTeamDetail(request, 500, context, CancellationToken.None));
        Assert.Equal([194908], details.RequestedSchedules);
        Assert.Equal(181303, details.RequestedTeam);

        // A team id outside the follows is refused before Spordle is touched.
        Assert.Equal(StatusCodes.Status403Forbidden,
            Assert.IsType<StatusCodeResult>(await function.FollowedTeamDetail(request, 999, context, CancellationToken.None)).StatusCode);
        Assert.Equal([194908], details.RequestedSchedules);
    }

    [Fact]
    public async Task TeamDetail_MapsTheSpordleIdsOfTheGamesToThePageIds()
    {
        // The games carry Spordle team ids while the page navigates with
        // database ids: the route returns the bridge for the whole league, so a
        // next game can link its opponent's page.
        var function = new TeamFunctions(
            null!, new RecordingTeamDetails(), Options.Create(new LeagueApiOptions()),
            new StubScope(FollowedTeams(), [
                new SelectableTeam(500, 181303, "Grizzly 1", 100, "M11A", [new FollowedSchedule(194908, "M11A Saison")]),
                new SelectableTeam(502, 181305, "Falcon", 100, "M11A", [new FollowedSchedule(194908, "M11A Saison")]),
            ]),
            new RequestUserResolver(new StubProvisioning()), FunctionsTests.WithTestDatabase());
        var (context, request) = CreateContext();

        using var json = await ReadJsonAsync(function.FollowedTeamDetail(request, 500, context, CancellationToken.None));
        var links = json.RootElement.GetProperty("teamLinks").EnumerateArray()
            .Select(link => (External: link.GetProperty("externalTeamId").GetInt32(), Page: link.GetProperty("pageTeamId").GetInt32()))
            .ToArray();
        Assert.Equal([(181303, 500), (181305, 502)], links);
        // The detail itself keeps the shape the page already reads.
        Assert.Equal("Grizzly 1", json.RootElement.GetProperty("team").GetProperty("teamName").GetString());
        Assert.Equal(0, json.RootElement.GetProperty("totalGoals").GetInt32());
    }

    private static SubscriptionNavigationFunctions CreateNavigation(IReadOnlyList<FollowedTeam> teams) =>
        new(new StubScope(teams), new RequestUserResolver(new StubProvisioning()), FunctionsTests.WithTestDatabase());

    private static SubscriptionNavigationFunctions CreateNavigation(
        IReadOnlyList<FollowedTeam> teams, IReadOnlyList<SelectableTeam> leagueTeams) =>
        new(new StubScope(teams, leagueTeams), new RequestUserResolver(new StubProvisioning()),
            FunctionsTests.WithTestDatabase());

    private static (Microsoft.Azure.Functions.Worker.FunctionContext Context, HttpRequest Request) CreateContext()
    {
        var request = new DefaultHttpContext().Request;
        return (FunctionsTests.CreateFunctionContextForRequest(request), request);
    }

    private static async Task<JsonDocument> ReadJsonAsync(Task<IActionResult> action)
    {
        var result = Assert.IsType<OkObjectResult>(await action);
        return JsonDocument.Parse(JsonSerializer.Serialize(result.Value, Web));
    }

    private static async Task<List<int>> ReadAsync(Task<IActionResult> action, string property)
    {
        using var json = await ReadJsonAsync(action);
        return [.. json.RootElement.EnumerateArray().Select(item => item.GetProperty(property).GetInt32())];
    }

    /// <summary>Two followed teams in two associations, plus the unfollowed ones.</summary>
    private static IReadOnlyList<FollowedTeam> FollowedTeams() =>
    [
        new(500, 181303, "Grizzly 1", 100, "M11A", [new FollowedSchedule(194908, "M11A Saison")],
            10, "Simple lettre", 1, "Hockey Richelieu"),
        new(501, 181304, "Grizzly 2", 200, "M11B", [new FollowedSchedule(194910, "M11B Saison")],
            20, "Double lettre", 2, "Autre association"),
    ];

    private sealed class StubScope(
        IReadOnlyList<FollowedTeam> teams,
        IReadOnlyList<SelectableTeam>? leagueTeams = null) : ICallerDataScopeService
    {
        public Task<CallerDataScope> GetAsync(int userId, bool unrestricted = false, CancellationToken cancellationToken = default) =>
            Task.FromResult(CallerDataScope.NothingFollowed());

        public Task<CallerDataScope> GetForSubjectAsync(string? auth0Subject, bool unrestricted = false, CancellationToken cancellationToken = default) =>
            Task.FromResult(CallerDataScope.NothingFollowed());

        public Task<IReadOnlyList<FollowedTeam>> GetFollowedTeamsAsync(int userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(teams);

        public Task<IReadOnlyList<SelectableTeam>> GetLeagueTeamsAsync(
            int userId, int leagueId, CancellationToken cancellationToken = default)
        {
            var list = (leagueTeams ?? teams
                .Where(team => team.LeagueId == leagueId)
                .Select(team => new SelectableTeam(
                    team.Id, team.ExternalId, team.Name, team.LeagueId, team.LeagueName, team.Schedules))
                .ToList())
                .Where(team => leagueTeams is null || team.LeagueId == leagueId)
                .ToList();
            return Task.FromResult<IReadOnlyList<SelectableTeam>>(list);
        }

        public Task<SelectableTeam?> GetSelectableTeamAsync(
            int userId, int teamId, CancellationToken cancellationToken = default) =>
            Task.FromResult<SelectableTeam?>(teams.FirstOrDefault(team => team.Id == teamId) is { } followed
                ? new SelectableTeam(
                    followed.Id, followed.ExternalId, followed.Name,
                    followed.LeagueId, followed.LeagueName, followed.Schedules)
                : null);
    }

    private sealed class StubProvisioning : IUserProvisioningService
    {
        public Task<UserProvisioningResult> ProvisionAsync(ApplicationUserClaims claims, CancellationToken cancellationToken = default) =>
            Task.FromResult(new UserProvisioningResult(7, Created: true));
    }

    /// <summary>Records the schedule ids the page route resolved from the database.</summary>
    private sealed class RecordingTeamDetails : ITeamDetailService
    {
        public List<int> RequestedSchedules { get; } = [];
        public int? RequestedTeam { get; private set; }

        public Task<TeamDetailResponse?> GetAsync(int leagueId, int teamId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The page route must resolve the schedule, not use a Spordle league id.");

        public Task<TeamDetailResponse?> GetAsync(
            IReadOnlyList<int> scheduleIds, int teamId, CancellationToken cancellationToken)
        {
            RequestedSchedules.AddRange(scheduleIds);
            RequestedTeam = teamId;
            var standing = new LeagueStanding(
                teamId, "Grizzly 1", null, "M11A", "M", 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, null, null);
            return Task.FromResult<TeamDetailResponse?>(new TeamDetailResponse(
                194908, standing, [standing], [], [], [], 0, 0, 0, [], [], 0, 0, 0, false));
        }
    }
}