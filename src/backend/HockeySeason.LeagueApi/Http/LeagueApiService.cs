using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using HockeySeason.LeagueApi.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HockeySeason.LeagueApi.Http;

/// <summary>
/// Reads leagues, standings, teams, games and suspensions from the public
/// Spordle league API. One service owns every endpoint so the wire format,
/// headers and filter conventions live in exactly one place.
/// </summary>
public sealed partial class LeagueApiService
{
    private const string PageTypeHeader = "x-page-type";

    private const string LanguageHeader = "accept-language";

    // The league sends its language preference as "fr;en", which is not valid
    // HTTP syntax, so every header is passed through unvalidated.
    private const string LanguageHeaderValue = "fr;en";

    /// <summary>Filter asking for the whole standings table, excluding placeholders, with teams expanded.</summary>
    private const string TeamStatsFilter =
        """{"where":{"team":{"name":{"neq":"TBA"}}},"include":["team","group"]}""";

    private static readonly TimeSpan StandingsCacheDuration = TimeSpan.FromSeconds(60);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        RespectNullableAnnotations = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
    };

    private readonly HttpClient _httpClient;
    private readonly LeagueApiOptions _options;
    private readonly ILogger<LeagueApiService> _logger;
    private readonly SemaphoreSlim _standingsLock = new(1, 1);

    private IReadOnlyList<Models.LeagueStanding>? _standingsCache;
    private int _standingsCacheLeagueId;
    private DateTimeOffset _standingsCacheExpiresAt;

    public LeagueApiService(HttpClient httpClient, IOptions<LeagueApiOptions> options, ILogger<LeagueApiService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Lists the leagues (schedules) published for an office and season.
    /// </summary>
    public async Task<IReadOnlyList<League>> GetLeaguesAsync(
        int officeId,
        string seasonId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(officeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(seasonId);

        var rows = await GetJsonAsync<ScheduleRowDto[]>(
            $"sp/schedules?officeId={officeId}&seasonId={Uri.EscapeDataString(seasonId)}",
            cancellationToken).ConfigureAwait(false);

        return [.. rows.Select(row => new League(row.Id, row.Name)).OrderBy(league => league.Name, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// Reads the standings table of one league, ordered by ranking.
    /// Results are cached briefly because standings only change between games.
    /// </summary>
    public async Task<IReadOnlyList<Models.LeagueStanding>> GetLeagueStandingsAsync(
        int leagueId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(leagueId);

        if (TryGetFreshStandings(leagueId) is { } cached)
        {
            return cached;
        }

        await _standingsLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (TryGetFreshStandings(leagueId) is { } refreshed)
            {
                return refreshed;
            }

            var rows = await GetJsonAsync<StandingsRowDto[]>(
                $"sp/schedules/{leagueId}/teamStats?filter={Uri.EscapeDataString(TeamStatsFilter)}",
                cancellationToken).ConfigureAwait(false);

            var standings = MapStandings(rows);
            _standingsCache = standings;
            _standingsCacheLeagueId = leagueId;
            _standingsCacheExpiresAt = DateTimeOffset.UtcNow.Add(StandingsCacheDuration);
            return standings;
        }
        finally
        {
            _standingsLock.Release();
        }
    }

    /// <summary>
    /// Lists the teams of a league (id and name) from the standings endpoint.
    /// </summary>
    public async Task<IReadOnlyList<LeagueTeam>> GetLeagueTeamsAsync(
        int leagueId,
        CancellationToken cancellationToken = default)
    {
        var standings = await GetLeagueStandingsAsync(leagueId, cancellationToken).ConfigureAwait(false);
        return [.. standings
            .Select(row => new LeagueTeam(row.TeamId, row.TeamName))
            .Distinct()
            .OrderBy(team => team.Name, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// Lists the games of one team inside a league over a date range, ordered by date and time.
    /// </summary>
    public async Task<IReadOnlyList<LeagueGame>> GetLeagueGamesAsync(
        int leagueId,
        int? teamId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(leagueId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(teamId ?? 1);
        if (startDate > endDate)
        {
            throw new ArgumentException(
                $"The start date '{startDate:yyyy-MM-dd}' must not fall after the end date '{endDate:yyyy-MM-dd}'.",
                nameof(startDate));
        }

        var filter = BuildGamesFilter(leagueId, teamId, startDate, endDate);
        var rows = await GetJsonAsync<GameRowDto[]>(
            $"sp/games?filter={Uri.EscapeDataString(filter)}",
            cancellationToken).ConfigureAwait(false);

        return [.. rows.Select(MapGame)
            .OrderBy(game => game.Date, StringComparer.Ordinal)
            .ThenBy(game => game.Time, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Reads the box score of one game: both teams, their scoreboard figures,
    /// every goal and every penalty. Nothing is cached, because a box score is
    /// immutable once published.
    /// </summary>
    public async Task<LeagueGameDetail> GetLeagueGameDetailAsync(
        int gameId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(gameId);

        var gameFilter = Uri.EscapeDataString(
            """{"include":["awayTeam","homeTeam","teamStats"]}""");
        var gamePayload = await GetJsonAsync<GameDetailRowDto>(
            $"sp/games/{gameId}?filter={gameFilter}",
            cancellationToken).ConfigureAwait(false);
        var boxScore = await GetJsonAsync<BoxScoreRowDto>(
            $"sp/games/{gameId}/boxScore",
            cancellationToken).ConfigureAwait(false);

        return MapGameDetail(gamePayload, boxScore, gameId);
    }

    /// <summary>
    /// Lists the suspensions published for a league.
    /// </summary>
    public async Task<IReadOnlyList<LeagueSuspension>> GetLeagueSuspensionsAsync(
        int leagueId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(leagueId);

        var rows = await GetJsonAsync<SuspensionRowDto[]>(
            $"sp/schedules/{leagueId}/suspensions",
            cancellationToken).ConfigureAwait(false);

        return [.. rows.Select(row => new LeagueSuspension(
            row.Id,
            row.Status,
            row.Participant?.FullName,
            row.Participant?.Number,
            row.Participant?.Position,
            row.Team.Id,
            row.Team.Name,
            row.Code,
            row.GameId,
            row.GameNumber,
            row.RequiredGames,
            row.PurgedGames.Count))];
    }

    private IReadOnlyList<Models.LeagueStanding>? TryGetFreshStandings(int leagueId) =>
        _standingsCache is not null
            && _standingsCacheLeagueId == leagueId
            && DateTimeOffset.UtcNow < _standingsCacheExpiresAt
            ? _standingsCache
            : null;

    private async Task<T> GetJsonAsync<T>(string relativeUri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_options.BaseAddress, relativeUri));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation(LanguageHeader, LanguageHeaderValue);
        request.Headers.TryAddWithoutValidation(PageTypeHeader, "LEAGUE");
        request.Headers.TryAddWithoutValidation("authorization", $"API-Key {_options.ApiKey}");

        using var response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"The league API returned {(int)response.StatusCode} ({response.ReasonPhrase}) for '{relativeUri}'.",
                inner: null,
                response.StatusCode);
        }

        var payload = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        LogReceivedPayload(_logger, payload.Length, relativeUri);

        return JsonSerializer.Deserialize<T>(payload, SerializerOptions)
            ?? throw new JsonException($"The league API returned an empty payload for '{relativeUri}'.");
    }

    private static IReadOnlyList<Models.LeagueStanding> MapStandings(IEnumerable<StandingsRowDto> rows) =>
        [.. rows
            .Select(row => new Models.LeagueStanding(
                row.TeamId,
                row.Team.Name,
                row.Team.ShortName,
                row.Team.Division,
                row.Team.Gender,
                row.Ranking,
                row.GamesPlayed,
                row.Wins,
                row.Losses,
                row.Ties,
                row.Points,
                row.GoalsFor,
                row.GoalsAgainst,
                row.GoalDifferential,
                row.PenaltyMinutes,
                row.PowerPlayPercentage,
                row.PenaltyKillPercentage))
            .OrderBy(row => row.Ranking)];

    private static LeagueGame MapGame(GameRowDto row)
    {
        var homeKey = row.HomeTeamId.ToString(CultureInfo.InvariantCulture);
        var awayKey = row.AwayTeamId.ToString(CultureInfo.InvariantCulture);

        return new LeagueGame(
            row.Id,
            row.Date,
            ToLocalTime(row.StartTime, row.TimeZone),
            row.HomeTeamId,
            row.HomeTeam.Name,
            row.HomeTeam.LogoUrl,
            row.AwayTeamId,
            row.AwayTeam.Name,
            row.AwayTeam.LogoUrl,
            // The league omits "score" entirely until the game is played.
            row.Score.TryGetValue(homeKey, out var homeScore) ? homeScore : null,
            row.Score.TryGetValue(awayKey, out var awayScore) ? awayScore : null);
    }

    private static LeagueGameDetail MapGameDetail(GameDetailRowDto? row, BoxScoreRowDto detail, int gameId)
    {
        ArgumentNullException.ThrowIfNull(row);

        var goals = detail.Goals
            .OrderBy(goal => int.Parse(goal.GameTime.Period, CultureInfo.InvariantCulture))
            .ThenBy(goal => goal.GameTime.ElapsedMinutes)
            .ThenBy(goal => goal.GameTime.ElapsedSeconds)
            .Select(MapGoal)
            .ToArray();

        return new LeagueGameDetail(
            row.Id,
            row.Number,
            row.Date,
            ToLocalTime(row.StartTime, row.TimeZone),
            row.Division,
            row.SeasonId,
            row.IsLive,
            row.IsApproved,
            new LeagueGameTeam(row.HomeTeam.Id, row.HomeTeam.Name, row.HomeTeam.LogoUrl),
            new LeagueGameTeam(row.AwayTeam.Id, row.AwayTeam.Name, row.AwayTeam.LogoUrl),
            [.. row.TeamStats.Select(MapGameTeamStats)
                .OrderBy(stats => stats.TeamId)],
            [.. goals
                .GroupBy(goal => (goal.TeamId, goal.Period))
                .Select(group => new LeagueGamePeriodGoals(group.Key.TeamId, group.Key.Period, group.Count()))
                .OrderBy(periodGoals => periodGoals.TeamId)
                .ThenBy(periodGoals => periodGoals.Period)],
            goals,
            [.. detail.Penalties
                .OrderBy(penalty => int.Parse(penalty.GameTime.Period, CultureInfo.InvariantCulture))
                .ThenBy(penalty => penalty.GameTime.ElapsedMinutes)
                .ThenBy(penalty => penalty.GameTime.ElapsedSeconds)
                .Select(MapPenalty)]);
    }

    private static LeagueGameTeamStats MapGameTeamStats(GameTeamStatsRowDto stats) => new(
        stats.TeamId,
        stats.GoalsFor,
        stats.GoalsAgainst,
        stats.GameResult,
        stats.WinLossType,
        stats.PenaltyMinutes,
        string.Create(CultureInfo.InvariantCulture, $"{stats.PowerPlayGoals}/{stats.PowerPlayOpportunities}"),
        string.Create(
            CultureInfo.InvariantCulture,
            $"{stats.PowerPlayGoalsAgainst}/{stats.TimesShorthanded}"));

    private static LeagueGameGoal MapGoal(GoalRowDto goal) => new(
        goal.TeamId,
        int.Parse(goal.GameTime.Period, CultureInfo.InvariantCulture),
        ToElapsed(goal.GameTime),
        MapParticipant(goal.Participant),
        [.. goal.Assists.Select(MapParticipant)],
        goal.IsPowerplay,
        goal.IsShorthanded,
        goal.IsEmptyNet,
        goal.IsPenaltyShot);

    private static LeagueGamePenalty MapPenalty(PenaltyRowDto penalty) => new(
        penalty.TeamId,
        int.Parse(penalty.GameTime.Period, CultureInfo.InvariantCulture),
        ToElapsed(penalty.GameTime),
        MapParticipant(penalty.Participant),
        // The league publishes the English label; the French one, when present, is what a
        // French-speaking team reads.
        penalty.I18n?.Fr?.Infraction ?? penalty.Infraction,
        penalty.Duration?.Name ?? string.Empty,
        penalty.IsBenchPenalty);

    private static LeagueGameParticipant MapParticipant(ParticipantRowDto participant) => new(
        participant.FullName,
        participant.Number,
        participant.IsAffiliate);

    private static string ToElapsed(GameTimeRowDto clock) => string.Create(
        CultureInfo.InvariantCulture,
        $"{clock.ElapsedMinutes}:{clock.ElapsedSeconds:D2}");

    private static string BuildGamesFilter(int leagueId, int? teamId, DateOnly startDate, DateOnly endDate)
    {
        // Built as a document: the filter is nested JSON and string interpolation
        // is how braces end up unbalanced.
        var clauses = new List<JsonNode>
        {
            new JsonObject { ["scheduleId"] = leagueId },
            new JsonObject
            {
                ["date"] = new JsonObject
                {
                    ["between"] = new JsonArray(
                        startDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        endDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                },
            },
        };

        if (teamId is { } id)
        {
            clauses.Add(new JsonObject
            {
                ["or"] = new JsonArray(
                    new JsonObject { ["awayTeamId"] = id },
                    new JsonObject { ["homeTeamId"] = id }),
            });
        }

        var filter = new JsonObject
        {
            ["order"] = new JsonArray("startTime ASC", "number ASC"),
            ["where"] = new JsonObject { ["and"] = new JsonArray([.. clauses]) },
            ["include"] = new JsonArray("teamStats", "awayTeam", "homeTeam"),
        };

        return filter.ToJsonString();
    }

    private static string ToLocalTime(string utcTimestamp, string? timeZoneId)
    {
        var utc = DateTimeOffset.Parse(
            utcTimestamp,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind).UtcDateTime;

        const string fallbackTimeZoneId = "America/Toronto";
        var timeZone = ResolveTimeZone(timeZoneId, fallbackTimeZoneId);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, timeZone).ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    private static TimeZoneInfo ResolveTimeZone(string? timeZoneId, string fallbackTimeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return TimeZoneInfo.FindSystemTimeZoneById(fallbackTimeZoneId);
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById(fallbackTimeZoneId);
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById(fallbackTimeZoneId);
        }
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Debug,
        Message = "Received {ByteCount} bytes from '{RelativeUri}'.")]
    private static partial void LogReceivedPayload(ILogger logger, int byteCount, string relativeUri);
}
