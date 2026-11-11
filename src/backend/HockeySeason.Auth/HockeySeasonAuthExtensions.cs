using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace HockeySeason.Auth;

/// <summary>
/// JWT bearer security backed by an OIDC identity provider (Auth0): the API
/// validates RS256 access tokens against the provider authority metadata —
/// issuer, audience and signing keys come from JWKS discovery, no signing
/// secret lives in this codebase. Token issuance and the login UI belong to
/// the identity provider (Universal Login).
/// </summary>
public static class HockeySeasonAuthExtensions
{
    /// <summary>Authorization policy applied to all business endpoints.</summary>
    public const string AuthenticatedPolicy = "Authenticated";

    public static IServiceCollection AddHockeySeasonAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<AuthOptions>()
            .Bind(configuration.GetSection(AuthOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IJwtValidator, JwtValidator>();
        // JwtValidator takes an optional ILoggerFactory that the host builders
        // (AddLogging) provide automatically; bare test providers resolve null.

        var authOptions = configuration
            .GetSection(AuthOptions.SectionName)
            .Get<AuthOptions>() ?? new AuthOptions();

        services
            .AddAuthorizationBuilder()
            .AddPolicy(AuthenticatedPolicy, policy => policy.RequireAuthenticatedUser());
        // Endpoints are secured by default: the fallback policy applies to
        // every endpoint without explicit authorization metadata, leaving
        // only [AllowAnonymous] probes (health checks) open.
        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = options.GetPolicy(AuthenticatedPolicy)
                ?? new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
        });

        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.Authority = authOptions.Authority;
                options.RequireHttpsMetadata = !authOptions.AllowHttpAuthority;
                options.MapInboundClaims = false;
                options.TokenValidationParameters = CreateValidationParameters(authOptions);
                // 401 JSON responses instead of redirecting to the authority page.
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        ResolveLogger(context.HttpContext)?.LogInformation(
                            "JwtBearer[MessageReceived] scheme={Scheme} hasAuthHeader={HasAuthHeader} value={HeaderValue}",
                            context.Scheme.Name,
                            context.Request.Headers.ContainsKey("Authorization"),
                            DescribeAuthorizationHeader(context.Request));
                        return Task.CompletedTask;
                    },
                    OnTokenValidated = context =>
                    {
                        ResolveLogger(context.HttpContext)?.LogInformation(
                            "JwtBearer[TokenValidated] scheme={Scheme} token={Token} sub={Sub}",
                            context.Scheme.Name,
                            DescribeToken(context.SecurityToken),
                            context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                                ?? context.Principal?.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value);
                        return Task.CompletedTask;
                    },
                    OnAuthenticationFailed = context =>
                    {
                        // The exception carries the definitive IDXxxxx failure
                        // (expired, wrong audience, bad signature...); log it
                        // in full because the 401 response stays generic.
                        ResolveLogger(context.HttpContext)?.LogError(
                            context.Exception,
                            "JwtBearer[AuthenticationFailed] scheme={Scheme}",
                            context.Scheme.Name);
                        return Task.CompletedTask;
                    },
                    OnChallenge = context =>
                    {
                        // AuthenticateFailure is null only for the no-token case.
                        ResolveLogger(context.HttpContext)?.LogWarning(
                            "JwtBearer[Challenge] scheme={Scheme} tokenPresent={TokenPresent} error={Error} errorDescription={ErrorDescription} authenticateFailure={AuthenticateFailure}",
                            context.Scheme.Name,
                            context.Request.Headers.ContainsKey("Authorization"),
                            context.Error ?? "<none>",
                            context.ErrorDescription ?? "<none>",
                            context.AuthenticateFailure?.GetType().Name ?? "<no failure: no token>");
                        context.HandleResponse();
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        context.Response.ContentType = "application/json";
                        return context.Response.WriteAsync(
                            """{"error":"invalid_token","error_description":"Missing or invalid bearer token"}""",
                            System.Text.Encoding.UTF8);
                    },
                };
            });

        return services;
    }

    /// <summary>
    /// Validation parameters keyed to the provider configuration: issuer and
    /// audience must match, signing keys come from JWKS discovery (RS256
    /// only, asymmetric — no shared secret is configured here).
    /// </summary>
    internal static TokenValidationParameters CreateValidationParameters(AuthOptions options) => new()
    {
        ValidateIssuer = true,
        ValidIssuer = options.Issuer,
        ValidateAudience = true,
        ValidAudience = options.Audience,
        ValidateIssuerSigningKey = true,
        RequireSignedTokens = true,
        ValidateLifetime = true,
        RequireExpirationTime = true,
        ClockSkew = TimeSpan.FromMinutes(1),
        ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
        ValidTypes = ["JWT"],
    };

    private static ILogger? ResolveLogger(HttpContext context) =>
        context.RequestServices.GetService(typeof(ILogger<JwtBearerHandler>)) as ILogger;

    /// <summary>
    /// Describes the Authorization header without ever logging the raw token:
    /// scheme shape only (the token body holds no value for debugging here).
    /// </summary>
    internal static string DescribeAuthorizationHeader(HttpRequest request)
    {
        var value = request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(value))
        {
            return "<absent>";
        }
        var separator = value.IndexOf(' ');
        return separator > 0
            ? $"scheme={value[..separator]} length={value[(separator + 1)..].Trim().Length}"
            : $"no-space (malformed, length={value.Length})";
    }

    /// <summary>
    /// Safe identity of a token for correlation across log lines: signing
    /// key id and issuer. Never emits claims or the signature.
    /// </summary>
    internal static string DescribeToken(Microsoft.IdentityModel.Tokens.SecurityToken? token) => token is null
        ? "<unparsed>"
        : $"kid={GetKeyId(token)} iss={token.Issuer}";

    private static string GetKeyId(Microsoft.IdentityModel.Tokens.SecurityToken token)
    {
        if (token is JwtSecurityToken jwt)
        {
            var kid = jwt.Header.Kid;
            return kid ?? token.SigningKey?.ToString() ?? "<none>";
        }
        return token.SigningKey?.ToString() ?? "<none>";
    }
}
