using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HockeySeason.Infrastructure.Data;

/// <summary>
/// Singleton <see cref="IDbContextFactory{TContext}"/> that postpones the
/// Npgsql configuration until the first context is created. This keeps hosts
/// without a configured connection string (unit-test host, Functions host)
/// startable: the missing-connection-string error surfaces only when a service
/// actually touches the database, with an actionable message.
/// </summary>
public sealed class LazyConnectionStringDbContextFactory(IConfiguration configuration, ILoggerFactory loggerFactory)
    : IDbContextFactory<HockeySeasonDbContext>
{
    private readonly object _lock = new();
    private PooledDbContextFactory<HockeySeasonDbContext>? _inner;
    private string? _failure;

    public HockeySeasonDbContext CreateDbContext()
    {
        var inner = GetOrCreateFactory();
        return inner.CreateDbContext();
    }

    public async ValueTask<HockeySeasonDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        var inner = GetOrCreateFactory();
        return await inner.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
    }

    private PooledDbContextFactory<HockeySeasonDbContext> GetOrCreateFactory()
    {
        if (_inner is not null)
        {
            return _inner;
        }

        lock (_lock)
        {
            if (_inner is not null)
            {
                return _inner;
            }

            // An empty value counts as "not configured" (test hosts clear the
            // key this way).
            var connectionString = configuration.GetConnectionString(DatabaseOptions.ConnectionName)
                is { Length: > 0 } value
                ? value
                : throw new InvalidOperationException(_failure ??= MissingConnectionStringMessage);

            var optionsBuilder = new DbContextOptionsBuilder<HockeySeasonDbContext>();
            optionsBuilder.UseNpgsql(connectionString);
            optionsBuilder.UseSnakeCaseNamingConvention();
            optionsBuilder.UseLoggerFactory(loggerFactory);
            _inner = new PooledDbContextFactory<HockeySeasonDbContext>(optionsBuilder.Options, poolSize: 64);
            return _inner;
        }
    }

    /// <summary>Actionable guidance when persistence is requested but not configured.</summary>
    internal const string MissingConnectionStringMessage =
        "Set ConnectionStrings:Postgres (user-secrets in dev, ConnectionStrings__Postgres in production) to enable persistence.";
}
