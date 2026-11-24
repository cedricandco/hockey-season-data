using System.Text;
using System.Text.Json;
using HockeySeason.Application;
using HockeySeason.Infrastructure.Data;
using HockeySeason.LeagueApi;
using HockeySeason.LeagueApi.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace HockeySeason.Functions;

/// <summary>
/// Team discovery/detail triggers. Every trigger validates the JWT through
/// <see cref="FunctionsAuthorization"/> because AuthorizationLevel.Anonymous
/// delegates access control to the application (the ASP.NET Core middleware
/// pipeline does not provide endpoint metadata for Functions triggers).
/// </summary>
/// <remarks>
/// Discovery (offices, leagues, teams) is limited to the caller's follows,
/// resolved from the database: an id outside those follows is refused with a
/// 403 rather than silently fetched from Spordle. While
/// <c>Authorization:OpenAccess</c> is on, the whole reference data stays
/// readable.
/// </remarks>
public sealed class TeamFunctions(
    LeagueApiService league,
    ITeamDetailService teams,
    IOptions<LeagueApiOptions> options,
    ICallerDataScopeService dataScope,
    RequestUserResolver resolver,
    IConfiguration configuration)
{

    public class ClientPrincipal
    {
        public string IdentityProvider { get; set; } = "";
        public string UserId { get; set; } = "";
        public string UserDetails { get; set; } = "";
        public List<string> UserRoles { get; set; } = new();
        public List<ClientClaim> Claims { get; set; } = new();
    }

    public class ClientClaim
    {
        public string Typ { get; set; } = "";
        public string Val { get; set; } = "";
    }

    public static ClientPrincipal? GetClientPrincipal(HttpRequest request)
    {
        if (!request.Headers.TryGetValue("X-MS-CLIENT-PRINCIPAL", out var header))
            return null;

        var decoded = Encoding.UTF8.GetString(
            Convert.FromBase64String(header!));

        return JsonSerializer.Deserialize<ClientPrincipal>(
            decoded,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
    }

    [Function("Offices")]
    public IActionResult Offices([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "offices")] HttpRequest request, FunctionContext context)
    {
        var authorization = FunctionsAuthorization.IsAuthenticatedV2(context);
        if (!authorization.IsAuthenticated)
        {
            return FunctionsAuthorization.UnauthorizedResult(authorization.Reason);
        }
        return new OkObjectResult(LeagueOffices.All);
    }

    [Function("OfficeLeagues")]
    public async Task<IActionResult> Leagues([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "offices/{officeId:int}/leagues")] HttpRequest request,
        int officeId, FunctionContext context, CancellationToken cancellationToken)
    {
        var authorization = FunctionsAuthorization.IsAuthenticatedV2(context);
        if (!authorization.IsAuthenticated)
        {
            return FunctionsAuthorization.UnauthorizedResult(authorization.Reason);
        }
        if (officeId <= 0) return InvalidIds();

        var scope = await GetScopeAsync(context, cancellationToken);
        if (scope.IsRestricted && !scope.ContainsDivision(officeId))
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        var season = request.Query["seasonId"].ToString();
        return new OkObjectResult(await league.GetLeaguesAsync(officeId, string.IsNullOrWhiteSpace(season) ? options.Value.DefaultSeasonId : season, cancellationToken));
    }

    [Function("LeagueTeams")]
    public async Task<IActionResult> LeagueTeams([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "leagues/{leagueId:int}/teams")] HttpRequest request,
        int leagueId, FunctionContext context, CancellationToken cancellationToken)
    {
        var authorization = FunctionsAuthorization.IsAuthenticatedV2(context);
        if (!authorization.IsAuthenticated)
        {
            return FunctionsAuthorization.UnauthorizedResult(authorization.Reason);
        }
        if (leagueId <= 0) return InvalidIds();

        var scope = await GetScopeAsync(context, cancellationToken);
        if (scope.IsRestricted && !scope.ContainsSchedule(leagueId))
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        return new OkObjectResult(await league.GetLeagueTeamsAsync(leagueId, cancellationToken));
    }

    [Function("TeamDetail")]
    public async Task<IActionResult> TeamDetail([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "leagues/{leagueId:int}/teams/{teamId:int}")] HttpRequest request,
        int leagueId, int teamId, FunctionContext context, CancellationToken cancellationToken)
    {
        var authorization = FunctionsAuthorization.IsAuthenticatedV2(context);
        if (!authorization.IsAuthenticated)
        {
            return FunctionsAuthorization.UnauthorizedResult(authorization.Reason);
        }
        if (leagueId <= 0 || teamId <= 0) return InvalidIds();

        // Only a team the caller follows (or a team of a league they follow) is
        // readable: the id is never trusted from the query string.
        var scope = await GetScopeAsync(context, cancellationToken);
        if (scope.IsRestricted && !scope.ContainsTeam(teamId))
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        var result = await teams.GetAsync(leagueId, teamId, cancellationToken);
        return result is null ? new NotFoundResult() : new OkObjectResult(result);
    }

    /// <summary>
    /// Detail of a team the page may open, keyed on database ids: the team page
    /// passes a <c>teams.id</c> of its selectors, never a Spordle id. A team the
    /// caller follows may be opened, and so may any team of a league that
    /// contains one — following a team opens its whole league. The league is
    /// validated against that rule (403 otherwise) and the published schedule
    /// is resolved from the database, trying every schedule of the league
    /// because a category is played in several ones ("Saison Rives", "Phase
    /// 2"…).
    /// </summary>
    [Function("FollowedTeamDetail")]
    public async Task<IActionResult> FollowedTeamDetail(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "teams/{teamId:int}")] HttpRequest request,
        int teamId, FunctionContext context, CancellationToken cancellationToken)
    {
        var authorization = FunctionsAuthorization.IsAuthenticatedV2(context);
        if (!authorization.IsAuthenticated)
        {
            return FunctionsAuthorization.UnauthorizedResult(authorization.Reason);
        }
        if (teamId <= 0) return InvalidIds();
        if (!DatabaseOptions.IsPersistenceConfigured(configuration)) return new NotFoundResult();

        var userId = await resolver.GetOrCreateUserIdAsync(context, cancellationToken);
        var selectable = await dataScope.GetSelectableTeamAsync(userId, teamId, cancellationToken);
        if (selectable is null)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        var scheduleIds = selectable.Schedules.Select(schedule => schedule.Id).ToArray();
        if (scheduleIds.Length == 0)
            return new NotFoundResult();

        var result = await teams.GetAsync(scheduleIds, selectable.ExternalId, cancellationToken);
        return result is null ? new NotFoundResult() : new OkObjectResult(result);
    }

    /// <summary>
    /// Scope of the caller. Open access, and a host without persistence (which
    /// cannot resolve follows), keep the whole reference data readable; a
    /// restricted caller only sees their follows.
    /// </summary>
    private async Task<CallerDataScope> GetScopeAsync(FunctionContext context, CancellationToken cancellationToken)
    {
        var unrestricted = configuration.GetSection(AuthorizationOptions.SectionName).Get<AuthorizationOptions>()
            is { OpenAccess: true };
        if (!DatabaseOptions.IsPersistenceConfigured(configuration))
        {
            return CallerDataScope.Unrestricted([], [], [], []);
        }

        var userId = await resolver.GetOrCreateUserIdAsync(context, cancellationToken);
        return await dataScope.GetAsync(userId, unrestricted, cancellationToken);
    }

    private static BadRequestObjectResult InvalidIds() => new(new ValidationProblemDetails(
        new Dictionary<string, string[]> { ["ids"] = ["Identifiers must be positive."] }));
}
