using System.Net;
using System.Net.Http.Json;
using HockeySeason.Api;
using HockeySeason.Auth;
using HockeySeason.LeagueApi;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HockeySeason.Tests;

/// <summary>
/// End-to-end coverage of the Auth0-backed JWT security layer using the
/// composition root of HockeySeason.Api with an in-memory OIDC provider.
/// </summary>
public sealed class AuthEndpointsTests : IAsyncLifetime
{
    private readonly OidcTestServer _identityProvider = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _identityProvider.DisposeAsync();

    [Fact]
    public async Task ApiEndpoints_AreAnonymous()
    {
        await using var app = CreateApp();
        var client = app.CreateClient(new() { BaseAddress = new Uri("https://localhost") });

        var health = await client.GetAsync("/api/health");
        var report = await client.GetAsync("/api/weekly-report?startDate=2026-11-09&endDate=2026-11-02&divisionId=194899&teamId=182497");

        health.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, report.StatusCode);
    }

    [Fact]
    public async Task WeeklyReport_ValidatesParametersAndPassesSelectionToService()
    {
        var service = new FakeWeeklyReportService();
        await using var app = CreateApp().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<IWeeklyReportService>(service)));
        var client = await CreateClientWithTokenAsync(app, sub: "user-a");

        var invalid = await client.GetAsync("/api/weekly-report?startDate=2026-11-09&endDate=2026-11-02&divisionId=194899&teamId=182497");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Null(service.Request);

        var response = await client.GetAsync("/api/weekly-report?startDate=2026-11-02&endDate=2026-11-09&divisionId=194899&divisionId=194901&teamId=182497&teamId=194901");
        response.EnsureSuccessStatusCode();
        Assert.NotNull(service.Request);
        Assert.Equal(new[] { 194899, 194901 }, service.Request.DivisionIds);
        Assert.Equal(new[] { 182497, 194901 }, service.Request.TeamIds);
    }

    private sealed class FakeWeeklyReportService : IWeeklyReportService
    {
        public WeeklyReportRequest? Request { get; private set; }

        public Task<WeeklyReportResponse> GetAsync(WeeklyReportRequest request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new WeeklyReportResponse(request.StartDate, request.EndDate, request.TeamIds, []));
        }
    }

    [Fact]
    public async Task ProtectedEndpoint_WithValidIdToken_Returns200()
    {
        // The JwtBearer middleware challenges with 401 JSON for missing
        // tokens; request pipelines that authenticate (Functions host) are
        // covered by FunctionsTests.
        await using var app = CreateApp();
        var client = await CreateClientWithTokenAsync(app, sub: "user-a");

        var response = await client.GetAsync("/api/health");

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task ApiEndpoints_DoNotChallengeAnonymousCallers()
    {
        // Business endpoints are anonymous by design (see README): the JwtBearer
        // challenge only fires for pipelines that require authentication.
        await using var app = CreateApp();
        var client = app.CreateClient(new() { BaseAddress = new Uri("https://localhost") });

        var response = await client.GetAsync("/api/health");

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public void JwtValidator_AcceptsTokenForConfiguredAudience()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new AuthOptions
        {
            Authority = _identityProvider.Authority,
            Issuer = _identityProvider.Issuer,
            Audience = _identityProvider.Audience,
            AllowHttpAuthority = true,
        });
        var validator = new JwtValidator(options);

        var token = _identityProvider.CreateToken(subject: "auth0|user-a");
        Assert.True(validator.TryValidate(token, out var subject));
        Assert.Equal("auth0|user-a", subject);
    }

    [Fact]
    public void JwtValidator_RejectsTokenForWrongAudience()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new AuthOptions
        {
            Authority = _identityProvider.Authority,
            Issuer = _identityProvider.Issuer,
            Audience = _identityProvider.Audience,
            AllowHttpAuthority = true,
        });
        var validator = new JwtValidator(options);

        var token = _identityProvider.CreateToken(audience: "https://other-audience.test");
        Assert.False(validator.TryValidate(token, out _));
    }

    [Fact]
    public void JwtValidator_RejectsExpiredToken()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new AuthOptions
        {
            Authority = _identityProvider.Authority,
            Issuer = _identityProvider.Issuer,
            Audience = _identityProvider.Audience,
            AllowHttpAuthority = true,
        });
        var validator = new JwtValidator(options);

        var token = _identityProvider.CreateToken(
            expires: DateTimeOffset.UtcNow.AddHours(-1),
            notBefore: DateTime.UtcNow.AddHours(-2));
        Assert.False(validator.TryValidate(token, out _));
    }

    [Fact]
    public void JwtValidator_RejectsForgedSignature()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new AuthOptions
        {
            Authority = _identityProvider.Authority,
            Issuer = _identityProvider.Issuer,
            Audience = _identityProvider.Audience,
            AllowHttpAuthority = true,
        });
        var validator = new JwtValidator(options);

        var otherProvider = new OidcTestServer();
        var token = otherProvider.CreateToken();
        Assert.False(validator.TryValidate(token, out _));
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

    private TestApiFactory CreateApp() => new(_identityProvider);

    private async Task<HttpClient> CreateClientWithTokenAsync(
        WebApplicationFactory<HockeySeason.Api.Program> app,
        string sub,
        string? audience = null)
    {
        var client = app.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        var token = _identityProvider.CreateToken(subject: sub, audience: audience);
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return await Task.FromResult(client);
    }

    private sealed class TestApiFactory : WebApplicationFactory<HockeySeason.Api.Program>
    {
        private readonly OidcTestServer _identityProvider;

        public TestApiFactory(OidcTestServer identityProvider)
        {
            _identityProvider = identityProvider;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Auth:Authority", _identityProvider.Authority);
            builder.UseSetting("Auth:Issuer", _identityProvider.Issuer);
            builder.UseSetting("Auth:Audience", _identityProvider.Audience);
            builder.UseSetting("Auth:AllowHttpAuthority", "true");
            builder.UseSetting("LeagueApi:ApiKey", "test-league-key");
        }
    }
}
