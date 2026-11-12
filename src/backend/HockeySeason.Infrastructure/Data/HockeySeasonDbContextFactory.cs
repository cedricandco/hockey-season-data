using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.UserSecrets;

namespace HockeySeason.Infrastructure.Data;

/// <summary>
/// Design-time factory for <c>dotnet ef</c> commands. The connection string
/// comes from user-secrets / env (<c>ConnectionStrings:Postgres</c>); a
/// design-time placeholder is used when the secret is absent, since generating
/// migrations never needs credentials. Secrets are resolved with the Functions
/// host's user-secrets id first, so <c>dotnet ef database update
/// --startup-project ../HockeySeason.Functions</c> targets the configured database.
/// </summary>
public sealed class HockeySeasonDbContextFactory : IDesignTimeDbContextFactory<HockeySeasonDbContext>
{
    /// <summary>UserSecretsId of the Functions host (the canonical secret store for dev).</summary>
    private const string ApiUserSecretsId = "e84f87c0-4937-4d07-a9ba-cef86eb02329";

    private const string PlaceholderConnectionString =
        "Host=localhost;Port=5432;Database=hockey_season_design_time;Username=postgres;Password=postgres";

    public HockeySeasonDbContext CreateDbContext(string[] args)
    {
        // User secrets of the Functions host (canonical dev store; the Infrastructure
        // project cannot reference the Functions project, so the id is by value).
        // Lower precedence than environment variables, identical to the host.
        var builder = new ConfigurationBuilder().AddEnvironmentVariables();
        if (TryLoadApiSecrets(out var apiSecrets))
        {
            builder.AddInMemoryCollection(apiSecrets);
        }
        builder.AddUserSecrets<HockeySeasonDbContextFactory>(optional: true);
        var configuration = builder.Build();

        // An empty value counts as "not configured" (test hosts clear the key
        // this way); fall back to the design-time placeholder.
        var connectionString = configuration.GetConnectionString(DatabaseOptions.ConnectionName)
            is { Length: > 0 } value ? value : PlaceholderConnectionString;

        var optionsBuilder = new DbContextOptionsBuilder<HockeySeasonDbContext>();
        optionsBuilder.UseNpgsql(connectionString);
        optionsBuilder.UseSnakeCaseNamingConvention();
        return new HockeySeasonDbContext(optionsBuilder.Options);
    }

    /// <summary>
    /// Loads the Functions host's user secrets by id, mirroring how the host resolves
    /// them (path per platform: %APPDATA%\Microsoft\UserSecrets\<id>\secrets.json).
    /// </summary>
    private static bool TryLoadApiSecrets(out Dictionary<string, string?> secrets)
    {
        secrets = [];
        var secretPath = Environment.OSVersion.Platform == PlatformID.Win32NT
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "UserSecrets", ApiUserSecretsId, "secrets.json")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".microsoft", "usersecrets", ApiUserSecretsId, "secrets.json");
        if (!File.Exists(secretPath))
        {
            return false;
        }
        var json = JsonDocument.Parse(File.ReadAllText(secretPath));
        foreach (var property in json.RootElement.EnumerateObject())
        {
            secrets[property.Name] = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : property.Value.ToString();
        }
        return true;
    }
}
