using HockeySeason.Application;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Hosting;

namespace HockeySeason.Functions;

public sealed class HealthFunction(IHealthService healthService, IHostEnvironment environment)
{
    [Function("Health")]
    public IActionResult Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequest request, FunctionContext context)
    {
        FunctionsAuthorization.RequireAuthenticatedUser(context);
        return new OkObjectResult(new HealthReport(healthService.GetServerTime(), environment.EnvironmentName));
    }
}
