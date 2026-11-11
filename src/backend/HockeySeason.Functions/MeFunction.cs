using HockeySeason.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace HockeySeason.Functions;

/// <summary>
/// Returns the authenticated caller's identity-provider profile (sub,
/// nickname, name, picture, email...). The SPA renders the avatar from
/// <c>picture</c>; the token is read from <c>X-Authorization</c> when the
/// standard header is absent, matching <see cref="FunctionsAuthorization"/>.
/// </summary>
public sealed class MeFunction(IUserContext userContext)
{
    [Function("Me")]
    public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "me")] HttpRequest request, FunctionContext context, CancellationToken cancellationToken)
    {
        var authorization = FunctionsAuthorization.IsAuthenticatedV2(context);
        if (!authorization.IsAuthenticated)
        {
            return FunctionsAuthorization.UnauthorizedResult(authorization.Reason);
        }

        var user = await userContext.GetCurrentUserAsync(cancellationToken);
        return user is null
            ? new ObjectResult(new { error = "userinfo_unavailable", error_description = "The identity provider profile could not be resolved." })
            {
                StatusCode = StatusCodes.Status502BadGateway,
            }
            : new OkObjectResult(user);
    }
}
