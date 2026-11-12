using System.Security.Claims;
using System.Text.Json;
using HockeySeason.Auth;
using HockeySeason.Functions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using HockeySeason.Infrastructure.Data;
using Xunit;

namespace HockeySeason.Tests;

/// <summary>
/// Coverage of the Auth0-backed JWT security layer as enforced by the Functions
/// host (<see cref="FunctionsAuthorization"/>) and by <see cref="JwtValidator"/>,
/// using an in-memory OIDC provider.
/// </summary>
public sealed class FunctionsAuthTests : IAsyncLifetime
{
    private readonly OidcTestServer _identityProvider = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _identityProvider.DisposeAsync();

    [Fact]
    public void BusinessFunction_WithoutToken_Returns401Json()
    {
        var request = new DefaultHttpContext().Request;
        var context = FunctionsTests.CreateAnonymousBearerContext((DefaultHttpContext)request.HttpContext, string.Empty, _identityProvider);

        var authorization = FunctionsAuthorization.IsAuthenticatedV2(context);
        var result = FunctionsAuthorization.UnauthorizedResult(authorization.Reason);

        Assert.False(authorization.IsAuthenticated);
        Assert.Equal(StatusCodes.Status401Unauthorized, result.StatusCode);
        using var body = JsonDocument.Parse(JsonSerializer.Serialize(result.Value));
        Assert.Equal("invalid_token", body.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public void BusinessFunction_RejectsWrongAudience()
    {
        var token = _identityProvider.CreateToken(audience: "https://other-audience.test");
        var request = new DefaultHttpContext().Request;
        var context = FunctionsTests.CreateBearerRequestContext(request, _identityProvider, token);

        var authorization = FunctionsAuthorization.IsAuthenticatedV2(context);

        Assert.False(authorization.IsAuthenticated);
        Assert.Equal("invalid_audience", authorization.Reason);
        Assert.Null(FunctionsAuthorization.GetPrincipal(context));
    }

    [Fact]
    public void ValidToken_ExposesThePrincipalToFunctions()
    {
        var token = _identityProvider.CreateToken(subject: "auth0|user-a", extraClaims: [new Claim("roles", "admin")]);
        var request = new DefaultHttpContext().Request;
        var context = FunctionsTests.CreateBearerRequestContext(request, _identityProvider, token);

        Assert.True(FunctionsAuthorization.IsAuthenticatedV2(context).IsAuthenticated);

        var principal = FunctionsAuthorization.GetPrincipal(context);
        Assert.NotNull(principal);
        Assert.Equal("auth0|user-a", principal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.True(RequestUserResolver.IsAdmin(context));
    }

    [Fact]
    public void ValidToken_WithoutAdminRole_IsNotAdmin()
    {
        var request = new DefaultHttpContext().Request;
        var context = FunctionsTests.CreateBearerRequestContext(request, _identityProvider);

        Assert.True(FunctionsAuthorization.IsAuthenticatedV2(context).IsAuthenticated);
        Assert.False(RequestUserResolver.IsAdmin(context));
    }

    [Fact]
    public async Task Probes_RemainAnonymous()
    {
        var emptyConfiguration = new ConfigurationBuilder().Build();
        var probes = new ProbeFunctions(emptyConfiguration, null!);
        var request = new DefaultHttpContext().Request;

        Assert.IsType<OkObjectResult>(probes.Healthz(request));
        // Without a connection string readiness stays "ready" and never touches the database.
        var readiness = Assert.IsType<OkObjectResult>(await probes.Readyz(request, CancellationToken.None));
        using var body = JsonDocument.Parse(JsonSerializer.Serialize(readiness.Value));
        Assert.Equal("not_configured", body.RootElement.GetProperty("database").GetString());
    }

    [Fact]
    public async Task Readiness_Degrades503_WhenTheDatabaseIsUnreachable()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = "Host=test" })
            .Build();
        var probes = new ProbeFunctions(configuration, new UnreachableFactory());

        var result = await probes.Readyz(new DefaultHttpContext().Request, CancellationToken.None);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, Assert.IsType<StatusCodeResult>(result).StatusCode);
    }

    [Fact]
    public void JwtValidator_AcceptsTokenForConfiguredAudience()
    {
        var validator = CreateValidator();

        var token = _identityProvider.CreateToken(subject: "auth0|user-a");
        Assert.True(validator.TryValidate(token, out var subject));
        Assert.Equal("auth0|user-a", subject);
    }

    [Fact]
    public void JwtValidator_ReturnsThePrincipalOfAValidToken()
    {
        var validator = CreateValidator();

        var result = validator.Validate(_identityProvider.CreateToken(subject: "auth0|user-a", extraClaims: [new Claim("roles", "admin")]));

        Assert.True(result.IsValid);
        Assert.Equal("auth0|user-a", result.Subject);
        Assert.NotNull(result.Principal);
        Assert.True(result.Principal.HasClaim(ClaimTypes.Role, "admin"));
    }

    [Fact]
    public void JwtValidator_RejectsTokenForWrongAudience()
    {
        var validator = CreateValidator();

        var token = _identityProvider.CreateToken(audience: "https://other-audience.test");
        Assert.False(validator.TryValidate(token, out _));
    }

    [Fact]
    public void JwtValidator_RejectsExpiredToken()
    {
        var validator = CreateValidator();

        var token = _identityProvider.CreateToken(
            expires: DateTimeOffset.UtcNow.AddHours(-1),
            notBefore: DateTime.UtcNow.AddHours(-2));
        Assert.False(validator.TryValidate(token, out _));
    }

    [Fact]
    public async Task JwtValidator_RejectsForgedSignature()
    {
        var validator = CreateValidator();

        await using var otherProvider = new OidcTestServer();
        var token = otherProvider.CreateToken();
        Assert.False(validator.TryValidate(token, out _));
    }

    private JwtValidator CreateValidator() => new(Microsoft.Extensions.Options.Options.Create(new AuthOptions
    {
        Authority = _identityProvider.Authority,
        Issuer = _identityProvider.Issuer,
        Audience = _identityProvider.Audience,
        AllowHttpAuthority = true,
    }));

    private sealed class UnreachableFactory : IDbContextFactory<HockeySeasonDbContext>
    {
        public HockeySeasonDbContext CreateDbContext() => throw new InvalidOperationException("db unreachable");
    }
}
