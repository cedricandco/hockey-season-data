using System.Diagnostics;
using HockeySeason.Application;
using HockeySeason.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HockeySeason.Infrastructure;

/// <summary>
/// Composition-root helpers for the PostgreSQL persistence layer. Hosts call
/// <see cref="AddHockeySeasonData"/> once; the connection string comes from
/// <c>ConnectionStrings:Postgres</c> (user-secrets in development,
/// <c>ConnectionStrings__Postgres</c> in hosted environments) and migrations
/// apply on startup only when <c>Database:MigrateOnStartup</c> is enabled.
/// </summary>
public static class HockeySeasonDataExtensions
{
    /// <summary>
    /// Registers the EF Core Npgsql <see cref="HockeySeasonDbContext"/> with
    /// snake_case naming, plus <see cref="DatabaseOptions"/>. No migration
    /// runs here; hosts opt in via the returned builder or the readyz probe.
    /// </summary>
    public static IServiceCollection AddHockeySeasonData(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName));

        // Factory-first registration: IDbContextFactory (singleton) plus the
        // scopedDbContext it can serve. The connection string is read at
        // context creation, not registration, so hosts without persistence
        // configured (tests without a database, Functions host) only fail when
        // they actually touch the database — not at startup.
        services.AddSingleton<IDbContextFactory<HockeySeasonDbContext>>(provider =>
            new LazyConnectionStringDbContextFactory(configuration, provider.GetRequiredService<ILoggerFactory>()));
        services.AddScoped<HockeySeasonDbContext>(provider =>
            provider.GetRequiredService<IDbContextFactory<HockeySeasonDbContext>>().CreateDbContext());
        services.AddScoped<IUserProvisioningService, UserProvisioningService>();
        services.AddScoped<IScopeService, ScopeService>();
        services.AddScoped<IUserService, UserService>();
        services.AddMemoryCache();

        // Authorization switches: AddOptions so hosts can override, e.g. the
        // temporary open-access phase via appsettings or
        // Authorization__OpenAccess env var.
        services.AddOptions<AuthorizationOptions>()
            .Bind(configuration.GetSection(AuthorizationOptions.SectionName));

        return services;
    }

    /// <summary>
    /// Applies pending EF Core migrations when
    /// <c>Database:MigrateOnStartup</c> is <c>true</c> (hosted deployments).
    /// In development the default is <c>false</c>: apply migrations manually
    /// with <c>dotnet ef database update</c>.
    /// </summary>
    public static async Task MigrateDatabaseIfConfiguredAsync(
        this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var logger = services.GetService<ILoggerFactory>()?.CreateLogger("HockeySeason.Infrastructure.Migrations");
        var migrate = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>()?.MigrateOnStartup == true;
        if (!migrate)
        {
            logger?.LogDebug("Database migration on startup disabled (Database:MigrateOnStartup).");
            return;
        }

        var dbFactory = services.GetRequiredService<IDbContextFactory<HockeySeasonDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var stopwatch = Stopwatch.StartNew();
        await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();
        logger?.LogInformation("Database migrations applied in {ElapsedMilliseconds} ms.", stopwatch.ElapsedMilliseconds);
    }
}
