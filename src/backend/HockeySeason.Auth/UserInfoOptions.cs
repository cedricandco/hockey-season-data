using System.ComponentModel.DataAnnotations;

namespace HockeySeason.Auth;

/// <summary>
/// Settings of the identity provider's userinfo endpoint. The provider host
/// (e.g. <c>https://dev-xxxx.us.auth0.com</c>) lives in configuration — the
/// code never hardcodes a tenant — and defaults to the authority, so only
/// non-standard deployments need an explicit override.
/// </summary>
public sealed class UserInfoOptions
{
    public const string SectionName = "UserInfo";

    /// <summary>Base address of the identity provider, e.g. <c>https://dev-xxxx.us.auth0.com/</c>.</summary>
    [Required, MinLength(8)]
    public string BaseAddress { get; set; } = string.Empty;

    /// <summary>Relative path of the OIDC userinfo endpoint on that base address.</summary>
    [Required]
    public string Path { get; set; } = "userinfo";

    /// <summary>Timeout of a userinfo call.</summary>
    public int TimeoutSeconds { get; set; } = 10;
}
