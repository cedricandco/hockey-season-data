using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace HockeySeason.Auth;

/// <summary>
/// Registers the OIDC-style JWT security layer: options binding/validation,
/// HS256 token issuance, JwtBearer authentication and authorization policies.
/// Every protected endpoint requires a Bearer token created by
/// <c>POST /api/auth/login</c> with the shared 6-digit access code.
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

        services.AddSingleton<ITokenService, TokenService>();
        services.AddSingleton<IAuthService, AuthService>();
        services.AddSingleton<IJwtValidator, JwtValidator>();
        services.AddSingleton<LoginRateGuard>();

        var authOptions = configuration
            .GetSection(AuthOptions.SectionName)
            .Get<AuthOptions>() ?? new AuthOptions();

        services
            .AddAuthorizationBuilder()
            .AddPolicy(AuthenticatedPolicy, policy => policy.RequireAuthenticatedUser());

        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = CreateValidationParameters(authOptions);
                // 401 responses instead of redirecting to an OIDC authority page.
                options.Events = new JwtBearerEvents
                {
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsync(
                            """{"error":"invalid_token","error_description":"Missing or invalid bearer token"}""",
                            Encoding.UTF8);
                    },
                };
            });

        return services;
    }

    /// <summary>
    /// Wires the auth login endpoint. Call after
    /// <see cref="AddHockeySeasonAuth"/>; brute-force limiting is handled
    /// inside the endpoint so it works identically in the minimal-API host
    /// and Azure Functions.
    /// </summary>
    public static IEndpointConventionBuilder MapHockeySeasonLogin(
        this IEndpointRouteBuilder endpoints,
        LoginRateGuard rateGuard)
    {
        return endpoints.MapLoginEndpoint(rateGuard);
    }

    internal static TokenValidationParameters CreateValidationParameters(AuthOptions options) => new()
    {
        ValidateIssuer = true,
        ValidIssuer = options.Issuer,
        ValidateAudience = true,
        ValidAudience = options.Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
        ValidateLifetime = true,
        RequireExpirationTime = true,
        RequireSignedTokens = true,
        ClockSkew = TimeSpan.FromMinutes(1),
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        ValidTypes = ["JWT"],
    };
}
