using HockeySeason.Application;
using HockeySeason.Infrastructure.Data;
using HockeySeason.LeagueApi;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;

namespace HockeySeason.Functions;

/// <summary>
/// Anonymous preview of the reference data, used by the landing page shown
/// before sign-in: association → division → league → team, then the same team
/// detail the authenticated team page renders.
/// </summary>
/// <remarks>
/// These triggers are the only ones that accept a caller without a token, and
/// they only read imported reference data plus the public Spordle schedule of
/// a team. They are deliberately separate from the authenticated
/// <c>subscriptions/*</c> and <c>teams/*</c> routes so the anonymous surface
/// stays explicit and easy to audit. The visitor's three free attempts are
/// counted by the browser (<c>localStorage</c>): the API cannot see them,
/// and the account creation is what actually keeps the data available.
/// </remarks>
public sealed class PreviewFunctions(
    ISubscriptionCatalogService catalog,
    IPreviewService preview,
    ITeamDetailService teams,
    IConfiguration configuration)
{
    /// <summary>Associations that own imported data: the first selector level.</summary>
    [Function("PreviewAssociations")]
    public async Task<IActionResult> Associations(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "preview/associations")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (!DatabaseOptions.IsPersistenceConfigured(configuration)) return new OkObjectResult(new { associations = Array.Empty<object>() });

        var associations = await catalog.GetAssociationsAsync(cancellationToken);
        return new OkObjectResult(new
        {
            associations = associations.Select(item => new { item.Id, item.Name }),
        });
    }

    /// <summary>Divisions (Spordle offices) of one association.</summary>
    [Function("PreviewDivisions")]
    public async Task<IActionResult> Divisions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "preview/associations/{associationId:int}/divisions")]
        HttpRequest request, int associationId, CancellationToken cancellationToken)
    {
        if (associationId <= 0) return InvalidId("associationId");
        if (!DatabaseOptions.IsPersistenceConfigured(configuration)) return new OkObjectResult(new { divisions = Array.Empty<object>() });

        var divisions = await catalog.GetDivisionsAsync(associationId, cancellationToken);
        return new OkObjectResult(new
        {
            divisions = divisions.Select(item => new { item.Id, item.Name }),
        });
    }

    /// <summary>Leagues of a division for the latest imported season.</summary>
    [Function("PreviewLeagues")]
    public async Task<IActionResult> Leagues(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "preview/divisions/{divisionId:int}/leagues")]
        HttpRequest request, int divisionId, CancellationToken cancellationToken)
    {
        if (divisionId <= 0) return InvalidId("divisionId");
        if (!DatabaseOptions.IsPersistenceConfigured(configuration))
        {
            return new OkObjectResult(new { seasonId = (string?)null, leagues = Array.Empty<object>() });
        }

        var options = await catalog.GetLeaguesAsync(divisionId, seasonId: null, cancellationToken);
        return new OkObjectResult(new
        {
            options.SeasonId,
            leagues = options.Leagues.Select(league => new { league.Id, league.Name }),
        });
    }

    /// <summary>Teams of one league (Spordle category guid), keyed on database ids.</summary>
    [Function("PreviewTeams")]
    public async Task<IActionResult> Teams(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "preview/leagues/{leagueId}/teams")]
        HttpRequest request, string leagueId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(leagueId)) return InvalidId("leagueId");
        if (!DatabaseOptions.IsPersistenceConfigured(configuration)) return new OkObjectResult(new { teams = Array.Empty<object>() });

        var teamsOfLeague = await preview.GetTeamsAsync(leagueId, cancellationToken);
        return new OkObjectResult(new
        {
            teams = teamsOfLeague.Select(team => new { team.Id, team.Name }),
        });
    }

    /// <summary>
    /// Detail of one team by database id, with the Spordle ↔ database team
    /// mapping so the shared statistics component can render the score lines
    /// exactly as the authenticated team page does.
    /// </summary>
    [Function("PreviewTeamDetail")]
    public async Task<IActionResult> TeamDetail(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "preview/teams/{teamId:int}")]
        HttpRequest request, int teamId, CancellationToken cancellationToken)
    {
        if (teamId <= 0) return InvalidId("teamId");
        if (!DatabaseOptions.IsPersistenceConfigured(configuration)) return new NotFoundResult();

        var selectable = await preview.GetTeamAsync(teamId, cancellationToken);
        var scheduleIds = selectable?.Schedules.Select(schedule => schedule.Id).ToArray() ?? [];
        if (selectable is null || scheduleIds.Length == 0) return new NotFoundResult();

        var result = await teams.GetAsync(scheduleIds, selectable.ExternalId, cancellationToken);
        if (result is null) return new NotFoundResult();

        var leagueTeams = await preview.GetLeagueTeamLinksAsync(selectable.LeagueId, cancellationToken);
        return new OkObjectResult(TeamPageDetailResponse.From(result,
            [.. leagueTeams.Select(link => new TeamPageLink(link.ExternalId, link.TeamId))]));
    }

    private static BadRequestObjectResult InvalidId(string name) => new(new ValidationProblemDetails(
        new Dictionary<string, string[]> { [name] = ["The identifier must be positive."] }));
}
