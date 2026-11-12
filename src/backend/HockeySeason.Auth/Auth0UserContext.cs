using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HockeySeason.Auth;

/// <summary>
/// Default <see cref="IUserContext"/>: calls the identity provider's
/// <c>/userinfo</c> endpoint with the caller's bearer token. The call happens
/// on demand (never during token validation) so hosts that cannot use the
/// JwtBearer middleware — Functions triggers — still get profile data.
/// </summary>
public sealed class Auth0UserContext : IUserContext
{
    private const string Scheme = "Bearer ";

    private readonly HttpClient _httpClient;
    private readonly UserInfoOptions _options;
    private readonly IBearerTokenAccessor _tokenAccessor;
    private readonly ILogger<Auth0UserContext> _logger;

    public Auth0UserContext(
        HttpClient httpClient,
        IOptions<UserInfoOptions> options,
        IBearerTokenAccessor tokenAccessor,
        ILogger<Auth0UserContext> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
        _tokenAccessor = tokenAccessor ?? throw new ArgumentNullException(nameof(tokenAccessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<UserInfo?> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        var token = _tokenAccessor.GetBearerToken();
        if (string.IsNullOrWhiteSpace(token))
        {
            _logger.LogDebug("UserContext resolved no bearer token from the request headers.");
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, _options.Path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "UserContext userinfo call rejected by the provider: status={Status}, path={Path}.",
                    (int)response.StatusCode,
                    _options.Path);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var user = await JsonSerializer.DeserializeAsync<UserInfo>(
                stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                cancellationToken).ConfigureAwait(false);
            if (user is not { Sub: { Length: > 0 } })
            {
                _logger.LogWarning("UserContext received a userinfo payload without a usable 'sub' claim.");
                return null;
            }
            return user;
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(exception, "UserContext could not reach the identity provider userinfo endpoint.");
            return null;
        }
        catch (JsonException exception)
        {
            _logger.LogError(exception, "UserContext received an unparseable userinfo payload.");
            return null;
        }
    }
}

/// <summary>Shared constants for user-context resolution.</summary>
public static class UserContextExtensions
{
    /// <summary>
    /// Header mirrored by the SPA carrying the same bearer token as
    /// <c>Authorization</c>; consumed when the standard header is absent.
    /// </summary>
    public const string MirroredAuthorizationHeader = "X-Authorization";
}

/// <summary>
/// Composition-root helpers for the user context. Hosts call
/// <see cref="AddHockeySeasonUserContext"/> once; the tenant host resolves
/// from <c>Auth:Authority</c> unless <c>UserInfo:BaseAddress</c> overrides it.
/// </summary>
public static class HockeySeasonUserContextExtensions
{
    /// <summary>
    /// Adds the auth-project user context: options bound to the
    /// <c>UserInfo</c> section, a typed userinfo client and DI plumbing for
    /// <see cref="IUserContext"/>/<see cref="IBearerTokenAccessor"/>. Call from
    /// any composition root (the Functions host).
    /// </summary>
    public static IServiceCollection AddHockeySeasonUserContext(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpContextAccessor();
        services.AddSingleton<IBearerTokenAccessor, BearerTokenAccessor>();
        services.AddHttpClient<Auth0UserContext>((provider, client) =>
            {
                var configuration = provider.GetRequiredService<IConfiguration>();
                var section = configuration.GetSection(UserInfoOptions.SectionName);
                // The userinfo host defaults to the Auth authority so the tenant
                // (dev-xxxx) lives in one settings place: Auth:Authority.
                var options = section.Get<UserInfoOptions>() ?? new UserInfoOptions();
                if (string.IsNullOrWhiteSpace(options.BaseAddress))
                {
                    options.BaseAddress = configuration.GetSection(AuthOptions.SectionName)[nameof(AuthOptions.Authority)]
                        ?? throw new InvalidOperationException(
                            $"Set UserInfo:BaseAddress or Auth:Authority to enable the user context.");
                }
                client.BaseAddress = new Uri(options.BaseAddress, UriKind.Absolute);
                client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            })
            .AddTypedClient<IUserContext>((http, provider) =>
                new Auth0UserContext(http, CreateOptions(provider), provider.GetRequiredService<IBearerTokenAccessor>(),
                    provider.GetRequiredService<ILogger<Auth0UserContext>>()));
        return services;
    }

    private static IOptions<UserInfoOptions> CreateOptions(IServiceProvider provider)
    {
        var configuration = provider.GetRequiredService<IConfiguration>();
        var section = configuration.GetSection(UserInfoOptions.SectionName);
        var options = section.Get<UserInfoOptions>() ?? new UserInfoOptions();
        if (string.IsNullOrWhiteSpace(options.BaseAddress))
        {
            options.BaseAddress = configuration.GetSection(AuthOptions.SectionName)[nameof(AuthOptions.Authority)] ?? string.Empty;
        }
        return Microsoft.Extensions.Options.Options.Create(options);
    }
}
