using HockeySeason.Application;
using HockeySeason.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;

namespace HockeySeason.Functions;

/// <summary>
/// Navigation of the team page, limited to the caller's follows and the
/// leagues they open.
/// </summary>
/// <remarks>
/// The reference data comes from the database and every identifier returned is
/// a database id: an association is its <c>organizations.id</c>, a division its
/// <c>divisions.id</c>, a league its <c>leagues.id</c> and a team its
/// <c>teams.id</c>. Associations, divisions and leagues list only what owns a
/// followed team — a team outside the follows can neither be reached nor
/// guessed — but once a league is opened (one of its teams is followed, or the
/// league/association is followed) all of its teams are listed: following a
/// team opens its whole league. The Spordle ids stay behind the API: the
/// detail endpoint resolves the published schedule itself.
/// </remarks>
public sealed class SubscriptionNavigationFunctions(
    ICallerDataScopeService dataScope,
    RequestUserResolver resolver,
    IConfiguration configuration)
{
    [Function("NavAssociations")]
    public async Task<IActionResult> Associations(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "nav/associations")] HttpRequest request,
        FunctionContext context, CancellationToken cancellationToken)
    {
        if (Unauthorized(context) is { } denied) return denied;

        var teams = await GetTeamsAsync(context, cancellationToken);
        return new OkObjectResult(teams
            .DistinctBy(team => team.AssociationId)
            .Select(team => new { AssociationId = team.AssociationId, Name = team.AssociationName })
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase));
    }

    [Function("NavDivisions")]
    public async Task<IActionResult> Divisions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "nav/associations/{associationId:int}/divisions")] HttpRequest request,
        int associationId, FunctionContext context, CancellationToken cancellationToken)
    {
        if (Unauthorized(context) is { } denied) return denied;
        if (associationId <= 0) return InvalidId();

        var teams = await GetTeamsAsync(context, cancellationToken);
        return new OkObjectResult(teams
            .Where(team => team.AssociationId == associationId)
            .DistinctBy(team => team.DivisionId)
            .Select(team => new { DivisionId = team.DivisionId, Name = team.DivisionName })
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase));
    }

    [Function("NavLeagues")]
    public async Task<IActionResult> Leagues(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "nav/divisions/{divisionId:int}/leagues")] HttpRequest request,
        int divisionId, FunctionContext context, CancellationToken cancellationToken)
    {
        if (Unauthorized(context) is { } denied) return denied;
        if (divisionId <= 0) return InvalidId();

        var teams = await GetTeamsAsync(context, cancellationToken);
        return new OkObjectResult(teams
            .Where(team => team.DivisionId == divisionId)
            .DistinctBy(team => team.LeagueId)
            .Select(team => new { LeagueId = team.LeagueId, Name = team.LeagueName })
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase));
    }

    [Function("NavTeams")]
    public async Task<IActionResult> Teams(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "nav/leagues/{leagueId:int}/teams")] HttpRequest request,
        int leagueId, FunctionContext context, CancellationToken cancellationToken)
    {
        if (Unauthorized(context) is { } denied) return denied;
        if (leagueId <= 0) return InvalidId();

        var teams = await GetLeagueTeamsAsync(context, leagueId, cancellationToken);
        return new OkObjectResult(teams
            .Select(team => new { TeamId = team.Id, team.Name })
            .OrderBy(team => team.Name, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Every team the caller follows, in one list: the first component of the
    /// team page. Each entry carries the league, division and association name
    /// so the list is readable without another call.
    /// </summary>
    [Function("NavFollowedTeams")]
    public async Task<IActionResult> FollowedTeams(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "nav/teams")] HttpRequest request,
        FunctionContext context, CancellationToken cancellationToken)
    {
        if (Unauthorized(context) is { } denied) return denied;

        var teams = await GetTeamsAsync(context, cancellationToken);
        return new OkObjectResult(teams.Select(team => new
        {
            TeamId = team.Id,
            team.Name,
            team.LeagueId,
            team.LeagueName,
            team.DivisionId,
            team.DivisionName,
            team.AssociationId,
            team.AssociationName,
        }));
    }

    /// <summary>
    /// Teams followed by the caller. A host without persistence cannot resolve
    /// follows, so it lists nothing rather than the whole catalogue.
    /// </summary>
    private async Task<IReadOnlyList<FollowedTeam>> GetTeamsAsync(
        FunctionContext context, CancellationToken cancellationToken)
    {
        if (!DatabaseOptions.IsPersistenceConfigured(configuration))
        {
            return [];
        }

        var userId = await resolver.GetOrCreateUserIdAsync(context, cancellationToken);
        return await dataScope.GetFollowedTeamsAsync(userId, cancellationToken);
    }

    /// <summary>
    /// Teams the team page can open in one league: all of them once the league
    /// holds a followed team — following a team opens its whole league — and
    /// nothing otherwise (and nothing for a host without persistence, which
    /// cannot resolve follows).
    /// </summary>
    private async Task<IReadOnlyList<SelectableTeam>> GetLeagueTeamsAsync(
        FunctionContext context, int leagueId, CancellationToken cancellationToken)
    {
        if (!DatabaseOptions.IsPersistenceConfigured(configuration))
        {
            return [];
        }

        var userId = await resolver.GetOrCreateUserIdAsync(context, cancellationToken);
        return await dataScope.GetLeagueTeamsAsync(userId, leagueId, cancellationToken);
    }

    private static ObjectResult? Unauthorized(FunctionContext context)
    {
        var authorization = FunctionsAuthorization.IsAuthenticatedV2(context);
        return authorization.IsAuthenticated ? null : FunctionsAuthorization.UnauthorizedResult(authorization.Reason);
    }

    private static BadRequestObjectResult InvalidId() => new(new ValidationProblemDetails(
        new Dictionary<string, string[]> { ["id"] = ["The identifier must be positive."] }));
}