using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using HockeySeason.Application;
using HockeySeason.Auth;
using Microsoft.Azure.Functions.Worker;

namespace HockeySeason.Functions;

/// <summary>
/// Resolves the local user of the current invocation: the caller's JWT claims
/// are provisioned (idempotent, the first authenticated request creates the
/// row) and the local id is returned. One instance per invocation (scoped), so
/// the id is cached for the duration of the request.
///
/// The OIDC profile is completed from the provider's <c>/userinfo</c> endpoint
/// when the access token carries no email or name: Auth0 access tokens only
/// publish those claims when they were added to the API, so the userinfo
/// profile is what actually stores "nom, prénom" in the database.
/// </summary>
public sealed class RequestUserResolver(
    IUserProvisioningService provisioning,
    IUserContext? userContext = null)
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
        var claims = ReadClaims(principal);
        var result = await provisioning.ProvisionAsync(claims, cancellationToken);

        // The stored profile still misses a name or the email: complete the
        // claims from the provider profile and store them. Once the row is
        // complete this never calls userinfo again.
        if (result.ProfileIncomplete && userContext is not null)
        {
            var completed = await CompleteFromUserInfoAsync(claims, cancellationToken);
            if (completed is not null)
            {
                await provisioning.ProvisionAsync(completed, cancellationToken);
            }
        }

        _userId = result.UserId;
        return result.UserId;
    }

    /// <summary>
    /// Completes the claims with the provider's userinfo profile; null when the
    /// profile is unavailable or brings nothing new. Failures are ignored: the
    /// account keeps what the token said and the next request retries.
    /// </summary>
    private async Task<ApplicationUserClaims?> CompleteFromUserInfoAsync(
        ApplicationUserClaims claims, CancellationToken cancellationToken)
    {
        UserInfo? profile;
        try
        {
            profile = await userContext!.GetCurrentUserAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }

        if (profile is null)
        {
            return null;
        }

        var (firstName, lastName) = Names(profile);
        var completed = claims with
        {
            Email = claims.Email ?? EmptyToNull(profile.Email),
            FirstName = claims.FirstName ?? firstName,
            LastName = claims.LastName ?? lastName,
        };
        return completed == claims ? null : completed;
    }

    /// <summary>
    /// First and last name of a userinfo profile: the dedicated claims first,
    /// then the display name of a database (email/password) signup, which Auth0
    /// fills with "Prénom Nom". A display name that is the email is not split.
    /// </summary>
    private static (string? FirstName, string? LastName) Names(UserInfo profile)
    {
        var firstName = EmptyToNull(profile.GivenName);
        var lastName = EmptyToNull(profile.FamilyName);
        if (firstName is not null || lastName is not null)
        {
            return (firstName, lastName);
        }

        var displayName = EmptyToNull(profile.Name);
        if (displayName is null || displayName.Contains('@', StringComparison.Ordinal))
        {
            return (null, null);
        }

        var separator = displayName.IndexOf(' ', StringComparison.Ordinal);
        return separator < 0
            ? (displayName, null)
            : (displayName[..separator], EmptyToNull(displayName[(separator + 1)..]));
    }

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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
