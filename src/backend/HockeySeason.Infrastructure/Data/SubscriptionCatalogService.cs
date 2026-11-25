using System.Globalization;
using HockeySeason.Application;
using Microsoft.EntityFrameworkCore;

namespace HockeySeason.Infrastructure.Data;

/// <summary>
/// Read-only navigation of the reference data mirrored from Spordle, used by
/// the "follow a team" selector of the configuration page: association →
/// division → league → team.
/// </summary>
/// <remarks>
/// Every level returns the identifier the next one expects: the association id
/// is the local <c>organizations.id</c>, the division id is the Spordle office
/// number (<c>divisions.external_id</c>), the league id is the Spordle category
/// guid (<c>leagues.external_id</c>) and the team id is the Spordle team id
/// (<c>teams.external_id</c>) — the same values the subscription endpoints
/// store.
/// </remarks>
public sealed class SubscriptionCatalogService(
    IDbContextFactory<HockeySeasonDbContext> dbContextFactory) : ISubscriptionCatalogService
{
    public async Task<IReadOnlyList<SubscriptionAssociation>> GetAssociationsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        // Only associations that own a division are useful: an association
        // without imported data would lead to empty drop-downs.
        return await db.Organizations
            .AsNoTracking()
            .Where(organization => organization.Divisions.Any())
            .OrderBy(organization => organization.Name)
            .Select(organization => new SubscriptionAssociation(organization.Id, organization.Name))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SubscriptionDivision>> GetDivisionsAsync(
        int associationId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var divisions = await db.Divisions
            .AsNoTracking()
            .Where(division => division.OrganizationId == associationId)
            .OrderBy(division => division.Name)
            .Select(division => new { division.Name, division.ExternalId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // The selector talks Spordle numbers: a row whose external id is not
        // one cannot be navigated to and is skipped.
        return [.. divisions
            .Select(division => int.TryParse(division.ExternalId, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                ? new SubscriptionDivision(id, division.Name)
                : null)
            .OfType<SubscriptionDivision>()];
    }

    public async Task<SubscriptionLeagueOptions> GetLeaguesAsync(
        int divisionId, string? seasonId = null, CancellationToken cancellationToken = default)
    {
        var divisionExternalId = divisionId.ToString(CultureInfo.InvariantCulture);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var seasons = await db.Leagues
            .AsNoTracking()
            .Where(league => league.Division.ExternalId == divisionExternalId)
            .Select(league => new { league.SeasonId, league.Season.ExternalId })
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (seasons.Count == 0)
        {
            return new(null, []);
        }

        // The requested season when the division publishes it, the latest
        // imported one otherwise ("2026-27" sorts after "2025-26").
        var selected = seasons.FirstOrDefault(season => season.ExternalId == seasonId)
            ?? seasons.OrderByDescending(season => season.ExternalId, StringComparer.Ordinal).First();

        var leagues = await db.Leagues
            .AsNoTracking()
            .Where(league => league.Division.ExternalId == divisionExternalId && league.SeasonId == selected.SeasonId)
            .OrderBy(league => league.Name)
            .Select(league => new SubscriptionLeague(league.ExternalId, league.Name, selected.ExternalId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new(selected.ExternalId, leagues);
    }

    public async Task<IReadOnlyList<SubscriptionTeam>> GetTeamsAsync(
        string leagueId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leagueId);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        // A category guid is shared by every season: the teams of the latest
        // one are the current roster of the league.
        var latestSeason = await db.Leagues
            .AsNoTracking()
            .Where(league => league.ExternalId == leagueId)
            .OrderByDescending(league => league.Season.ExternalId)
            .Select(league => (int?)league.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (latestSeason is null)
        {
            return [];
        }

        var teams = await db.Teams
            .AsNoTracking()
            .Where(team => team.LeagueId == latestSeason.Value)
            .OrderBy(team => team.Name)
            .Select(team => new { team.Name, team.ExternalId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. teams
            .Select(team => int.TryParse(team.ExternalId, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                ? new SubscriptionTeam(id, team.Name)
                : null)
            .OfType<SubscriptionTeam>()];
    }

    /// <summary>
    /// Teams of one association whose name starts with the base name, across
    /// every league of its latest imported season. The name comparison happens
    /// in memory (<see cref="TeamNameMatcher"/>) because it is accent- and
    /// case-insensitive and word-aware, which a provider-side LIKE cannot
    /// express; only the reference rows of the association are read.
    /// </summary>
    public async Task<PresidentTeamOptions> GetTeamsByBaseNameAsync(
        int associationId, string baseName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baseName);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var association = await db.Organizations
            .AsNoTracking()
            .Where(organization => organization.Id == associationId)
            .Select(organization => new { organization.Name })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (association is null)
        {
            return new(associationId, string.Empty, baseName.Trim(), []);
        }

        // The latest season of the association: a team promoted between
        // categories keeps one Spordle id, so each team appears once even when
        // several divisions of the association publish the same base name.
        var seasonExternalId = await db.Leagues
            .AsNoTracking()
            .Where(league => league.Division.OrganizationId == associationId)
            .OrderByDescending(league => league.Season.ExternalId)
            .Select(league => league.Season.ExternalId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (seasonExternalId is null)
        {
            return new(associationId, association.Name, baseName.Trim(), []);
        }

        var candidates = await db.Teams
            .AsNoTracking()
            .Where(team => team.League.Division.OrganizationId == associationId
                && team.League.Season.ExternalId == seasonExternalId)
            .Select(team => new
            {
                team.Name,
                team.ExternalId,
                LeagueName = team.League.Name,
                DivisionName = team.League.Division.Name,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var teams = candidates
            .Where(team => TeamNameMatcher.Matches(team.Name, baseName))
            .Select(team => (Team: team, ExternalId: int.TryParse(
                team.ExternalId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : 0))
            .Where(item => item.ExternalId > 0)
            .DistinctBy(item => item.ExternalId)
            .OrderBy(item => item.Team.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.ExternalId)
            .Select(item => new PresidentTeam(item.ExternalId, item.Team.Name, item.Team.LeagueName, item.Team.DivisionName))
            .ToList();

        return new(associationId, association.Name, baseName.Trim(), teams);
    }
}
