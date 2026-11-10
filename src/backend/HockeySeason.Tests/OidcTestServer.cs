using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace HockeySeason.Tests;

/// <summary>
/// In-memory OIDC provider for tests: serves discovery metadata, JWKS and
/// issues RS256 access tokens signed with a generated RSA key, so auth
/// validation is exercised end-to-end without a network dependency.
/// </summary>
public sealed class OidcTestServer : IAsyncDisposable
{
    private readonly RsaSecurityKey _signingKey;
    private readonly WebApplication _app;

    public string Authority { get; private set; } = string.Empty;
    public string Audience { get; } = "https://api.hockeyseason.test";

    public OidcTestServer()
    {
        _signingKey = new RsaSecurityKey(RSA.Create(2048)) { KeyId = Guid.NewGuid().ToString("N") };
        var issuerPath = "/test-issuer/";

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _app = builder.Build();

        _app.MapGet($"{issuerPath}.well-known/openid-configuration", (HttpRequest request) =>
        {
            var issuer = GetIssuer(request);
            return Results.Json(new
            {
                issuer,
                jwks_uri = $"{issuer}jwks",
            });
        });

        _app.MapGet($"{issuerPath}jwks", () =>
        {
            var parameters = _signingKey.Rsa!.ExportParameters(false);
            return Results.Json(new
            {
                keys = new[]
                {
                    new
                    {
                        kty = "RSA",
                        use = "sig",
                        kid = _signingKey.KeyId,
                        alg = "RS256",
                        n = Base64UrlEncoder.Encode(parameters.Modulus!),
                        e = Base64UrlEncoder.Encode(parameters.Exponent!),
                    },
                },
            });
        });

        _app.StartAsync().GetAwaiter().GetResult();
        var addresses = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!;
        var baseAddress = addresses.Addresses.First().Replace("[::1]", "127.0.0.1").Replace("localhost", "127.0.0.1");
        Authority = $"{baseAddress.TrimEnd('/')}{issuerPath}";
    }

    public string Issuer => Authority;

    private static string GetIssuer(HttpRequest request)
    {
        var port = request.Host.Port ?? 80;
        return $"http://127.0.0.1:{port}/test-issuer/";
    }

    public string CreateToken(
        string subject = "auth0|test-user",
        string? audience = null,
        string? issuer = null,
        DateTimeOffset? expires = null,
        DateTime? notBefore = null,
        IEnumerable<Claim>? extraClaims = null)
    {
        var now = DateTime.UtcNow;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, subject),
            new(JwtRegisteredClaimNames.Sub, subject),
        };
        if (extraClaims is not null)
        {
            claims.AddRange(extraClaims);
        }

        var token = new JwtSecurityToken(
            issuer: issuer ?? Issuer,
            audience: audience ?? Audience,
            claims: claims,
            notBefore: notBefore ?? now.AddMinutes(-1),
            expires: (expires?.UtcDateTime ?? now.AddMinutes(60)),
            signingCredentials: new SigningCredentials(_signingKey, SecurityAlgorithms.RsaSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public ValueTask DisposeAsync()
    {
        _app.StopAsync().GetAwaiter().GetResult();
        _app.DisposeAsync().GetAwaiter().GetResult();
        return ValueTask.CompletedTask;
    }
}
