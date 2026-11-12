using Microsoft.EntityFrameworkCore;

using Microsoft.Extensions.Configuration;

namespace HockeySeason.Infrastructure.Data;

/// <summary>
/// Connection + migration settings for PostgreSQL persistence.
/// Bound from the configuration root (env vars like
/// <c>ConnectionStrings__Postgres</c> and <c>Database__MigrateOnStartup</c>).
/// </summary>
public sealed class DatabaseOptions
{
    /// <summary>Configuration key carrying the PostgreSQL connection string.</summary>
    public const string ConnectionName = "Postgres";

    /// <summary>Configuration section bound to these options.</summary>
    public const string SectionName = "Database";

    /// <summary>
    /// Run <c>MigrateAsync</c> at host startup. Default <c>false</c>
    /// (development); hosted/production deployments set it to <c>true</c>.
    /// </summary>
    public bool MigrateOnStartup { get; set; }

    /// <summary>
    /// Whether the persistence provider is reachable: a configured connection
    /// string. An empty value is treated as not configured (test hosts clear
    /// the key this way to bypass the developer's user-secrets store).
    /// </summary>
    public static bool IsPersistenceConfigured(IConfiguration configuration) =>
        configuration.GetConnectionString(ConnectionName) is { Length: > 0 };
}
