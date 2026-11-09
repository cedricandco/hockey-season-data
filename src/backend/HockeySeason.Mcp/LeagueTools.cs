using System.ComponentModel;
using HockeySeason.LeagueApi;
using HockeySeason.LeagueApi.Extensions;
using HockeySeason.LeagueApi.Http;
using HockeySeason.LeagueApi.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace HockeySeason.Mcp;

/// <summary>
/// MCP tools exposing the Spordle league data: leagues, standings, teams,
/// games and suspensions. Tool methods are plain calls into
/// <see cref="LeagueApiService"/>, keeping this layer a thin adapter.
/// </summary>
[McpServerToolType]
public sealed class LeagueTools
{
    private readonly LeagueApiService _leagueApiService;
    private readonly LeagueApiOptions _options;

    public LeagueTools(LeagueApiService leagueApiService, IOptions<LeagueApiOptions> options)
    {
        _leagueApiService = leagueApiService ?? throw new ArgumentNullException(nameof(leagueApiService));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Returns all leagues (schedules) published for an office and season with their id and name.
    /// </summary>
    [McpServerTool(
        Name = "GetLeagues",
        Title = "Get Leagues",
        ReadOnly = true,
        Idempotent = true,
        Destructive = false,
        OpenWorld = true,
        UseStructuredContent = true)]
    [Description(
        "Returns all leagues (schedules) for an office and season, with each league's id and name. " +
        "Defaults to office 6193 and season 2026-27. Use a returned id with GetLeagueStanding, " +
        "GetLeagueTeams, GetLeagueGames or GetLeagueSuspensions.")]
    public async Task<IReadOnlyList<League>> GetLeagues(
        [Description("Office identifier. Defaults to 6193 (Sainte-Julie).")]
        int? officeId = null,
        [Description("Season identifier, for example 2026-27. Defaults to the configured season.")]
        string? seasonId = null,
        CancellationToken cancellationToken = default)
    {
        var offices = officeId ?? _options.DefaultOfficeId;
        var season = string.IsNullOrWhiteSpace(seasonId) ? _options.DefaultSeasonId : seasonId;

        return await _leagueApiService
            .GetLeaguesAsync(offices, season, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the current standings table of one league, ordered by ranking.
    /// </summary>
    [McpServerTool(
        Name = "GetLeagueStanding",
        Title = "Get League Standing",
        ReadOnly = true,
        Idempotent = true,
        Destructive = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Returns the standings table of one league (ranking, record, points, goals, special teams, " +
        "discipline) for every team, ordered by ranking. Use GetLeagues to find the league id.")]
    public async Task<IReadOnlyList<LeagueStanding>> GetLeagueStanding(
        [Description("League (schedule) identifier, for example 194908. Use GetLeagues to find it.")]
        int leagueId,
        CancellationToken cancellationToken = default)
    {
        return await _leagueApiService
            .GetLeagueStandingsAsync(leagueId, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the id and name of every team in a league.
    /// </summary>
    [McpServerTool(
        Name = "GetLeagueTeams",
        Title = "Get League Teams",
        ReadOnly = true,
        Idempotent = true,
        Destructive = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Returns the id and name of every team in a league. Served from the league's standings " +
        "endpoint, so it reflects the teams currently registered. Use GetLeagues to find the league id.")]
    public async Task<IReadOnlyList<LeagueTeam>> GetLeagueTeams(
        [Description("League (schedule) identifier, for example 194908. Use GetLeagues to find it.")]
        int leagueId,
        CancellationToken cancellationToken = default)
    {
        return await _leagueApiService
            .GetLeagueTeamsAsync(leagueId, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the games of a league (optionally one team) between two dates.
    /// </summary>
    [McpServerTool(
        Name = "GetTeamGames",
        Title = "Get Team Games",
        ReadOnly = true,
        Idempotent = true,
        Destructive = false,
        OpenWorld = true,
        UseStructuredContent = true)]
    [Description(
        "Returns the games of a league between a start date and an end date, ordered by date and " +
        "time, with both teams (id, name, logo) and the score when the game was played. Pass a team " +
        "id to restrict to one team's games (home and away). Use GetLeagues to find the league id " +
        "and GetLeagueTeams to find team ids.")]
    public async Task<IReadOnlyList<LeagueGame>> GetTeamGames(
        [Description("League (schedule) identifier, for example 194908. Use GetLeagues to find it.")]
        int leagueId,
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
            throw new ModelContextProtocol.McpException(
                $"Invalid startDate '{startDate}'. The date must use the yyyy-MM-dd form, for example 2026-10-01.");
        }

        if (!DateOnly.TryParseExact(
                endDate,
                "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out var end))
        {
            throw new ModelContextProtocol.McpException(
                $"Invalid endDate '{endDate}'. The date must use the yyyy-MM-dd form, for example 2026-11-09.");
        }

        return await _leagueApiService
            .GetLeagueGamesAsync(leagueId, teamId, start, end, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the suspensions published for a league.
    /// </summary>
    [McpServerTool(
        Name = "GetLeagueSuspension",
        Title = "Get League Suspensions",
        ReadOnly = true,
        Idempotent = true,
        Destructive = false,
        OpenWorld = true,
        UseStructuredContent = true)]
    [Description(
        "Returns the suspensions published for a league: status, suspended player (name, number, " +
        "position), team, suspension code, the game it was issued in, games to sit out and games " +
        "already served. Use GetLeagues to find the league id.")]
    public async Task<IReadOnlyList<LeagueSuspension>> GetLeagueSuspension(
        [Description("League (schedule) identifier, for example 194908. Use GetLeagues to find it.")]
        int leagueId,
        CancellationToken cancellationToken = default)
    {
        return await _leagueApiService
            .GetLeagueSuspensionsAsync(leagueId, cancellationToken)
            .ConfigureAwait(false);
    }
}
