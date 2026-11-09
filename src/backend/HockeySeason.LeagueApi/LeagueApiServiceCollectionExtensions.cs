using HockeySeason.LeagueApi;
using HockeySeason.LeagueApi.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HockeySeason.LeagueApi.Extensions;

/// <summary>
/// Registration helpers for the Spordle league API client.
/// </summary>
public static class LeagueApiServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="LeagueApiService"/> as a typed HTTP client bound to the
    /// <c>LeagueApi</c> configuration section.
    /// </summary>
    public static IServiceCollection AddLeagueApi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<LeagueApiOptions>()
            .BindConfiguration(LeagueApiOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(options => options.BaseAddress.IsAbsoluteUri, "LeagueApi:BaseAddress must be an absolute URI.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ApiKey),
                "LeagueApi:ApiKey must not be empty.")
            .ValidateOnStart();

        services.AddHttpClient<LeagueApiService>((provider, client) =>
        {
            var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<LeagueApiOptions>>().Value;
            client.BaseAddress = options.BaseAddress;
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        return services;
    }
}
