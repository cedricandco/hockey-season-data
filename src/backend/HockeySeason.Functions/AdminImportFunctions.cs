using System.Globalization;
using System.Text.Json;
using HockeySeason.Application;
using HockeySeason.Infrastructure.Data;
using HockeySeason.LeagueApi;
using HockeySeason.LeagueApi.Http;
using HockeySeason.LeagueApi.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace HockeySeason.Functions;

/// <summary>
/// Admin: Spordle reference-data import. Upserts associations/divisions
/// (offices), leagues (per season) and teams (per league standings) by Spordle
/// external id. Requires the <c>admin</c> role claim.
/// </summary>
public sealed class AdminImportFunctions(
    LeagueApiService league,
    IDbContextFactory<HockeySeasonDbContext> dbContextFactory,
    IOptions<LeagueApiOptions> options,
    IConfiguration configuration)
{
    [Function("ImportOffices")]
    public async Task<IActionResult> ImportOffices(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "import/offices")] HttpRequest request,
        FunctionContext context, CancellationToken cancellationToken)
    {
        if (Denied(context, configuration) is { } denied) return denied;

        var imported = 0;
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;

        foreach (var office in LeagueOffices.All)
        {
            var externalId = office.Id.ToString(CultureInfo.InvariantCulture);
            var organization = await db.Organizations.FirstOrDefaultAsync(o => o.ExternalId == externalId, cancellationToken);
            if (organization is null)
            {
                organization = new() { Name = office.Name, ExternalId = externalId, CreatedAt = now, UpdatedAt = now };
                db.Organizations.Add(organization);
            }
            else
            {
                organization.Name = office.Name;
                organization.UpdatedAt = now;
            }

            var division = await db.Divisions.FirstOrDefaultAsync(d => d.ExternalId == externalId, cancellationToken);
            if (division is null)
            {
                division = new() { Organization = organization, Name = office.Name, ExternalId = externalId, CreatedAt = now, UpdatedAt = now };
                db.Divisions.Add(division);
            }
            else
            {
                division.Name = office.Name;
                division.UpdatedAt = now;
            }
            imported++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return new OkObjectResult(new { imported });
    }

    [Function("ImportLeagues")]
    public async Task<IActionResult> ImportLeagues(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "import/leagues")] HttpRequest request,
        FunctionContext context, CancellationToken cancellationToken)
    {
        if (Denied(context, configuration) is { } denied) return denied;

        var seasonId = request.Query["seasonId"].ToString();
        var seasonExternalId = string.IsNullOrWhiteSpace(seasonId) ? options.Value.DefaultSeasonId : seasonId;

        var imported = 0;
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;

        var season = await db.Seasons.FirstOrDefaultAsync(s => s.ExternalId == seasonExternalId, cancellationToken);
        if (season is null)
        {
            season = new() { Name = seasonExternalId, ExternalId = seasonExternalId, CreatedAt = now, UpdatedAt = now };
            db.Seasons.Add(season);
        }

        foreach (var office in LeagueOffices.All)
        {
            var officeExternalId = office.Id.ToString(CultureInfo.InvariantCulture);
            var division = await db.Divisions
                .Include(d => d.Organization)
                .FirstOrDefaultAsync(d => d.ExternalId == officeExternalId, cancellationToken);
            if (division is null) continue;

            var leagues = await league.GetLeaguesAsync(office.Id, seasonExternalId, cancellationToken);
            foreach (var spordleLeague in leagues)
            {
                var leagueExternalId = spordleLeague.Id.ToString(CultureInfo.InvariantCulture);
                var existing = await db.Leagues.FirstOrDefaultAsync(l => l.ExternalId == leagueExternalId, cancellationToken);
                if (existing is null)
                {
                    db.Leagues.Add(new()
                    {
                        DivisionId = division.Id,
                        Season = season,
                        Name = spordleLeague.Name,
                        ExternalId = leagueExternalId,
                        CreatedAt = now,
                        UpdatedAt = now,
                    });
                    imported++;
                }
                else if (existing.Name != spordleLeague.Name)
                {
                    existing.Name = spordleLeague.Name;
                    existing.UpdatedAt = now;
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return new OkObjectResult(new { imported, seasonId = seasonExternalId });
    }

    [Function("ImportTeams")]
    public async Task<IActionResult> ImportTeams(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "import/teams")] HttpRequest request,
        FunctionContext context, CancellationToken cancellationToken)
    {
        if (Denied(context, configuration) is { } denied) return denied;

        var imported = 0;
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;

        var leagues = await db.Leagues.ToListAsync(cancellationToken);
        foreach (var row in leagues)
        {
            var leagueId = int.Parse(row.ExternalId, CultureInfo.InvariantCulture);
            IReadOnlyList<LeagueTeam> teams;
            try
            {
                teams = await league.GetLeagueTeamsAsync(leagueId, cancellationToken);
            }
            catch (Exception exception) when (exception is HttpRequestException or JsonException or OperationCanceledException)
            {
                continue; // League unavailable: skip this pass, keep the import partial.
            }

            foreach (var team in teams)
            {
                var teamExternalId = team.Id.ToString(CultureInfo.InvariantCulture);
                var existing = await db.Teams.FirstOrDefaultAsync(t => t.ExternalId == teamExternalId, cancellationToken);
                if (existing is null)
                {
                    db.Teams.Add(new()
                    {
                        LeagueId = row.Id,
                        Name = team.Name,
                        ExternalId = teamExternalId,
                        CreatedAt = now,
                        UpdatedAt = now,
                    });
                    imported++;
                }
                else if (existing.Name != team.Name)
                {
                    existing.Name = team.Name;
                    existing.UpdatedAt = now;
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return new OkObjectResult(new { imported });
    }

    /// <summary>401 when unauthenticated, 403 when the caller is not an admin and open access is off; null otherwise.</summary>
    private static IActionResult? Denied(FunctionContext context, IConfiguration configuration)
    {
        var authorization = FunctionsAuthorization.IsAuthenticatedV2(context);
        if (!authorization.IsAuthenticated)
        {
            return FunctionsAuthorization.UnauthorizedResult(authorization.Reason);
        }
        if (configuration.GetSection(AuthorizationOptions.SectionName).Get<AuthorizationOptions>() is { OpenAccess: true })
        {
            return null;
        }
        return RequestUserResolver.IsAdmin(context) ? null : new StatusCodeResult(StatusCodes.Status403Forbidden);
    }
}
