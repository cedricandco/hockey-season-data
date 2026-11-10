using HockeySeason.LeagueApi;
using HockeySeason.LeagueApi.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Options;

namespace HockeySeason.Functions;

public sealed class TeamFunctions(LeagueApiService league, ITeamDetailService teams, IOptions<LeagueApiOptions> options)
{
    [Function("Offices")]
    public IActionResult Offices([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "offices")] HttpRequest request)
        => new OkObjectResult(LeagueOffices.All);

    [Function("OfficeLeagues")]
    public async Task<IActionResult> Leagues([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "offices/{officeId:int}/leagues")] HttpRequest request,
        int officeId, CancellationToken cancellationToken)
    {
        if (officeId <= 0) return InvalidIds();
        var season = request.Query["seasonId"].ToString();
        return new OkObjectResult(await league.GetLeaguesAsync(officeId, string.IsNullOrWhiteSpace(season) ? options.Value.DefaultSeasonId : season, cancellationToken));
    }

    [Function("LeagueTeams")]
    public async Task<IActionResult> LeagueTeams([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "leagues/{leagueId:int}/teams")] HttpRequest request,
        int leagueId, CancellationToken cancellationToken)
    {
        if (leagueId <= 0) return InvalidIds();
        return new OkObjectResult(await league.GetLeagueTeamsAsync(leagueId, cancellationToken));
    }

    [Function("TeamDetail")]
    public async Task<IActionResult> TeamDetail([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "leagues/{leagueId:int}/teams/{teamId:int}")] HttpRequest request,
        int leagueId, int teamId, CancellationToken cancellationToken)
    {
        if (leagueId <= 0 || teamId <= 0) return InvalidIds();
        var result = await teams.GetAsync(leagueId, teamId, cancellationToken);
        return result is null ? new NotFoundResult() : new OkObjectResult(result);
    }

    private static BadRequestObjectResult InvalidIds() => new(new ValidationProblemDetails(
        new Dictionary<string, string[]> { ["ids"] = ["Identifiers must be positive."] }));
}
