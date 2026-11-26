using System.Text.Json.Serialization;

namespace HockeySeason.Auth;

/// <summary>
/// OIDC standard claims returned by the identity provider's
/// <c>/userinfo</c> endpoint. "sub" is the stable user identifier; the
/// remaining fields are profile claims the provider publishes for this user.
/// </summary>
/// <param name="Sub">Stable subject identifier, e.g. <c>auth0|6af39e...</c>.</param>
/// <param name="Nickname">User-chosen handle shown in compact UIs.</param>
/// <param name="Name">Display name (Auth0 defaults to the email).</param>
/// <param name="Picture">Absolute URL of the avatar image.</param>
/// <param name="UpdatedAt">Last profile update timestamp; a Unix-seconds number on
/// the wire (<c>/userinfo</c>) or an RFC 3339 string in a token, normalized to
/// RFC 3339 by <see cref="OidcTimestampConverter"/>.</param>
/// <param name="Email">Published email address (only with the email scope).</param>
/// <param name="EmailVerified">Whether the provider verified the address.</param>
/// <param name="GivenName">First name, when the provider publishes it (profile scope).</param>
/// <param name="FamilyName">Last name, when the provider publishes it (profile scope).</param>
public sealed record UserInfo(
    [property: JsonPropertyName("sub")] string Sub,
    [property: JsonPropertyName("nickname")] string? Nickname,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("picture")] string? Picture,
    [property: JsonPropertyName("updated_at")][property: JsonConverter(typeof(OidcTimestampConverter))] string? UpdatedAt,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("email_verified")] bool? EmailVerified,
    [property: JsonPropertyName("given_name")] string? GivenName = null,
    [property: JsonPropertyName("family_name")] string? FamilyName = null);
