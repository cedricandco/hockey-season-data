using HockeySeason.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HockeySeason.Functions;

/// <summary>
/// Anonymous liveness and readiness probes. Readiness also proves the
/// PostgreSQL connection when persistence is configured and reports whether
/// the schema is up to date; when the database is unreachable while
/// configured, or behind the bundled EF Core migrations, readiness degrades to
/// 503.
/// </summary>
public sealed class ProbeFunctions(IConfiguration configuration, IDbContextFactory<HockeySeasonDbContext> dbContextFactory)
{
    [Function("Healthz")]
    public IActionResult Healthz([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "healthz")] HttpRequest request)
        => new OkObjectResult(new { status = "ok" });

    [Function("Readyz")]
    public async Task<IActionResult> Readyz([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "readyz")] HttpRequest request, CancellationToken cancellationToken)
    {
        if (!DatabaseOptions.IsPersistenceConfigured(configuration))
        {
            return new OkObjectResult(new { status = "ready", database = "not_configured" });
        }

        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            if (!await db.Database.CanConnectAsync(cancellationToken))
            {
                return new StatusCodeResult(StatusCodes.Status503ServiceUnavailable);
            }

            // A server reachable but behind the bundled migrations answers 200
            // while every query on a new table fails at runtime (42P01). Report
            // the pending migrations so a deployment never looks ready by
            // mistake; the migration itself stays manual unless
            // Database:MigrateOnStartup is on.
            var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();
            return pending.Length == 0
                ? new OkObjectResult(new { status = "ready", database = "ok" })
                : new ObjectResult(new
                {
                    status = "migrations_pending",
                    database = "outdated",
                    pendingMigrations = pending,
                })
                {
                    StatusCode = StatusCodes.Status503ServiceUnavailable,
                };
        }
        catch (Exception exception) when (
            exception is HttpRequestException
                or InvalidOperationException
                or Npgsql.NpgsqlException)
        {
            return new StatusCodeResult(StatusCodes.Status503ServiceUnavailable);
        }
    }
}
