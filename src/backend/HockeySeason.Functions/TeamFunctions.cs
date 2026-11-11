using System.Text;
using System.Text.Json;
using HockeySeason.LeagueApi;
using HockeySeason.LeagueApi.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Options;

namespace HockeySeason.Functions;

/// <summary>
/// Team discovery/detail triggers. Every trigger validates the JWT through
/// <see cref="FunctionsAuthorization"/> because AuthorizationLevel.Anonymous
/// delegates access control to the application (the ASP.NET Core middleware
/// pipeline does not provide endpoint metadata for Functions triggers).
/// </summary>
public sealed class TeamFunctions(LeagueApiService league, ITeamDetailService teams, IOptions<LeagueApiOptions> options)
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
            var principal = GetClientPrincipal(request);

            if (principal == null)
            {
                return new UnauthorizedResult();
            }

            var userId = principal.UserId;
            var email = principal.UserDetails;
            var roles = principal.UserRoles;

            return FunctionsAuthorization.UnauthorizedResult(authorization.Reason + "  " + request.Headers["Authorization"].FirstOrDefault() + "    user id = " + userId + "; email = " + email + "; roles = " + roles);
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
        var result = await teams.GetAsync(leagueId, teamId, cancellationToken);
        return result is null ? new NotFoundResult() : new OkObjectResult(result);
    }

    private static BadRequestObjectResult InvalidIds() => new(new ValidationProblemDetails(
        new Dictionary<string, string[]> { ["ids"] = ["Identifiers must be positive."] }));
}
