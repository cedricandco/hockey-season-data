using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace HockeySeason.Functions;

/// <summary>
/// TEMPORARY diagnostic endpoint (no secrets echoed): reports whether the
/// Authorization header reaches the trigger and whether the security principal
/// is populated. Remove after the SWA header investigation.
/// </summary>
public sealed class AuthProbeFunction(ILogger<AuthProbeFunction> logger)
{
    [Function("AuthProbe")]
    public IActionResult Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "auth-probe")] HttpRequest request, FunctionContext context)
    {
        var header = request.Headers.Authorization.ToString();
        var userAuthenticated = request.HttpContext.User.Identity?.IsAuthenticated == true;
        logger.LogInformation(
            "AuthProbe: headerPresent={HeaderPresent} headerLength={HeaderLength} startsWithBearer={StartsWithBearer} userAuthenticated={UserAuthenticated}",
            header.Length > 0, header.Length, header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase), userAuthenticated);
        return new OkObjectResult(new
        {
            headerPresent = header.Length > 0,
            headerLength = header.Length,
            startsWithBearer = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase),
            userAuthenticated
        });
    }
}
