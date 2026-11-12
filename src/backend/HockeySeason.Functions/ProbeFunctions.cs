using HockeySeason.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HockeySeason.Functions;

/// <summary>
/// Anonymous liveness and readiness probes. Readiness also proves the
/// PostgreSQL connection when persistence is configured; when the database is
/// unreachable while configured, readiness degrades to 503.
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
            return await db.Database.CanConnectAsync(cancellationToken)
                ? new OkObjectResult(new { status = "ready", database = "ok" })
                : new StatusCodeResult(StatusCodes.Status503ServiceUnavailable);
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
