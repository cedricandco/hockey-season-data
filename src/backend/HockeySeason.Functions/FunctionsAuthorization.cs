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
    /// Throws <see cref="UnauthorizedAccessException"/> (surfaced as HTTP 401
    /// by the Functions host) when the caller does not carry a valid token
    /// from the configured issuer/audience.
    /// </summary>
    public static void RequireAuthenticatedUser(FunctionContext context)
    {
        var httpContext = context.GetHttpContext()
            ?? throw new InvalidOperationException("Function context has no HttpContext.");
        if (httpContext.User.Identity?.IsAuthenticated != true)
        {
            throw new UnauthorizedAccessException("Missing or invalid bearer token.");
        }
    }
}
