using System.ComponentModel.DataAnnotations;

namespace HockeySeason.Auth;

/// <summary>
/// Configuration for the JWT security layer backed by Auth0 (or any OIDC
/// provider): the API only validates access tokens issued for its audience
/// by the configured authority — it never issues tokens itself.
/// </summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>
    /// OIDC authority base URL, e.g. <c>https://dev-xyz.us.auth0.com/</c>.
    /// Discovery metadata (<c>/.well-known/openid-configuration</c>) and JWKS
    /// are resolved from this URL by the JwtBearer middleware.
    /// </summary>
    [Required, MinLength(8)]
    public string Authority { get; set; } = string.Empty;

    /// <summary>
    /// Issuer (`iss`) embedded in tokens by the provider and required by
    /// validation. Defaults to the authority URL with a trailing slash
    /// (Auth0 convention); set it explicitly when the provider differs.
    /// </summary>
    [Required, MinLength(8)]
    public string Issuer { get; set; } = string.Empty;

    /// <summary>
    /// Audience (`aud`) of this API as registered in the identity provider —
    /// the unique identifier of the Auth0 "API" (resource server).
    /// </summary>
    [Required, MinLength(8)]
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// Development escape hatch allowing an http:// authority (no HTTPS
    /// metadata). Never enable in production.
    /// </summary>
    public bool AllowHttpAuthority { get; set; }
}
