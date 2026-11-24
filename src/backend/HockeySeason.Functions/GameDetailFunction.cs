using HockeySeason.Application;
using HockeySeason.Infrastructure.Data;
using HockeySeason.LeagueApi.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;

namespace HockeySeason.Functions;

/// <summary>
/// Box score of one game. A game is readable when at least one of its teams is
/// in the caller's readable scope — a followed team, or any team of a league
/// that contains one; otherwise the call is refused, so a game id cannot be
/// used to read a team outside the caller's follows.
/// </summary>
public sealed class GameDetailFunction(
    LeagueApiService league,
    ICallerDataScopeService dataScope,
    RequestUserResolver resolver,
    IConfiguration configuration)
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

        var unrestricted = configuration.GetSection(AuthorizationOptions.SectionName).Get<AuthorizationOptions>()
            is { OpenAccess: true };
        var noPersistence = !unrestricted && !DatabaseOptions.IsPersistenceConfigured(configuration);

        var game = await league.GetLeagueGameDetailAsync(gameId, cancellationToken);
        if (noPersistence)
        {
            return new OkObjectResult(game);
        }

        var userId = await resolver.GetOrCreateUserIdAsync(context, cancellationToken);
        var scope = await dataScope.GetAsync(userId, unrestricted, cancellationToken);
        var visible = !scope.IsRestricted
            || scope.ContainsTeam(game.HomeTeam.Id)
            || scope.ContainsTeam(game.AwayTeam.Id);
        return visible ? new OkObjectResult(game) : new StatusCodeResult(StatusCodes.Status403Forbidden);
    }
}
