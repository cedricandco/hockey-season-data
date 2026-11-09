using System.Text.Json;
using HockeySeason.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace HockeySeason.Functions;

/// <summary>
/// OIDC-style login endpoint for the Azure Functions host: exchanges the
/// shared 6-digit access code for a signed JWT. Shares
/// <see cref="IAuthService"/> and <see cref="LoginRateGuard"/> with the
/// minimal API so both hosts behave identically.
/// </summary>
public sealed class LoginFunction(IAuthService auth, LoginRateGuard rateGuard, ILogger<LoginFunction> logger)
{
    [Function("Login")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/login")] HttpRequest request,
        FunctionContext context,
        CancellationToken cancellationToken)
    {
        var clientKey = request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (!rateGuard.TryAcquire(clientKey))
        {
            request.HttpContext.Response.Headers.RetryAfter = rateGuard.RetryAfterSeconds(clientKey).ToString();
            return new StatusCodeResult(StatusCodes.Status429TooManyRequests);
        }

        LoginRequest? login;
        try
        {
            login = await JsonSerializer.DeserializeAsync<LoginRequest>(request.Body, JsonOptions, cancellationToken);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Invalid login request body.");
            return new BadRequestObjectResult(new { error = "invalid_request" });
        }

        var result = auth.Login(login?.Code);
        return result.Success
            ? new OkObjectResult(result)
            : new ObjectResult(new { error = "invalid_code" }) { StatusCode = StatusCodes.Status401Unauthorized };
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
