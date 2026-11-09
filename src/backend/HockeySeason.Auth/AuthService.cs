using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace HockeySeason.Auth;

/// <summary>
/// Validates login attempts against the 6-digit access code stored in
/// configuration (user secrets / environment), then delegates token creation
/// to <see cref="ITokenService"/>.
/// </summary>
public interface IAuthService
{
    /// <summary>Issues a signed JWT when <paramref name="code"/> matches the configured PIN.</summary>
    LoginResult Login(string? code);

    /// <summary>Exposes pin-derived entropy so callers can stamp issued tokens.</summary>
    string SecurityStamp { get; }
}

public sealed class AuthService : IAuthService
{
    private readonly AuthOptions _options;
    private readonly ITokenService _tokens;
    private readonly byte[] _pinHash;

    public AuthService(IOptions<AuthOptions> options, ITokenService tokens)
    {
        _options = options.Value;
        _tokens = tokens;
        // A pin-derived hash keeps the raw code out of object graphs that end up
        // in dumps/logs, while giving CryptographicOperations a stable buffer.
        _pinHash = SHA256.HashData(Encoding.UTF8.GetBytes(_options.PinCode));
    }

    public string SecurityStamp =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_options.PinCode + AuthConstants.CodeAuthenticationMethod)))[..32];

    public LoginResult Login(string? code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length != 6)
        {
            return LoginResult.Failed();
        }

        var candidate = SHA256.HashData(Encoding.UTF8.GetBytes(code));
        if (!CryptographicOperations.FixedTimeEquals(candidate, _pinHash))
        {
            return LoginResult.Failed();
        }

        var token = _tokens.CreateAccessToken(SecurityStamp);
        return new LoginResult(
            Success: true,
            AccessToken: token,
            ExpiresInSeconds: (long)_options.AccessTokenMinutes * 60,
            TokenType: "Bearer");
    }
}
