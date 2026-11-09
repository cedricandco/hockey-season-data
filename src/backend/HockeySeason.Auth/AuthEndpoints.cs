using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace HockeySeason.Auth;

/// <summary>
/// Shared OIDC-style <c>POST /api/auth/login</c> endpoint used by the Angular
/// frontend (and by any future OIDC client) to exchange the 6-digit access
/// code for a signed JWT. Only anonymous (unauthenticated) requests are
/// accepted, so a valid token cannot be replayed to mint new tokens, and each
/// client IP is limited to a handful of attempts per minute.
/// </summary>
public static class AuthEndpoints
{
    public static IEndpointConventionBuilder MapLoginEndpoint(
        this IEndpointRouteBuilder endpoints,
        LoginRateGuard rateGuard)
    {
        var group = endpoints.MapGroup("/api/auth");
        group.MapPost("/login", (LoginRequest request, IAuthService auth, HttpContext http) =>
        {
            var clientKey = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            if (!rateGuard.TryAcquire(clientKey))
            {
                http.Response.Headers.RetryAfter = rateGuard.RetryAfterSeconds(clientKey).ToString();
                return Results.Json(
                    new { error = "too_many_attempts" },
                    statusCode: StatusCodes.Status429TooManyRequests);
            }

            var result = auth.Login(request.Code);
            return result.Success
                ? Results.Json(result, statusCode: StatusCodes.Status200OK)
                : Results.Json(new { error = "invalid_code" }, statusCode: StatusCodes.Status401Unauthorized);
        })
        // The endpoint only succeeds for unauthenticated callers; a valid
        // Bearer token must never be able to mint additional tokens.
        .AllowAnonymous();
        return group;
    }
}

/// <summary>Request body for the login endpoint.</summary>
public sealed record LoginRequest(string? Code);
