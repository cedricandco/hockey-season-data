using HockeySeason.LeagueApi;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace HockeySeason.Functions;

public sealed class WeeklyReportFunction(IWeeklyReportService reports, CallerScope callerScope)
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
        if (!WeeklyReportRequest.TryParse(query["startDate"], query["endDate"], query["divisionId"], query["teamId"], out var request))
            return new BadRequestObjectResult(new ValidationProblemDetails(
                new Dictionary<string, string[]> { ["query"] = ["Provide valid yyyy-MM-dd dates and positive divisionId and teamId parameters (at most 50 each)."] }));

        var scope = await callerScope.GetAsync(context, cancellationToken);
        if (scope is null)
            return new OkObjectResult(await reports.GetAsync(request!, cancellationToken));

        // Ids out of scope are dropped; a fully out-of-scope request is 403.
        var divisionIds = request!.DivisionIds.Where(id => scope.SpordleOfficeIds.Contains(id)).ToArray();
        var teamIds = request.TeamIds.Where(id => scope.SpordleTeamIds.Contains(id)).ToArray();
        // if (divisionIds.Length == 0 && teamIds.Length == 0)
        //     return new StatusCodeResult(StatusCodes.Status403Forbidden);
        return new OkObjectResult(await reports.GetAsync(request with { DivisionIds = divisionIds, TeamIds = teamIds }, cancellationToken));
    }
}
