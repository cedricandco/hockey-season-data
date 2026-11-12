using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using HockeySeason.Application;
using Microsoft.Azure.Functions.Worker;

namespace HockeySeason.Functions;

/// <summary>
/// Resolves the local user of the current invocation: the caller's JWT claims
/// are provisioned (idempotent, the first authenticated request creates the
/// row) and the local id is returned. One instance per invocation (scoped), so
/// the id is cached for the duration of the request.
/// </summary>
public sealed class RequestUserResolver(IUserProvisioningService provisioning)
{
    private int? _userId;

    /// <summary>
    /// Admin flag: the Auth0 claim <c>roles</c> containing <c>admin</c> (defense
    /// in depth with the <c>users.is_admin</c> column read from the database).
    /// The JWT handler maps the inbound <c>roles</c> claim to
    /// <see cref="ClaimTypes.Role"/>, so both spellings are accepted.
    /// </summary>
    public static bool IsAdmin(FunctionContext context)
    {
        var principal = FunctionsAuthorization.GetPrincipal(context);
        return principal is not null
            && (principal.HasClaim("roles", "admin") || principal.HasClaim(ClaimTypes.Role, "admin"));
    }

    public async Task<int> GetOrCreateUserIdAsync(FunctionContext context, CancellationToken cancellationToken)
    {
        if (_userId.HasValue)
        {
            return _userId.Value;
        }

        var principal = FunctionsAuthorization.GetPrincipal(context)
            ?? throw new InvalidOperationException("The invocation carries no authenticated principal.");
        var result = await provisioning.ProvisionAsync(ReadClaims(principal), cancellationToken);
        _userId = result.UserId;
        return result.UserId;
    }

    private static ApplicationUserClaims ReadClaims(ClaimsPrincipal user)
    {
        var subject = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? throw new InvalidOperationException("The authenticated principal carries no 'sub' claim.");
        return new ApplicationUserClaims(
            subject,
            user.FindFirstValue(JwtRegisteredClaimNames.Email) ?? user.FindFirstValue(ClaimTypes.Email),
            user.FindFirstValue(JwtRegisteredClaimNames.GivenName) ?? user.FindFirstValue(ClaimTypes.GivenName),
            user.FindFirstValue(JwtRegisteredClaimNames.FamilyName) ?? user.FindFirstValue(ClaimTypes.Surname));
    }
}
