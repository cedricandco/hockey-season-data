using System.Net;
using System.Text.Json;
using HockeySeason.LeagueApi.Http;
using Microsoft.Extensions.Logging;

namespace HockeySeason.LeagueApi;

/// <summary>
/// One unmodified Spordle response, or an explicit unavailable status carrying
/// a human-readable failure reason (HTTP status, timeout or invalid payload).
/// </summary>
public sealed record WeeklyReportSource(bool Available, JsonElement? Data, string? Error);

/// <summary>
/// Raw Spordle data for one followed league/schedule. <see cref="ScheduleId"/>
/// is the Spordle schedule the figures were read from (stored in the database),
/// <see cref="DivisionName"/> and <see cref="LeagueName"/> the database names of
/// the league the followed team belongs to, <see cref="ScheduleName"/> the
/// published schedule name, and <see cref="TeamIds"/> the followed teams the
/// card reports.
/// </summary>
public sealed record WeeklyReportDivision(
    int DivisionId,
    string DivisionName,
    string? LeagueName,
    int ScheduleId,
    string? ScheduleName,
    int[] TeamIds,
    WeeklyReportSource Standings,
    WeeklyReportSource Games,
    WeeklyReportSource Suspensions);

/// <summary>Raw source data and selection parameters for the report.</summary>
public sealed record WeeklyReportResponse(
    DateOnly StartDate, DateOnly EndDate, int[] TeamIds, IReadOnlyList<WeeklyReportDivision> Divisions);

/// <summary>
/// A league/schedule of the report: the league of a team the caller follows,
/// its Spordle schedule id (from the database) and the teams it reports. A
/// category played in several schedules yields one entry per schedule.
/// </summary>
public sealed record WeeklyReportLeague(
    int DivisionId, string DivisionName, int ScheduleId, string ScheduleName, string LeagueName, int[] TeamIds);

public interface IWeeklyReportService
{
    /// <summary>
    /// Builds the report of the given leagues. The leagues, their schedule ids
    /// and their teams come from the caller's follows (resolved by the host),
    /// so the service never receives an unrestricted list.
    /// </summary>
    Task<WeeklyReportResponse> GetAsync(
        DateOnly startDate, DateOnly endDate, IReadOnlyList<WeeklyReportLeague> leagues,
        CancellationToken cancellationToken);
}

/// <summary>
/// Weekly report over the public Spordle API: the live standings, games and
/// suspensions of the leagues the caller follows, read with the schedule ids
/// stored in the database.
/// </summary>
public sealed class WeeklyReportService(
    LeagueApiService leagueApi,
    ILogger<WeeklyReportService> logger) : IWeeklyReportService
{
    public async Task<WeeklyReportResponse> GetAsync(
        DateOnly startDate,
        DateOnly endDate,
        IReadOnlyList<WeeklyReportLeague> leagues,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(leagues);

        var divisions = await Task.WhenAll(
            leagues.Select(league => GetDivisionAsync(league, startDate, endDate, cancellationToken)));

        // A category is played in several schedules and a team only appears in
        // its own: a card whose standings do not publish any followed team is
        // dropped. An unavailable standings table proves nothing and keeps the
        // card, so a transient Spordle failure never hides a league.
        var reported = divisions.Where(division => PublishesAFollowedTeam(division)).ToArray();
        var teamIds = reported
            .SelectMany(division => division.TeamIds)
            .Distinct()
            .Order()
            .ToArray();
        return new(startDate, endDate, teamIds, reported);
    }

    private static bool PublishesAFollowedTeam(WeeklyReportDivision division) =>
        !division.Standings.Available
        || division.Standings.Data is not { ValueKind: JsonValueKind.Array } standings
        || standings.EnumerateArray().Any(row =>
            row.TryGetProperty("teamId", out var teamId)
            && teamId.TryGetInt32(out var id)
            && division.TeamIds.Contains(id));

    private async Task<WeeklyReportDivision> GetDivisionAsync(
        WeeklyReportLeague league, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken)
    {
        var scheduleId = league.ScheduleId;
        var standings = FetchAsync(league.DivisionId, "standings", ct => leagueApi.GetRawStandingsAsync(scheduleId, ct), cancellationToken);
        var games = FetchAsync(league.DivisionId, "games", ct => leagueApi.GetRawGamesAsync(scheduleId, startDate, endDate, ct), cancellationToken);
        var suspensions = FetchAsync(league.DivisionId, "suspensions", ct => leagueApi.GetRawSuspensionsAsync(scheduleId, ct), cancellationToken);
        await Task.WhenAll(standings, games, suspensions);
        return new(
            league.DivisionId,
            league.DivisionName,
            league.LeagueName,
            scheduleId,
            league.ScheduleName,
            league.TeamIds,
            await standings,
            await games,
            await suspensions);
    }

    private async Task<WeeklyReportSource> FetchAsync(
        int divisionId, string source, Func<CancellationToken, Task<JsonElement>> fetch, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            string lastError;
            try
            {
                using var attemptCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
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
