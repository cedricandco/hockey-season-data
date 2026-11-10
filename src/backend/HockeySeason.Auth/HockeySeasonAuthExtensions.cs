using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsync(
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
}
