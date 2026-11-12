using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
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

    /// <summary>
    /// Same validation as <see cref="TryValidate"/>, additionally returning a
    /// machine-readable failure reason (null when the token is valid).
    /// </summary>
    (bool IsValid, string? Reason) TryValidateV2(string? token, out string? subject);

    /// <summary>
    /// Same validation, returning the validated <see cref="ClaimsPrincipal"/>
    /// (sub, email, names, roles) so callers can provision the local account.
    /// </summary>
    JwtValidationResult Validate(string? token);
}

/// <summary>Outcome of <see cref="IJwtValidator.Validate"/>.</summary>
public sealed record JwtValidationResult(bool IsValid, string? Reason, string? Subject, ClaimsPrincipal? Principal);

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
    private readonly ILoggerFactory? _loggerFactory;

    public JwtValidator(IOptions<AuthOptions> options, ILoggerFactory? loggerFactory = null)
    {
        _loggerFactory = loggerFactory;
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

    public bool TryValidate(string? token, out string? subject) => TryValidateV2(token, out subject).IsValid;

    public (bool IsValid, string? Reason) TryValidateV2(string? token, out string? subject)
    {
        var result = Validate(token);
        subject = result.Subject;
        return (result.IsValid, result.Reason);
    }

    public JwtValidationResult Validate(string? token)
    {
        var logger = _loggerFactory?.CreateLogger<JwtValidator>();
        logger?.LogDebug("Starting JWT validation");

        string? subject;
        if (string.IsNullOrWhiteSpace(token))
        {
            logger?.LogWarning("JwtValidator rejected: empty_token");
            return new(false, "empty_token", null, null);
        }

        try
        {
            var configuration = _configurationManager.GetConfigurationAsync(CancellationToken.None).GetAwaiter().GetResult();
            logger?.LogDebug(
                "JwtValidator discovery: authority jwks has {KeyCount} signing keys (kid: {KeyIds})",
                configuration.SigningKeys.Count,
                string.Join(", ", configuration.SigningKeys.Select(k => k.KeyId ?? "<none>")));
            var parameters = _parameters.Clone();
            parameters.IssuerSigningKeys = configuration.SigningKeys;

            var principal = _handler.ValidateToken(token, parameters, out SecurityToken validatedToken);
            if (principal.Identity?.IsAuthenticated != true)
            {
                logger?.LogWarning("JwtValidator rejected: not_authenticated");
                return new(false, "not_authenticated", null, null);
            }
            var tokenType = principal.FindFirst("typ")?.Value;
            if (tokenType is not null && !_validTypes.Contains(tokenType, StringComparer.OrdinalIgnoreCase))
            {
                logger?.LogWarning("JwtValidator rejected: invalid_token_type typ={TokenType}", tokenType);
                return new(false, "invalid_token_type", null, null);
            }
            subject = principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            if (subject is not null)
            {
                logger?.LogInformation(
                    "JwtValidator accepted: sub={Subject} iss={Issuer} kid={KeyId}",
                    subject,
                    validatedToken.Issuer,
                    HockeySeasonAuthExtensions.DescribeToken(validatedToken));
                return new(true, null, subject, principal);
            }
            logger?.LogWarning("JwtValidator rejected: no_subject");
            return new(false, "no_subject", null, null);
        }
        catch (Exception ex)
        {
            // Full detail (IDX code, expected vs actual issuer/audience/signing
            // key) goes to the log; the response body only carries the short
            // machine-readable reason. The token header is also logged —
            // without leaking its material — because "signature key not found"
            // is usually caused by tokens from a different issuer/algorithm
            // (e.g. kid-less HS256 tokens) rather than an actual JWKS outage.
            var reason = DescribeFailure(ex);
            LogTokenHeader(token, logger);
            if (reason is not null)
            {
                logger?.LogWarning(ex, "JwtValidator rejected: {Reason}", reason);
                return new(false, reason, null, null);
            }
            logger?.LogError(ex, "JwtValidator rejected: unexpected_failure");
            return new(false, "invalid_token", null, null);
        }
    }

    private void LogTokenHeader(string? token, ILogger? logger)
    {
        if (logger is null || string.IsNullOrWhiteSpace(token))
        {
            return;
        }
        try
        {
            var header = _handler.ReadJwtToken(token).Header;
            logger?.LogWarning(
                "JwtValidator rejected token header: alg={Alg} kidPresent={KidPresent} kid={Kid} type={Type}",
                header.Alg,
                header.Kid is not null,
                header.Kid ?? "<none>",
                header.Typ ?? "<none>");
        }
        catch (ArgumentException)
        {
            logger?.LogWarning("JwtValidator rejected token header: unparsable length={Length}", token.Length);
        }
    }

    private static string? DescribeFailure(Exception exception) => exception switch
    {
        SecurityTokenSignatureKeyNotFoundException => "unknown_signing_key",
        SecurityTokenExpiredException => "token_expired",
        SecurityTokenNotYetValidException => "token_not_yet_valid",
        SecurityTokenInvalidIssuerException => "invalid_issuer",
        SecurityTokenInvalidAudienceException => "invalid_audience",
        SecurityTokenInvalidLifetimeException => "invalid_lifetime",
        SecurityTokenInvalidSigningKeyException => "invalid_signing_key",
        SecurityTokenInvalidAlgorithmException => "invalid_algorithm",
        SecurityTokenException => "invalid_token",
        _ => null,
    };
}
