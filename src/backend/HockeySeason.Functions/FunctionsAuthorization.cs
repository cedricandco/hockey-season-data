using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore;

namespace HockeySeason.Functions;

/// <summary>
/// Enforces authentication on Function HTTP triggers. The ASP.NET Core
/// integration does not attach endpoint metadata (routing is performed by the
/// Functions host), so authorization middleware alone never runs here; each
/// trigger validates through this helper after the startup filter has
/// authenticated the HttpContext.
/// </summary>
public static class FunctionsAuthorization
{
    /// <summary>
    /// Returns whether the caller has a valid token from the configured issuer/audience.
    /// </summary>
    public static bool IsAuthenticated(FunctionContext context)
    {
        var httpContext = context.GetHttpContext()
            ?? throw new InvalidOperationException("Function context has no HttpContext.");
        return httpContext.User.Identity?.IsAuthenticated == true;
    }
}
