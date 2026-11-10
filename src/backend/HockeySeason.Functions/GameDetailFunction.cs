using HockeySeason.LeagueApi.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace HockeySeason.Functions;

public sealed class GameDetailFunction(LeagueApiService league)
{
    [Function("GameDetail")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "games/{gameId:int}")] HttpRequest request,
        int gameId,
        CancellationToken cancellationToken)
    {
        if (gameId <= 0)
            return new BadRequestObjectResult(new ValidationProblemDetails(
                new Dictionary<string, string[]> { ["gameId"] = ["The game ID must be positive."] }));

        return new OkObjectResult(await league.GetLeagueGameDetailAsync(gameId, cancellationToken));
    }
}
