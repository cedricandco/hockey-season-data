using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using HockeySeason.Api;
using HockeySeason.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace HockeySeason.Tests;

/// <summary>
/// End-to-end coverage of the OIDC-style JWT security layer using the
/// composition root of HockeySeason.Api with an in-memory test configuration.
/// </summary>
public sealed class AuthEndpointsTests
{
    private const string PinCode = "654321";
    private const string SigningKey = "test-signing-key-that-is-long-enough-1234567890";
    private const string Issuer = "https://tests.hockey-season-data";
    private const string Audience = "hockey-season-api-tests";

    [Fact]
    public async Task Login_WithWrongCode_Returns401()
    {
        await using var app = CreateApp();
        var client = app.CreateClient(new() { BaseAddress = new Uri("https://localhost") });

        var response = await client.PostAsJsonAsync("/api/auth/login", new { code = "111111" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithShortCode_Returns401()
    {
        await using var app = CreateApp();
        var client = app.CreateClient(new() { BaseAddress = new Uri("https://localhost") });

        var response = await client.PostAsJsonAsync("/api/auth/login", new { code = "12" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithCorrectCode_ReturnsValidBearerTokenWithExpectedClaims()
    {
        await using var app = CreateApp();
        var client = app.CreateClient(new() { BaseAddress = new Uri("https://localhost") });

        var response = await client.PostAsJsonAsync("/api/auth/login", new { code = PinCode });

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<LoginResult>();
        Assert.NotNull(result);
        Assert.True(result!.Success);
        Assert.Equal("Bearer", result.TokenType);
        Assert.True(result.ExpiresInSeconds > 0);

        var handler = new JwtSecurityTokenHandler();
        Assert.True(handler.CanReadToken(result.AccessToken));
        var token = handler.ReadJwtToken(result.AccessToken);
        Assert.Equal(Issuer, token.Issuer);
        Assert.Contains(Audience, token.Audiences);
        Assert.Contains(token.Claims, c => c.Type == JwtRegisteredClaimNames.Sub && c.Value == "pin-user");
        Assert.Contains(token.Claims, c => c.Type == "amr" && c.Value == AuthConstants.CodeAuthenticationMethod);
        Assert.True(token.ValidTo > DateTime.UtcNow.AddMinutes(50));
    }

    [Fact]
    public async Task Login_BlocksMoreThanFiveAttemptsPerMinutePerIp()
    {
        await using var app = CreateApp();
        var client = app.CreateClient(new() { BaseAddress = new Uri("https://localhost") });

        for (var attempt = 0; attempt < LoginRateGuard.AttemptLimit; attempt++)
        {
            var blocked = await client.PostAsJsonAsync("/api/auth/login", new { code = "000000" });
            Assert.Equal(HttpStatusCode.Unauthorized, blocked.StatusCode);
        }

        var throttled = await client.PostAsJsonAsync("/api/auth/login", new { code = PinCode });
        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
        Assert.True(throttled.Headers.RetryAfter is not null);
    }

    [Fact]
    public async Task Login_AuthenticatedCallerStillRequiresCorrectCode()
    {
        await using var app = CreateApp();
        var client = await CreateAuthenticatedClientAsync(app);

        var withWrongCode = await client.PostAsJsonAsync("/api/auth/login", new { code = "000000" });
        Assert.Equal(HttpStatusCode.Unauthorized, withWrongCode.StatusCode);

        // A valid token alone never grants a fresh token: the correct code is
        // still mandatory (standard OIDC behavior for token-issuing endpoints).
        var withOldToken = await client.PostAsJsonAsync("/api/auth/login", new { code = PinCode });
        withOldToken.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task ProtectedEndpoint_WithoutToken_Returns401()
    {
        await using var app = CreateApp();
        var client = app.CreateClient(new() { BaseAddress = new Uri("https://localhost") });

        var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithToken_Returns200()
    {
        await using var app = CreateApp();
        var client = await CreateAuthenticatedClientAsync(app);

        var response = await client.GetAsync("/api/health");

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task ProtectedEndpoint_WithTamperedToken_Returns401()
    {
        await using var app = CreateApp();
        var client = await CreateAuthenticatedClientAsync(app);
        client.DefaultRequestHeaders.Authorization = new("Bearer", client.DefaultRequestHeaders.Authorization!.ToString() is { Length: > 3 } value ? value[..^2] + "xx" : "abc");

        var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithExpiredToken_Returns401()
    {
        await using var app = CreateApp();
        var expired = CreateTestToken(expiresInMinutes: -1);
        var client = app.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Authorization = new("Bearer", expired);

        var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithWrongAudience_Returns401()
    {
        await using var app = CreateApp();
        var token = CreateTestToken(audience: "other-audience");
        var client = app.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithWrongIssuer_Returns401()
    {
        await using var app = CreateApp();
        var token = CreateTestToken(issuer: "https://evil.example");
        var client = app.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithWrongSigningKey_Returns401()
    {
        await using var app = CreateApp();
        var token = CreateTestToken(signingKey: "another-key-that-is-long-enough-9876543210");
        var client = app.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task HealthProbes_RemainAnonymous()
    {
        await using var app = CreateApp();
        var client = app.CreateClient(new() { BaseAddress = new Uri("https://localhost") });

        var liveness = await client.GetAsync("/healthz");
        var readiness = await client.GetAsync("/readyz");

        liveness.EnsureSuccessStatusCode();
        readiness.EnsureSuccessStatusCode();
    }

    private static WebApplicationFactory<HockeySeason.Api.Program> CreateApp() =>
        new TestApiFactory();

    private static async Task<HttpClient> CreateAuthenticatedClientAsync(WebApplicationFactory<HockeySeason.Api.Program> app)
    {
        var client = app.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        var response = await client.PostAsJsonAsync("/api/auth/login", new { code = PinCode });
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<LoginResult>();
        client.DefaultRequestHeaders.Authorization = new("Bearer", result!.AccessToken);
        return client;
    }

    private static string CreateTestToken(
        string? issuer = null,
        string? audience = null,
        string? signingKey = null,
        int expiresInMinutes = 10)
    {
        var handler = new JwtSecurityTokenHandler();
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey ?? SigningKey)),
            SecurityAlgorithms.HmacSha256);
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer: issuer ?? Issuer,
            audience: audience ?? Audience,
            claims: [new Claim(JwtRegisteredClaimNames.Sub, "pin-user")],
            notBefore: now.AddMinutes(Math.Min(-1, expiresInMinutes) - 5),
            expires: now.AddMinutes(expiresInMinutes),
            signingCredentials: credentials);
        return handler.WriteToken(token);
    }

    private sealed class TestApiFactory : WebApplicationFactory<HockeySeason.Api.Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            // Remove user-secret / environment overrides loaded in Development so
            // the fixed test credentials are the single source of truth.
            builder.UseSetting("Auth:PinCode", PinCode);
            builder.UseSetting("Auth:SigningKey", SigningKey);
            builder.UseSetting("Auth:Issuer", Issuer);
            builder.UseSetting("Auth:Audience", Audience);
            builder.UseSetting("Auth:AccessTokenMinutes", "60");
            builder.UseSetting("LeagueApi:ApiKey", "test-league-key");
        }
    }
}
