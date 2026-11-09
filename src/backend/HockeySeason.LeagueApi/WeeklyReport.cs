using System.Globalization;
using System.Net;
using System.Text.Json;
using HockeySeason.LeagueApi.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace HockeySeason.LeagueApi;

/// <summary>Validated parameters for a weekly report.</summary>
public sealed record WeeklyReportRequest(DateOnly StartDate, DateOnly EndDate, int[] DivisionIds, int[] TeamIds)
{
    public static bool TryParse(
        string? startDate, string? endDate, StringValues divisionIds, StringValues teamIds,
        out WeeklyReportRequest? request)
    {
        request = null;
        if (!DateOnly.TryParseExact(startDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
            || !DateOnly.TryParseExact(endDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end)
            || start > end
            || !TryParseIds(divisionIds, out var divisions)
            || !TryParseIds(teamIds, out var teams))
        {
            return false;
        }

        request = new(start, end, divisions, teams);
        return true;
    }

    private static bool TryParseIds(StringValues values, out int[] ids)
    {
        ids = [];
        if (values.Count is 0 or > 50)
            return false;

        var parsed = new List<int>();
        foreach (var value in values)
        {
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
                return false;
            if (!parsed.Contains(id))
                parsed.Add(id);
        }
        ids = [.. parsed];
        return true;
    }
}

/// <summary>
/// One unmodified Spordle response, or an explicit unavailable status carrying
/// a human-readable failure reason (HTTP status, timeout or invalid payload).
/// </summary>
public sealed record WeeklyReportSource(bool Available, JsonElement? Data, string? Error);

/// <summary>Raw Spordle data for a requested division.</summary>
public sealed record WeeklyReportDivision(
    int DivisionId, string? LeagueName, WeeklyReportSource Standings, WeeklyReportSource Games, WeeklyReportSource Suspensions);

/// <summary>Raw source data and selection parameters for the report.</summary>
public sealed record WeeklyReportResponse(
    DateOnly StartDate, DateOnly EndDate, int[] TeamIds, IReadOnlyList<WeeklyReportDivision> Divisions);

public interface IWeeklyReportService
{
    Task<WeeklyReportResponse> GetAsync(WeeklyReportRequest request, CancellationToken cancellationToken);
}

public sealed class WeeklyReportService(
    LeagueApiService leagueApi,
    IOptions<LeagueApiOptions> options,
    ILogger<WeeklyReportService> logger) : IWeeklyReportService
{    public async Task<WeeklyReportResponse> GetAsync(WeeklyReportRequest request, CancellationToken cancellationToken)
    {
        var leagueNames = await GetLeagueNamesAsync(cancellationToken);
        var divisions = await Task.WhenAll(request.DivisionIds.Select(id => GetDivisionAsync(id, request, cancellationToken)));
        return new(request.StartDate, request.EndDate, request.TeamIds,
            divisions.Select(division => division with { LeagueName = leagueNames.GetValueOrDefault(division.DivisionId) }).ToArray());
    }

    private async Task<IReadOnlyDictionary<int, string>> GetLeagueNamesAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var attemptCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                attemptCancellation.CancelAfter(TimeSpan.FromSeconds(8));
                var leagues = await leagueApi.GetLeaguesAsync(
                    options.Value.DefaultOfficeId, options.Value.DefaultSeasonId, attemptCancellation.Token);
                return leagues.ToDictionary(league => league.Id, league => league.Name);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var transient = ex is not HttpRequestException http
                    || http.StatusCode is null or HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
                    || (int)http.StatusCode >= 500;
                logger.LogWarning(ex, "Spordle leagues unavailable, attempt {Attempt}", attempt);
                if (!transient || attempt >= 3)
                    return new Dictionary<int, string>();
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken);
            }
        }
    }

    private async Task<WeeklyReportDivision> GetDivisionAsync(int id, WeeklyReportRequest request, CancellationToken cancellationToken)
    {
        var standings = FetchAsync(id, "standings", ct => leagueApi.GetRawStandingsAsync(id, ct), cancellationToken);
        var games = FetchAsync(id, "games", ct => leagueApi.GetRawGamesAsync(id, request.StartDate, request.EndDate, ct), cancellationToken);
        var suspensions = FetchAsync(id, "suspensions", ct => leagueApi.GetRawSuspensionsAsync(id, ct), cancellationToken);
        await Task.WhenAll(standings, games, suspensions);
        return new(id, null, await standings, await games, await suspensions);
    }

    private async Task<WeeklyReportSource> FetchAsync(
        int divisionId, string source, Func<CancellationToken, Task<JsonElement>> fetch, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            string lastError;
            try
            {                using var attemptCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                attemptCancellation.CancelAfter(TimeSpan.FromSeconds(8));
                var data = await fetch(attemptCancellation.Token);
                if (data.ValueKind != JsonValueKind.Array)
                    throw new JsonException("Spordle returned a non-array response.");
                return new(true, data, null);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var transient = ex is not HttpRequestException http
                    || http.StatusCode is null or HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
                    || (int)http.StatusCode >= 500;
                logger.LogWarning(ex, "Spordle {Source} unavailable for division {DivisionId}, attempt {Attempt}", source, divisionId, attempt);
                lastError = Describe(ex);
                if (!transient || attempt >= 3)
                    return new(false, null, lastError);
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken);
            }
        }
    }

    /// <summary>
    /// Builds the client-facing reason for a failed Spordle fetch, so the
    /// weekly report can show why a source was unavailable.
    /// </summary>
    private static string Describe(Exception exception) => exception switch
    {
        HttpRequestException { StatusCode: var status } when status is not null
            => $"HTTP {(int)status} {status} from Spordle",
        HttpRequestException => $"The Spordle API could not be reached: {exception.InnerException?.Message ?? exception.Message}",
        OperationCanceledException => "The Spordle API timed out after 8 seconds",
        JsonException json => $"Invalid Spordle response: {json.Message}",
        _ => exception.Message,
    };
}
