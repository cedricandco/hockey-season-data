using System.ComponentModel;
using HockeySeason.Application;
using HockeySeason.LeagueApi;
using HockeySeason.LeagueApi.Http;
using HockeySeason.LeagueApi.Models;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace HockeySeason.Mcp;

/// <summary>
/// MCP tools of the Hockey Assistant. The reference data — associations,
/// divisions, leagues, teams and their schedules — is read from the database
/// and limited to what the caller follows (association → division → league →
/// team). Only the live figures still come from the public Spordle API:
/// standings, games, box scores and suspensions, always called with schedule
/// and team ids resolved from the database and validated against the caller's
/// follows.
/// </summary>
/// <remarks>
/// The caller's follows are resolved by <see cref="ICallerDataScopeService"/>
/// for the subject carried by the current request; the MCP endpoint and the
/// AG-UI agent endpoint both validate the bearer token before invoking a tool.
/// Asking for a league, division or team outside those follows is refused with
/// an <see cref="McpException"/>, so the assistant can only answer about what
/// the user follows.
/// </remarks>
[McpServerToolType]
public sealed class LeagueTools
{
    private readonly LeagueApiService _leagueApiService;
    private readonly ITeamDetailService _teamDetails;
    private readonly ICallerDataScopeService _scope;
    private readonly CallerIdentity _identity;

    public LeagueTools(
        LeagueApiService leagueApiService,
        ITeamDetailService teamDetails,
        ICallerDataScopeService scope,
        CallerIdentity identity)
    {
        _leagueApiService = leagueApiService ?? throw new ArgumentNullException(nameof(leagueApiService));
        _teamDetails = teamDetails ?? throw new ArgumentNullException(nameof(teamDetails));
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
    }

    private Task<CallerDataScope> GetScopeAsync(CancellationToken cancellationToken) =>
        _scope.GetForSubjectAsync(_identity.Subject, _identity.Unrestricted, cancellationToken);

    [McpServerTool(Name = "GetAssociations", Title = "Get Associations", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false, UseStructuredContent = true)]
    [Description(
        "Returns the associations you follow, either directly or through a followed division, " +
        "league or team. Start here to navigate the data you are allowed to read.")]
    public async Task<IReadOnlyList<ScopedAssociation>> GetAssociations(CancellationToken cancellationToken = default)
    {
        var scope = await GetScopeAsync(cancellationToken).ConfigureAwait(false);
        return scope.Associations;
    }

    [McpServerTool(Name = "GetDivisions", Title = "Get Divisions", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false, UseStructuredContent = true)]
    [Description(
        "Returns the divisions (Spordle offices) you follow. Pass an association id from " +
        "GetAssociations to restrict the list to that association.")]
    public async Task<IReadOnlyList<ScopedDivision>> GetDivisions(
        [Description("Optional association identifier returned by GetAssociations.")] int? associationId = null,
        CancellationToken cancellationToken = default)
    {
        var scope = await GetScopeAsync(cancellationToken).ConfigureAwait(false);
        return [.. scope.Divisions.Where(division => associationId is null || division.AssociationId == associationId)];
    }

    [McpServerTool(Name = "GetLeagues", Title = "Get Leagues", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false, UseStructuredContent = true)]
    [Description(
        "Returns the leagues you follow, with the Spordle category id and the schedule ids. Pass a " +
        "division id from GetDivisions to restrict the list. The schedule ids feed the live calls " +
        "GetLeagueStanding, GetLeagueSuspension and GetTeamGames; the category id feeds GetLeagueTeams.")]
    public async Task<IReadOnlyList<ScopedLeague>> GetLeagues(
        [Description("Optional division identifier returned by GetDivisions.")] int? divisionId = null,
        CancellationToken cancellationToken = default)
    {
        var scope = await GetScopeAsync(cancellationToken).ConfigureAwait(false);
        return [.. scope.Leagues.Where(league => divisionId is null || league.DivisionId == divisionId)];
    }

    [McpServerTool(Name = "GetLeagueTeams", Title = "Get League Teams", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false, UseStructuredContent = true)]
    [Description(
        "Returns the teams you may see in one league, with their Spordle team id and the schedule id. " +
        "Following a team opens its whole league: every team of the league is listed, not only the " +
        "one followed. A league you follow directly, or one inside a followed association, is open too.")]
    public async Task<IReadOnlyList<ScopedTeam>> GetLeagueTeams(
        [Description("Spordle category identifier (guid) returned by GetLeagues.")] string leagueId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(leagueId))
        {
            throw new McpException("A league id from GetLeagues is required.");
        }

        var scope = await GetScopeAsync(cancellationToken).ConfigureAwait(false);
        if (!scope.ContainsLeague(leagueId))
        {
            throw new McpException($"League '{leagueId}' is not one of the leagues you follow.");
        }

        return [.. scope.Teams.Where(team => string.Equals(team.LeagueExternalId, leagueId, StringComparison.Ordinal))];
    }

    [McpServerTool(Name = "GetLeagueStanding", Title = "Get League Standing", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true, UseStructuredContent = true)]
    [Description(
        "Returns the current standings table of one league (ranking, record, points, goals, special " +
        "teams, discipline) for every team, ordered by ranking. Pass a schedule id returned by " +
        "GetLeagues; the figures come from the live league feed.")]
    public async Task<IReadOnlyList<LeagueStanding>> GetLeagueStanding(
        [Description("Schedule identifier: a schedule id returned by GetLeagues.")] int scheduleId,
        CancellationToken cancellationToken = default)
    {
        await EnsureScheduleVisibleAsync(scheduleId, cancellationToken).ConfigureAwait(false);
        return await _leagueApiService
            .GetLeagueStandingsAsync(scheduleId, cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = "GetTeamGames", Title = "Get Team Games", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true, UseStructuredContent = true)]
    [Description(
        "Returns the games of a league between a start date and an end date, ordered by date and " +
        "time, with both teams and the score when the game was played. Pass a team id to restrict " +
        "to one team's games. The schedule id comes from GetLeagues, the team ids from GetLeagueTeams.")]
    public async Task<IReadOnlyList<LeagueGame>> GetTeamGames(
        [Description("Schedule identifier: a schedule id returned by GetLeagues.")] int scheduleId,
        [Description("First date to include, in yyyy-MM-dd form, for example 2026-10-01.")]
        string startDate,
        [Description("Last date to include, in yyyy-MM-dd form, for example 2026-11-09.")]
        string endDate,
        [Description("Optional team identifier to restrict the games to one team (home and away).")]
        int? teamId = null,
        CancellationToken cancellationToken = default)
    {
        if (!DateOnly.TryParseExact(
                startDate,
                "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out var start))
        {
            throw new McpException(
                $"Invalid startDate '{startDate}'. The date must use the yyyy-MM-dd form, for example 2026-10-01.");
        }

        if (!DateOnly.TryParseExact(
                endDate,
                "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out var end))
        {
            throw new McpException(
                $"Invalid endDate '{endDate}'. The date must use the yyyy-MM-dd form, for example 2026-11-09.");
        }

        var scope = await GetScopeAsync(cancellationToken).ConfigureAwait(false);
        await EnsureScheduleVisibleAsync(scheduleId, cancellationToken, scope).ConfigureAwait(false);
        if (teamId is { } id && !scope.ContainsTeam(id))
        {
            throw new McpException($"Team {id} is not one of the teams you may read.");
        }

        return await _leagueApiService
            .GetLeagueGamesAsync(scheduleId, teamId, start, end, cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = "GetLeagueSuspension", Title = "Get League Suspensions", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true, UseStructuredContent = true)]
    [Description(
        "Returns the suspensions published for a league: status, suspended player, team, code, the " +
        "game it was issued in, games to sit out and games already served. Pass a schedule id " +
        "returned by GetLeagues.")]
    public async Task<IReadOnlyList<LeagueSuspension>> GetLeagueSuspension(
        [Description("Schedule identifier: a schedule id returned by GetLeagues.")] int scheduleId,
        CancellationToken cancellationToken = default)
    {
        await EnsureScheduleVisibleAsync(scheduleId, cancellationToken).ConfigureAwait(false);
        return await _leagueApiService
            .GetLeagueSuspensionsAsync(scheduleId, cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = "GetGameDetail", Title = "Get Game Detail", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true, UseStructuredContent = true)]
    [Description(
        "Returns the box score of one game: both teams, scoreboard figures per team, goals per " +
        "period, every goal (scorer, assists, special teams) and every penalty. Use the game id " +
        "returned by GetTeamGames.")]
    public async Task<LeagueGameDetail> GetGameDetail(
        [Description("Identifier of the game, as returned by GetTeamGames. Must be positive.")]
        int gameId,
        CancellationToken cancellationToken = default)
    {
        if (gameId <= 0)
        {
            throw new McpException("The game id must be positive.");
        }

        var scope = await GetScopeAsync(cancellationToken).ConfigureAwait(false);
        var detail = await _leagueApiService
            .GetLeagueGameDetailAsync(gameId, cancellationToken)
            .ConfigureAwait(false);
        // The box score is fetched from Spordle, but only a game involving a
        // visible team may be read.
        if (scope.IsRestricted
            && !scope.ContainsTeam(detail.HomeTeam.Id)
            && !scope.ContainsTeam(detail.AwayTeam.Id))
        {
            throw new McpException($"Game {gameId} does not involve any team you may read.");
        }

        return detail;
    }

    [McpServerTool(Name = "GetTeamDetail", Title = "Get Team Detail", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true, UseStructuredContent = true)]
    [Description(
        "Returns a team's division standings, completed games with full box scores, upcoming games, " +
        "event-based player scoring and special-teams and penalty totals. Players without game " +
        "events are not included. Use GetLeagueTeams to resolve the team id; any team of a league " +
        "that contains a followed team works, not only the followed one.")]
    public async Task<TeamDetailResponse> GetTeamDetail(
        [Description("Team identifier returned by GetLeagueTeams.")] int teamId,
        CancellationToken cancellationToken = default)
    {
        var scope = await GetScopeAsync(cancellationToken).ConfigureAwait(false);
        var team = scope.Teams.FirstOrDefault(candidate => candidate.Id == teamId);
        if (team is null)
        {
            throw new McpException($"Team {teamId} is not one of the teams you may read.");
        }

        if (team.ScheduleId is not { } scheduleId)
        {
            throw new McpException($"No published schedule is stored for team {teamId}.");
        }

        return await _teamDetails.GetAsync(scheduleId, teamId, cancellationToken).ConfigureAwait(false)
            ?? throw new McpException("Team not found in the league standings.");
    }

    /// <summary>
    /// Refuses a schedule id outside the caller's follows (standings, games and
    /// suspensions are keyed on it).
    /// </summary>
    private async Task EnsureScheduleVisibleAsync(
        int scheduleId, CancellationToken cancellationToken, CallerDataScope? scope = null)
    {
        if (scheduleId <= 0)
        {
            throw new McpException("A schedule id from GetLeagues is required.");
        }

        scope ??= await GetScopeAsync(cancellationToken).ConfigureAwait(false);
        if (scope.IsRestricted && !scope.ContainsSchedule(scheduleId))
        {
            throw new McpException(
                $"Schedule {scheduleId} is not one of the schedules you follow. Use an id returned by GetLeagues.");
        }
    }
}
