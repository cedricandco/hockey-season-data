using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using HockeySeason.Application;
using HockeySeason.Auth;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Options;

namespace HockeySeason.Functions;

/// <summary>
/// Resolves the local user of the current invocation: the caller's JWT claims
/// are provisioned (idempotent, the first authenticated request creates the
/// row) and the local id is returned. One instance per invocation (scoped), so
/// the id is cached for the duration of the request.
///
/// The profile (email, first and last name) is read from the access token
/// itself: an Auth0 Action copies those claims onto the token, which needs no
/// extra HTTP round trip inside the trigger and works on the Static Web Apps
/// managed host where the request headers are not always visible through
/// <c>IHttpContextAccessor</c>. The userinfo call stays as a fallback for
/// tenants where the Action is not configured.
/// </summary>
public sealed class RequestUserResolver(
    IUserProvisioningService provisioning,
    IUserContext? userContext = null,
    IOptions<AuthOptions>? authOptions = null)
{
    private readonly AuthOptions _authOptions = authOptions?.Value ?? new AuthOptions();
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
        var claims = ReadClaims(principal, _authOptions.ProfileClaimsNamespace);
        var result = await provisioning.ProvisionAsync(claims, cancellationToken);

        // The token carried no usable profile (the Auth0 Action that copies
        // email / given_name / family_name onto the access token is not
        // configured, or the row is still missing a field): fall back to the
        // provider's userinfo profile and store what it publishes. Once the
        // row is complete and the token carries the claims, this never runs.
        if (result.ProfileIncomplete && userContext is not null)
        {
            var completed = await CompleteFromUserInfoAsync(claims, context, cancellationToken);
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
        ApplicationUserClaims claims, FunctionContext context, CancellationToken cancellationToken)
    {
        UserInfo? profile;
        try
        {
            // The token is read from the invocation's HttpContext: the isolated
            // worker registers no IHttpContextAccessor, so the ambient accessor
            // inside IUserContext resolves nothing there.
            profile = await userContext!.GetUserAsync(ReadBearerToken(context), cancellationToken);
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

    /// <summary>
    /// Bearer token of the invocation: the standard header first, then the
    /// mirror the SPA sets for proxies that strip it. Read from the invocation's
    /// HttpContext, the source that works on every host.
    /// </summary>
    private static string? ReadBearerToken(FunctionContext context)
    {
        var httpContext = context.GetHttpContext();
        if (httpContext is null)
        {
            return null;
        }
        var headers = httpContext.Request.Headers;
        return BearerTokenParser.TryExtractBearerToken(headers.Authorization.ToString(), out var token)
            || BearerTokenParser.TryExtractBearerToken(
                headers[UserContextExtensions.MirroredAuthorizationHeader].ToString(), out token)
            ? token
            : null;
    }

    private static ApplicationUserClaims ReadClaims(ClaimsPrincipal user, string? claimNamespace)
    {
        var subject = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? throw new InvalidOperationException("The authenticated principal carries no 'sub' claim.");
        var (email, givenName, familyName) = ProfileClaims.Read(user, claimNamespace);
        return new ApplicationUserClaims(subject, email, givenName, familyName);
    }
}
