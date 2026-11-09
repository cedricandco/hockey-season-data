using System.ComponentModel.DataAnnotations;

namespace HockeySeason.Auth;

/// <summary>
/// Configuration for the OIDC-style JWT security layer.
/// When no real identity provider (Entra ID, Auth0, …) exists yet, the login
/// endpoint validates a 6-digit PIN code from configuration and issues signed
/// JWT access tokens that every other endpoint accepts as Bearer credentials.
/// </summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>Scheme used by the login endpoint. Defaults to OIDC-style Bearer JWTs.</summary>
    [Required, MinLength(1)]
    public string SchemeName { get; set; } = "Bearer";

    /// <summary>
    /// Issuer (`iss`) embedded in issued tokens and required by validation.
    /// Must be set explicitly; a mismatched issuer makes every token invalid.
    /// </summary>
    [Required, MinLength(8)]
    public string Issuer { get; set; } = string.Empty;

    /// <summary>
    /// Audience (`aud`) embedded in issued tokens and required by validation.
    /// Must be set explicitly; a mismatched audience makes every token invalid.
    /// </summary>
    [Required, MinLength(8)]
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// Symmetric signing key (HS256); at least 32 characters (256 bits).
    /// Comes from user secrets / environment variables, never from source.
    /// </summary>
    [Required, MinLength(32)]
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>Shared 6-digit access code. Replaces a real user database for now.</summary>
    [Required, RegularExpression(@"^\d{6}$", ErrorMessage = "PinCode must be exactly 6 digits.")]
    public string PinCode { get; set; } = string.Empty;

    /// <summary>Access-token lifetime in minutes.</summary>
    [Range(1, 1440)]
    public int AccessTokenMinutes { get; set; } = 60;
}
