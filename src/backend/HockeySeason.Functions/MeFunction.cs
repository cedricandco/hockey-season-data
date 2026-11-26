using HockeySeason.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace HockeySeason.Functions;

/// <summary>
/// Returns the authenticated caller's identity-provider profile (sub,
/// nickname, name, picture, email...). The SPA renders the avatar from
/// <c>picture</c>; the token is read from <c>X-Authorization</c> when the
/// standard header is absent, matching <see cref="FunctionsAuthorization"/>.
/// </summary>
public sealed class MeFunction(IUserContext userContext, ILogger<MeFunction> logger)
{
    [Function("Me")]
    public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "me")] HttpRequest request, FunctionContext context, CancellationToken cancellationToken)
    {
        var authorization = FunctionsAuthorization.IsAuthenticatedV2(context);
        if (!authorization.IsAuthenticated)
        {
            return FunctionsAuthorization.UnauthorizedResult(authorization.Reason);
        }

        // The token comes from the invocation's own HttpContext: the isolated
        // worker registers no IHttpContextAccessor, so an ambient accessor
        // would silently resolve nothing and turn every call into a 502.
        var result = await userContext.TryGetUserAsync(ReadBearerToken(request), cancellationToken);
        if (result.Profile is { } profile)
        {
            return new OkObjectResult(profile);
        }

        // The client answer stays generic; the reason is what makes the
        // failure diagnosable from the logs.
        logger.LogWarning(
            "Me could not resolve the provider profile: reason={Reason}, invocationId={InvocationId}.",
            result.Failure,
            context.InvocationId);
        return new ObjectResult(new { error = "userinfo_unavailable", error_description = "The identity provider profile could not be resolved." })
        {
            StatusCode = StatusCodes.Status502BadGateway,
        };
    }

    /// <summary>Bearer token of the request, standard header first then the SPA's mirror.</summary>
    private static string? ReadBearerToken(HttpRequest request) =>
        BearerTokenParser.TryExtractBearerToken(request.Headers.Authorization.ToString(), out var token)
            || BearerTokenParser.TryExtractBearerToken(
                request.Headers[UserContextExtensions.MirroredAuthorizationHeader].ToString(), out token)
            ? token
            : null;
}
