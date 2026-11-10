using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace HockeySeason.Auth;

/// <summary>
/// OIDC bearer-token validation, independent from the ASP.NET Core
/// authentication middleware. Used by the Azure Functions host, where the
/// ASP.NET Core integration cannot always be relied on to run
/// <c>UseAuthentication</c> before trigger code (behavior differs between
/// the local func CLI and the Static Web Apps managed host).
/// </summary>
public interface IJwtValidator
{
    /// <summary>
    /// Validates the raw JWT against the authority issuer, audience and
    /// lifetime using keys from JWKS discovery; returns the validated
    /// <c>sub</c> claim (the Auth0 user id) on success.
    /// </summary>
    bool TryValidate(string? token, out string? subject);
}

/// <summary>
/// Document retriever allowing http:// authorities. Production authorities
/// are https; tests against an in-memory provider run over http. The
/// JwtBearer middleware achieves the same through <c>RequireHttpsMetadata</c>.
/// </summary>
internal sealed class PermissiveHttpDocumentRetriever : IDocumentRetriever
{
    private readonly HttpClient _httpClient = new();

    public async Task<string> GetDocumentAsync(string address, CancellationToken cancel)
    {
        using var response = await _httpClient.GetAsync(address, cancel);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancel);
    }
}

public sealed class JwtValidator : IJwtValidator
{
    private static readonly TimeSpan MetadataRefreshInterval = TimeSpan.FromMinutes(5);

    private readonly TokenValidationParameters _parameters;
    private readonly IConfigurationManager<OpenIdConnectConfiguration> _configurationManager;
    private readonly JwtSecurityTokenHandler _handler = new();
    private readonly string[] _validTypes = ["JWT"];

    public JwtValidator(IOptions<AuthOptions> options)
    {
        _parameters = HockeySeasonAuthExtensions.CreateValidationParameters(options.Value);
        // Retriever mirroring the JwtBearer middleware: discovery metadata and
        // signing keys are fetched from the authority and refreshed
        // periodically instead of being pinned at startup.
        _configurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            $"{options.Value.Authority.TrimEnd('/')}/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever(),
            options.Value.AllowHttpAuthority
                ? new PermissiveHttpDocumentRetriever()
                : new HttpDocumentRetriever())
        {
            AutomaticRefreshInterval = MetadataRefreshInterval,
            RefreshInterval = MetadataRefreshInterval,
        };
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
            var configuration = _configurationManager.GetConfigurationAsync(CancellationToken.None).GetAwaiter().GetResult();
            var parameters = _parameters.Clone();
            parameters.IssuerSigningKeys = configuration.SigningKeys;

            var principal = _handler.ValidateToken(token, parameters, out _);
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
