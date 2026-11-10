using HockeySeason.Auth;
using HockeySeason.LeagueApi;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace HockeySeason.Functions;

public sealed class WeeklyReportFunction(IWeeklyReportService reports)
{
    [Function("WeeklyReport")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "weekly-report")] HttpRequest httpRequest,
        FunctionContext context, CancellationToken cancellationToken)
    {
        if (!FunctionsAuthorization.IsAuthenticated(context))
        {
            return new ObjectResult(new { error = "invalid_token", error_description = "Missing or invalid bearer token" })
            {
                StatusCode = StatusCodes.Status401Unauthorized,
            };
        }

        var query = httpRequest.Query;
        if (!WeeklyReportRequest.TryParse(query["startDate"], query["endDate"], query["divisionId"], query["teamId"], out var request))
            return new BadRequestObjectResult(new ValidationProblemDetails(
                new Dictionary<string, string[]> { ["query"] = ["Provide valid yyyy-MM-dd dates and positive divisionId and teamId parameters (at most 50 each)."] }));
        return new OkObjectResult(await reports.GetAsync(request!, cancellationToken));
    }
}
