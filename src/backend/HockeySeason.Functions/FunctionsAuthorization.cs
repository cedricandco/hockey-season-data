using HockeySeason.Auth;
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
/// <remarks>
/// Belt-and-braces: on the Static Web Apps managed Functions host the
/// <c>UseAuthentication</c> startup filter is not always applied to trigger
/// invocations (it works with the local func CLI but not in the cloud), which
/// left <c>HttpContext.User</c> anonymous and caused blanket 401s. When the
/// principal is unauthenticated, the raw <c>Authorization</c> header is
/// validated directly with the same issuer/audience/key parameters, so the
/// JWT contract is identical in both hosts.
/// </remarks>
public static class FunctionsAuthorization
{
    /// <summary>
    /// Returns whether the caller carries a valid token from the configured
    /// issuer/audience, either via the ASP.NET Core principal or via direct
    /// JWT validation of the Authorization header.
    /// </summary>
    public static bool IsAuthenticated(FunctionContext context)
    {
        var httpContext = context.GetHttpContext()
            ?? throw new InvalidOperationException("Function context has no HttpContext.");
        if (httpContext.User.Identity?.IsAuthenticated == true)
        {
            return true;
        }

        if (context.InstanceServices is not { } services || services.GetService(typeof(IJwtValidator)) is not IJwtValidator validator)
        {
            return false;
        }

        return JwtBearerTokenParser.TryExtractBearerToken(httpContext.Request.Headers.Authorization.ToString(), out var token)
            && validator.TryValidate(token, out _);
    }

    public static (bool IsAuthenticated, string? Reason) IsAuthenticatedV2(FunctionContext context)
    {
        var httpContext = context.GetHttpContext()
            ?? throw new InvalidOperationException("Function context has no HttpContext.");
        if (httpContext.User.Identity?.IsAuthenticated == true)
        {
            return (true, null);
        }

        if (context.InstanceServices is not { } services || services.GetService(typeof(IJwtValidator)) is not IJwtValidator validator)
        {
            return (false, "no_validator");
        }

        if (JwtBearerTokenParser.TryExtractBearerToken(httpContext.Request.Headers.Authorization.ToString(), out var token))
        {
            var (isValid, reason) = validator.TryValidateV2(token, out _);
            return (isValid, reason);
        }

        return (false, "missing_token");
    }
}

/// <summary>Parses the Bearer token out of a raw Authorization header value.</summary>
internal static class JwtBearerTokenParser
{
    public static bool TryExtractBearerToken(string? authorizationHeader, out string token)
    {
        token = string.Empty;
        if (string.IsNullOrWhiteSpace(authorizationHeader))
        {
            return false;
        }

        const string Scheme = "Bearer ";
        if (!authorizationHeader.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        token = authorizationHeader[Scheme.Length..].Trim();
        return token.Length > 0;
    }
}
