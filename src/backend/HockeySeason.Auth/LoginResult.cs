namespace HockeySeason.Auth;

/// <summary>Result of a login attempt.</summary>
/// <param name="Success">Whether the access code was accepted.</param>
/// <param name="AccessToken">The signed JWT when <see cref="Success"/> is true.</param>
/// <param name="ExpiresInSeconds">Token lifetime in seconds (0 when the login failed).</param>
/// <param name="TokenType">Always "Bearer" (OIDC-style) on success.</param>
public sealed record LoginResult(
    bool Success,
    string AccessToken,
    long ExpiresInSeconds,
    string TokenType)
{
    public static LoginResult Failed() => new(false, string.Empty, 0, string.Empty);
}
