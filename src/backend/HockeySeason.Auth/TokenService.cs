using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace HockeySeason.Auth;

/// <summary>
/// Issues HS256-signed JWT access tokens after validating the shared
/// 6-digit access code. Claims follow OIDC conventions (iss, sub, aud, iat,
/// exp) plus an amr claim marking the code-based flow, so the tokens stand in
/// for a real identity provider until user accounts exist.
/// </summary>
public interface ITokenService
{
    /// <summary>Creates a signed access token for the shared-code principal.</summary>
    string CreateAccessToken(string securityStamp);
}

public sealed class TokenService : ITokenService
{
    private readonly AuthOptions _options;

    public TokenService(IOptions<AuthOptions> options) => _options = options.Value;

    public string CreateAccessToken(string securityStamp)
    {
        var now = DateTimeOffset.UtcNow;
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, "pin-user"),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new("amr", AuthConstants.CodeAuthenticationMethod),
            new("security_stamp", securityStamp),
        };

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: now.AddMinutes(_options.AccessTokenMinutes).UtcDateTime,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
