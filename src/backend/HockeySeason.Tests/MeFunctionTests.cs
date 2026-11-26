using System.Security.Claims;
using HockeySeason.Auth;
using HockeySeason.Functions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HockeySeason.Tests;

/// <summary>
/// The caller profile endpoint. It must hand the invocation's own bearer token
/// to the identity provider: on the isolated Functions worker no
/// <c>IHttpContextAccessor</c> is registered, so resolving the token through an
/// ambient accessor finds nothing and every call degenerates into
/// <c>502 userinfo_unavailable</c> even with a valid token.
/// </summary>
public sealed class MeFunctionTests : IAsyncLifetime
{
    private readonly OidcTestServer _identityProvider = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _identityProvider.DisposeAsync();

    [Fact]
    public async Task Me_PrefersTheStandardHeaderOverTheMirror()
    {
        var userContext = new RecordingUserContext();
        var function = new MeFunction(userContext, NullLogger<MeFunction>.Instance);
        var httpContext = new DefaultHttpContext();
        // A token the configured authority actually signed: the endpoint
        // authenticates before resolving the profile.
        var token = _identityProvider.CreateToken();
        httpContext.Request.Headers.Authorization = $"Bearer {token}";
        // The SPA mirrors the token; FunctionsAuthorization requires the mirror.
        httpContext.Request.Headers[UserContextExtensions.MirroredAuthorizationHeader] = $"Bearer {token}";
        var context = FunctionsTests.CreateContextWith(
            httpContext, FunctionsTests.ConfigureAuthServices(_identityProvider));

        var result = await function.Run(httpContext.Request, context, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(token, userContext.LastToken);
    }

    [Fact]
    public async Task Me_FallsBackToTheMirroredHeader()
    {
        var userContext = new RecordingUserContext();
        var function = new MeFunction(userContext, NullLogger<MeFunction>.Instance);
        var httpContext = new DefaultHttpContext();
        var token = _identityProvider.CreateToken();
        httpContext.Request.Headers[UserContextExtensions.MirroredAuthorizationHeader] = $"Bearer {token}";
        var context = FunctionsTests.CreateContextWith(
            httpContext, FunctionsTests.ConfigureAuthServices(_identityProvider));

        var result = await function.Run(httpContext.Request, context, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(token, userContext.LastToken);
    }

    /// <summary>Captures the token it receives instead of calling the provider.</summary>
    private sealed class RecordingUserContext : IUserContext
    {
        public string? LastToken { get; private set; }

        public string? LastFailure { get; private set; }

        /// <summary>Set to make the fake report a failure.</summary>
        public UserInfoFailure? Failure { get; init; }

        public Task<UserInfo?> GetCurrentUserAsync(CancellationToken cancellationToken = default) =>
            GetUserAsync(null, cancellationToken);

        public async Task<UserInfo?> GetUserAsync(string? bearerToken, CancellationToken cancellationToken = default) =>
            (await TryGetUserAsync(bearerToken, cancellationToken)).Profile;

        public Task<UserInfoResult> TryGetUserAsync(string? bearerToken, CancellationToken cancellationToken = default)
        {
            LastToken = bearerToken;
            LastFailure = Failure?.ToString();
            return Task.FromResult(Failure is { } failure
                ? UserInfoResult.Failed(failure)
                : UserInfoResult.Success(
                    new UserInfo("auth0|me", "cg", "Cedric Gervasoni", null, null, "c@example.com", true)));
        }
    }
}
