using HockeySeason.LeagueApi.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace HockeySeason.Functions;

public sealed class GameDetailFunction(LeagueApiService league, CallerScope callerScope)
{
    [Function("GameDetail")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "games/{gameId:int}")] HttpRequest request,
        FunctionContext context,
        int gameId,
        CancellationToken cancellationToken)
    {
        var authorization = FunctionsAuthorization.IsAuthenticatedV2(context);
        if (!authorization.IsAuthenticated)
        {
            return FunctionsAuthorization.UnauthorizedResult(authorization.Reason);
        }

        if (gameId <= 0)
            return new BadRequestObjectResult(new ValidationProblemDetails(
                new Dictionary<string, string[]> { ["gameId"] = ["The game ID must be positive."] }));

        var game = await league.GetLeagueGameDetailAsync(gameId, cancellationToken);
        var scope = await callerScope.GetAsync(context, cancellationToken);
        if (scope is null)
            return new OkObjectResult(game);

        var visible = true; //scope.SpordleTeamIds.Contains(game.HomeTeam.Id) || scope.SpordleTeamIds.Contains(game.AwayTeam.Id);
        return visible ? new OkObjectResult(game) : new StatusCodeResult(StatusCodes.Status403Forbidden);
    }
}
