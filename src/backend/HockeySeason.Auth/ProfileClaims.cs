using System.Security.Claims;

namespace HockeySeason.Auth;

/// <summary>
/// Reads the OIDC profile claims (<c>email</c>, <c>given_name</c>,
/// <c>family_name</c>) off a validated access token.
///
/// Auth0 access tokens only publish these claims when an Action copies them
/// onto the token, and a non-standard claim added to a token issued for a
/// custom API must be namespaced. The reader therefore accepts, per field:
/// the namespaced claim first, then the standard (non-namespaced) OIDC claim,
///
/// and finally the claim name .NET maps it to (the JWT handler remaps
/// <c>email</c>/<c>given_name</c>/<c>family_name</c> to
/// <see cref="ClaimTypes"/> constants).
/// </summary>
public static class ProfileClaims
{
    /// <summary>Namespaced claim of the email address, e.g. <c>https://tenant/email</c>.</summary>
    public const string EmailKey = "email";

    /// <summary>Namespaced claim of the first name, e.g. <c>https://tenant/given_name</c>.</summary>
    public const string GivenNameKey = "given_name";

    /// <summary>Namespaced claim of the last name, e.g. <c>https://tenant/family_name</c>.</summary>
    public const string FamilyNameKey = "family_name";

    /// <summary>
    /// Extracts the three profile claims; a field is <c>null</c> when the token
    /// carries no usable value for it.
    /// </summary>
    /// <param name="principal">Validated principal of the caller.</param>
    /// <param name="claimNamespace">
    /// Namespace prefixed to the claim names (<c>https://tenant/</c>), or
    /// <c>null</c>/empty when the Action writes the standard OIDC claim names.
    /// </param>
    public static (string? Email, string? GivenName, string? FamilyName) Read(
        ClaimsPrincipal principal, string? claimNamespace = null)
    {
        ArgumentNullException.ThrowIfNull(principal);

        return (
            Find(principal, claimNamespace, EmailKey, ClaimTypes.Email),
            Find(principal, claimNamespace, GivenNameKey, ClaimTypes.GivenName),
            Find(principal, claimNamespace, FamilyNameKey, ClaimTypes.Surname));
    }

    /// <summary>Normalizes a configured namespace to the <c>https://host/</c> form used as a prefix.</summary>
    public static string? NormalizeNamespace(string? claimNamespace)
    {
        if (string.IsNullOrWhiteSpace(claimNamespace))
        {
            return null;
        }
        var trimmed = claimNamespace.Trim().TrimEnd('/');
        return trimmed.Length == 0 ? null : trimmed + "/";
    }

    private static string? Find(ClaimsPrincipal principal, string? claimNamespace, string claimName, string mappedName)
    {
        var namespaced = NormalizeNamespace(claimNamespace) is { } prefix ? prefix + claimName : null;

        foreach (var name in new[] { namespaced, claimName, mappedName })
        {
            if (name is null)
            {
                continue;
            }
            var value = principal.FindFirst(name)?.Value;
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }
        return null;
    }
}
