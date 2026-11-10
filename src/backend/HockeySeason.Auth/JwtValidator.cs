using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace HockeySeason.Auth;

/// <summary>
/// Direct HS256 bearer-token validation, independent from ASP.NET Core
/// authentication middleware. Used by the Azure Functions host, where the
/// ASP.NET Core integration cannot always be relied on to run
/// <c>UseAuthentication</c> before trigger code (behavior differs between
/// the local func CLI and the Static Web Apps managed host).
/// </summary>
public interface IJwtValidator
{
    /// <summary>
    /// Validates the raw JWT against issuer, audience, lifetime and the
    /// HS256 signing key; returns the validated <c>sub</c> claim on success.
    /// </summary>
    bool TryValidate(string? token, out string? subject);
}

public sealed class JwtValidator : IJwtValidator
{
    private readonly TokenValidationParameters _parameters;
    private readonly JwtSecurityTokenHandler _handler = new();
    private readonly string[] _validTypes = ["JWT"];

    public JwtValidator(IOptions<AuthOptions> options)
    {
        _parameters = HockeySeasonAuthExtensions.CreateValidationParameters(options.Value);
    }

    public bool TryValidate(string? token, out string? subject)
    {
        subject = null;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        try
        {
            var principal = _handler.ValidateToken(token, _parameters, out _);
            if (principal.Identity?.IsAuthenticated != true)
            {
                return false;
            }
            var tokenType = principal.FindFirst("typ")?.Value;
            if (tokenType is not null && !_validTypes.Contains(tokenType, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }
            subject = principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            return subject is not null;
        }
        catch (SecurityTokenException)
        {
            return false;
        }
    }
}
