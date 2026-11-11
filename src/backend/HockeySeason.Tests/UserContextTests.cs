using System.Net;
using HockeySeason.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace HockeySeason.Tests;

/// <summary>
/// Covers the user context contract: bearer extraction from both the
/// standard and mirrored headers, and on-demand Auth0 /userinfo resolution.
/// </summary>
public sealed class UserContextTests
{
    [Theory]
    [InlineData("Bearer abc.def.ghi", "abc.def.ghi")]
    [InlineData("bearer abc.def.ghi", "abc.def.ghi")]
    [InlineData("Bearer   spaced-token  ", "spaced-token")]
    [InlineData("", null)]
    [InlineData("Basic dXNlcjpwYXNz", null)]
    [InlineData("Bearer", null)]
    public void BearerTokenParser_ExtractsOnlyBearerSchemes(string headerValue, string? expected) =>
        Assert.Equal(expected, BearerTokenParser.TryExtractBearerToken(headerValue, out var token) ? token : null);

    [Fact]
    public void BearerTokenAccessor_ReadsAuthorizationThenMirroredHeader()
    {
        Assert.Equal("token-standard", CreateAccessor(("Authorization", "Bearer token-standard")).GetBearerToken());

        var accessor = CreateAccessor(("X-Authorization", "Bearer token-mirrored"));
        Assert.Equal("token-mirrored", accessor.GetBearerToken());

        Assert.Null(CreateAccessor().GetBearerToken());
    }

    [Fact]
    public async Task UserContext_ResolvesProfile_FromUserinfoEndpoint()
    {
        await using var provider = new UserinfoTestServer(
            """{"sub":"auth0|6af39e3cd7146f2a7e5d4959","nickname":"cedric.gervasoni","name":"cedric.gervasoni@outlook.com","picture":"https://s.gravatar.com/avatar/abc?s=480","updated_at":"2026-11-11T22:13:09.062Z","email":"cedric.gervasoni@outlook.com","email_verified":true}""");

        using var client = new HttpClient { BaseAddress = new Uri(provider.BaseAddress) };
        var context = new Auth0UserContext(
            client,
            Options.Create(new UserInfoOptions { BaseAddress = provider.BaseAddress }),
            new StaticBearerTokenAccessor("token-123"),
            NullLogger<Auth0UserContext>.Instance);

        var user = await context.GetCurrentUserAsync();

        Assert.NotNull(user);
        Assert.Equal("auth0|6af39e3cd7146f2a7e5d4959", user.Sub);
        Assert.Equal("cedric.gervasoni", user.Nickname);
        Assert.Equal("cedric.gervasoni@outlook.com", user.Name);
        Assert.Equal("https://s.gravatar.com/avatar/abc?s=480", user.Picture);
        Assert.Equal("2026-11-11T22:13:09.062Z", user.UpdatedAt);
        Assert.Equal("cedric.gervasoni@outlook.com", user.Email);
        Assert.True(user.EmailVerified);
        Assert.Equal("Bearer token-123", provider.LastAuthorization);
    }

    [Fact]
    public async Task UserContext_ReturnsNullWithoutTokenOrOnProviderFailure()
    {
        await using var provider = new UserinfoTestServer("""{"sub":"x","email_verified":false}""", HttpStatusCode.Forbidden);

        using var client = new HttpClient { BaseAddress = new Uri(provider.BaseAddress) };
        var context = new Auth0UserContext(
            client,
            Options.Create(new UserInfoOptions { BaseAddress = provider.BaseAddress }),
            new StaticBearerTokenAccessor("token-123"),
            NullLogger<Auth0UserContext>.Instance);
        Assert.Null(await context.GetCurrentUserAsync());
        Assert.Equal(1, provider.CallCount);

        var noToken = new Auth0UserContext(
            client,
            Options.Create(new UserInfoOptions { BaseAddress = provider.BaseAddress }),
            new StaticBearerTokenAccessor(null),
            NullLogger<Auth0UserContext>.Instance);
        Assert.Null(await noToken.GetCurrentUserAsync());
        Assert.Equal(1, provider.CallCount);
    }

    private static IBearerTokenAccessor CreateAccessor(params (string Header, string Value)[] headers)
    {
        var httpContext = new DefaultHttpContext();
        foreach (var (header, value) in headers)
        {
            httpContext.Request.Headers[header] = value;
        }
        return new BearerTokenAccessor(new HttpContextAccessor { HttpContext = httpContext });
    }

    private sealed class StaticBearerTokenAccessor(string? token) : IBearerTokenAccessor
    {
        public string? GetBearerToken() => token;
    }

    /// <summary>In-memory id-provider host exposing only /userinfo.</summary>
    private sealed class UserinfoTestServer : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly string _payload;
        private readonly HttpStatusCode _status;

        public string BaseAddress { get; private set; } = string.Empty;
        public string? LastAuthorization { get; private set; }
        public int CallCount { get; private set; }

        public UserinfoTestServer(string payload, HttpStatusCode status = HttpStatusCode.OK)
        {
            _payload = payload;
            _status = status;
            _app = BuildHost();
            BaseAddress = ResolveBaseAddress();
        }

        private WebApplication BuildHost()
        {
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            var app = builder.Build();
            app.MapGet("/userinfo", (HttpRequest request) =>
            {
                LastAuthorization = request.Headers.Authorization.ToString();
                CallCount++;
                return _status == HttpStatusCode.OK
                    ? Results.Content(_payload, "application/json")
                    : Results.StatusCode((int)_status);
            });
            app.StartAsync().GetAwaiter().GetResult();
            return app;
        }

        private string ResolveBaseAddress()
        {
            var addresses = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!;
            return addresses.Addresses.First().Replace("[::1]", "127.0.0.1").Replace("localhost", "127.0.0.1").TrimEnd('/') + "/";
        }

        public ValueTask DisposeAsync()
        {
            _app.StopAsync().GetAwaiter().GetResult();
            _app.DisposeAsync().GetAwaiter().GetResult();
            return ValueTask.CompletedTask;
        }
    }
}
