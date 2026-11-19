using System.Globalization;
using HockeySeason.Application;
using HockeySeason.Infrastructure.Data;
using HockeySeason.LeagueApi;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;

namespace HockeySeason.Functions;

/// <summary>
/// Weekly report of the leagues the caller follows.
/// </summary>
/// <remarks>
/// The caller no longer chooses divisions and teams: the report covers exactly
/// the teams their <c>team_subscriptions</c> define, resolved from the
/// database — never a hardcoded list. Each followed team brings its league (all
/// of its schedules: a category is played in several ones) and the live
/// standings, games and suspensions are read from Spordle with the schedule ids
/// stored in the database. A caller who follows nothing gets an empty report.
/// </remarks>
public sealed class WeeklyReportFunction(
    IWeeklyReportService reports,
    ICallerDataScopeService dataScope,
    RequestUserResolver resolver,
    IConfiguration configuration)
{
    [Function("WeeklyReport")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "weekly-report")] HttpRequest httpRequest,
        FunctionContext context, CancellationToken cancellationToken)
    {
        var authorization = FunctionsAuthorization.IsAuthenticatedV2(context);
        if (!authorization.IsAuthenticated)
        {
            return FunctionsAuthorization.UnauthorizedResult(authorization.Reason);
        }

        var query = httpRequest.Query;
        if (!TryParseDates(query["startDate"], query["endDate"], out var start, out var end))
        {
            return new BadRequestObjectResult(new ValidationProblemDetails(
                new Dictionary<string, string[]> { ["query"] = ["Provide valid yyyy-MM-dd dates with startDate on or before endDate."] }));
        }

        var leagues = await ResolveLeaguesAsync(context, cancellationToken);
        var report = await reports.GetAsync(start, end, leagues, cancellationToken);
        return new OkObjectResult(report);
    }

    /// <summary>
    /// Leagues of the report, built from the teams the caller follows: one
    /// entry per (followed team, schedule) pair, so a league played in several
    /// schedules reports every figure of the followed team. Followed leagues
    /// and associations are intentionally not expanded here: the weekly report
    /// is about the teams I follow.
    /// </summary>
    private async Task<IReadOnlyList<WeeklyReportLeague>> ResolveLeaguesAsync(
        FunctionContext context, CancellationToken cancellationToken)
    {
        if (!DatabaseOptions.IsPersistenceConfigured(configuration))
        {
            // No persistence: nothing is followed, so there is nothing to report.
            return [];
        }

        var userId = await resolver.GetOrCreateUserIdAsync(context, cancellationToken);
        var teams = await dataScope.GetFollowedTeamsAsync(userId, cancellationToken);

        return [.. teams
            .SelectMany(team => team.Schedules.Select(schedule => new WeeklyReportLeague(
                team.DivisionId,
                team.DivisionName,
                schedule.Id,
                schedule.Name,
                team.LeagueName,
                [team.ExternalId])))
            // Two followed teams of the same league share the schedule: their
            // ids are merged so the league is fetched once.
            .GroupBy(league => league.ScheduleId)
            .Select(group => group.First() with
            {
                TeamIds = [.. group.SelectMany(league => league.TeamIds).Distinct().Order()],
            })
            .OrderBy(league => league.LeagueName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(league => league.ScheduleId)];    }

    private static bool TryParseDates(string? startDate, string? endDate, out DateOnly start, out DateOnly end)
    {
        start = default;
        end = default;
        return DateOnly.TryParseExact(startDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out start)
            && DateOnly.TryParseExact(endDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out end)
            && start <= end;
    }
}
