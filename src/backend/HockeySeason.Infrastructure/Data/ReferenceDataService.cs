using System.Globalization;
using HockeySeason.Application;
using HockeySeason.Domain;
using HockeySeason.LeagueApi.Http;
using HockeySeason.LeagueApi.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HockeySeason.Infrastructure.Data;

/// <summary>
/// Admin use case: mirror the reference data of a Spordle association —
/// organization (region), division (office), season, leagues (categories) and
/// teams — into PostgreSQL, and reconcile it with what the LeagueApi publishes.
/// </summary>
/// <remarks>
/// <para>
/// The import is keyed on Spordle identifiers: a league is the (season,
/// category id) pair and a team is its Spordle team id. Existing rows are
/// updated in place, missing ones are created, and leagues/teams that
/// disappeared from the published season are deleted together with their user
/// subscriptions (a followed team that no longer exists cannot stay in a
/// scope). The pruning is limited to the imported season, so importing the
/// current season never wipes a past one.
/// </para>
/// <para>
/// A category may be played by several offices of the same association, and
/// <c>teams.external_id</c> is unique: an existing league or team found under
/// another office is moved, never duplicated, and the first office that
/// publishes it owns it.
/// </para>
/// </remarks>
public sealed class ReferenceDataService(
    IDbContextFactory<HockeySeasonDbContext> dbContextFactory,
    LeagueApiService leagueApi,
    ITimeProvider timeProvider,
    ILogger<ReferenceDataService> logger) : IReferenceDataService
{
    public async Task<IReadOnlyList<ReferenceOrganization>> GetOrganizationsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        // The association drop-down reads the organizations table itself; the
        // division number is typed by the administrator and matched against
        // divisions.external_id, so nothing is derived here.
        return await db.Organizations
            .AsNoTracking()
            .OrderBy(organization => organization.Name)
            .Select(organization => new ReferenceOrganization(
                organization.Id, organization.Name, organization.ExternalId ?? string.Empty))
            .ToListAsync(cancellationToken);
    }

    public async Task<ReferenceOrganization> EnsureOrganizationAsync(
        string externalId, string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var organization = await db.Organizations
            .FirstOrDefaultAsync(candidate => candidate.ExternalId == externalId, cancellationToken);
        if (organization is not null)
        {
            if (organization.Name != name)
            {
                organization.Name = name;
                organization.UpdatedAt = timeProvider.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
            }
            return new ReferenceOrganization(organization.Id, organization.Name, organization.ExternalId!);
        }

        var now = timeProvider.UtcNow;
        organization = new Organization
        {
            Name = name,
            ExternalId = externalId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Organizations.Add(organization);
        await db.SaveChangesAsync(cancellationToken);
        return new ReferenceOrganization(organization.Id, organization.Name, organization.ExternalId!);
    }

    public async Task<IReadOnlyList<ReferenceSeason>> GetSeasonsAsync(
        int officeId, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(officeId);

        var known = new List<ReferenceSeason>();
        var officeExternalId = officeId.ToString(CultureInfo.InvariantCulture);
        await using (var db = await dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            // The seasons already imported for this division.
            known.AddRange(await db.Seasons
                .AsNoTracking()
                .Where(season => season.Leagues.Any(league => league.Division.ExternalId == officeExternalId))
                .Select(season => new ReferenceSeason(season.ExternalId, season.Name, false))
                .ToListAsync(cancellationToken));
        }

        List<ReferenceSeason> published = [];
        try
        {
            published.AddRange((await leagueApi.GetSeasonsAsync(officeId, cancellationToken))
                .Select(season => new ReferenceSeason(season.SeasonId, season.Name, season.IsCurrent)));
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            // The office may publish no season: the known seasons stay usable.
            logger.LogWarning(exception, "Seasons of office {OfficeId} are unavailable.", officeId);
        }

        return [.. known
            .Concat(published)
            .GroupBy(season => season.ExternalId, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(season => season.IsCurrent).First())
            .OrderByDescending(season => season.ExternalId, StringComparer.Ordinal)];
    }

    public async Task<ReferenceImportResult> ImportAsync(
        ReferenceImportRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.OrganizationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DivisionExternalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SeasonId);
        if (!int.TryParse(request.DivisionExternalId, NumberStyles.None, CultureInfo.InvariantCulture, out var officeId)
            || officeId <= 0)
        {
            throw new InvalidOperationException(
                $"'{request.DivisionExternalId}' is not a Spordle division number.");
        }

        // The association picked in the page must exist: importing into an
        // unknown organization would create a duplicate row.
        await using var lookup = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var organization = await lookup.Organizations
            .AsNoTracking()
            .Where(candidate => candidate.Id == request.OrganizationId)
            .Select(candidate => new { candidate.Id, candidate.Name })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                $"Organization {request.OrganizationId} does not exist.");

        var office = await leagueApi.GetOfficeAsync(officeId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Spordle does not publish any office with id {officeId}.");

        var categories = await leagueApi.GetCategoriesAsync(officeId, request.SeasonId, cancellationToken);
        var warnings = new List<string>();
        var leagues = new List<ReferenceLeague>();
        foreach (var category in categories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var teams = await ReadCategoryTeamsAsync(officeId, request.SeasonId, category, warnings, cancellationToken);
            if (teams.Count == 0)
            {
                // Nothing to expose: a category without a published team has no
                // standings, and keeping an empty league would only add noise.
                continue;
            }

            leagues.Add(new ReferenceLeague(category.Id, category.FrenchName, teams));
        }

        return await PersistAsync(request, organization.Id, organization.Name, officeId, office.Name,
            leagues, warnings, cancellationToken);
    }

    /// <summary>
    /// Teams of one category: the union of the standings of every schedule the
    /// category is played in ("Rives", "Vallée"…), by Spordle team id.
    /// </summary>
    private async Task<IReadOnlyList<ReferenceTeam>> ReadCategoryTeamsAsync(
        int officeId,
        string seasonId,
        LeagueCategory category,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<HockeySeason.LeagueApi.Models.League> schedules;
        try
        {
            schedules = await leagueApi.GetCategorySchedulesAsync(
                officeId, seasonId, category.Id, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            logger.LogWarning(exception, "Schedules of category {CategoryId} are unavailable.", category.Id);
            warnings.Add($"Les horaires de la ligue « {category.FrenchName} » sont indisponibles : la ligue est ignorée.");
            return [];
        }

        var teams = new Dictionary<int, ReferenceTeam>();
        foreach (var schedule in schedules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<HockeySeason.LeagueApi.Models.LeagueTeam> scheduleTeams;
            try
            {
                scheduleTeams = await leagueApi.GetLeagueTeamsAsync(schedule.Id, cancellationToken);
            }
            catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
            {
                logger.LogWarning(exception, "Teams of schedule {ScheduleId} are unavailable.", schedule.Id);
                warnings.Add($"Les équipes de l'horaire « {schedule.Name} » sont indisponibles.");
                continue;
            }

            foreach (var team in scheduleTeams)
            {
                teams.TryAdd(team.Id, new ReferenceTeam(team.Id, team.Name));
            }
        }

        return [.. teams.Values.OrderBy(team => team.Name, StringComparer.OrdinalIgnoreCase)];
    }

    private async Task<ReferenceImportResult> PersistAsync(
        ReferenceImportRequest request,
        int organizationId,
        string organizationName,
        int officeId,
        string officeName,
        IReadOnlyList<ReferenceLeague> leagues,
        IReadOnlyList<string> warnings,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.UtcNow;
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var organization = await db.Organizations
            .FirstOrDefaultAsync(o => o.Id == organizationId, cancellationToken)
            ?? throw new InvalidOperationException($"Organization {organizationId} does not exist.");
        if (organization.Name != organizationName)
        {
            organization.Name = organizationName;
            organization.UpdatedAt = now;
        }

        // The division is identified by its external_id, the number typed in
        // the page; it is created under the selected association when new.
        var officeExternalId = officeId.ToString(CultureInfo.InvariantCulture);
        var office = await db.Divisions
            .FirstOrDefaultAsync(d => d.ExternalId == officeExternalId, cancellationToken);
        if (office is null)
        {
            office = new Division
            {
                Organization = organization,
                Name = officeName,
                ExternalId = officeExternalId,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.Divisions.Add(office);
        }
        else if (office.Name != officeName || office.OrganizationId != organization.Id)
        {
            office.Name = officeName;
            office.OrganizationId = organization.Id;
            office.UpdatedAt = now;
        }

        var season = await db.Seasons
            .FirstOrDefaultAsync(s => s.ExternalId == request.SeasonId, cancellationToken);
        if (season is null)
        {
            season = new Season
            {
                Name = request.SeasonId,
                ExternalId = request.SeasonId,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.Seasons.Add(season);
        }

        await db.SaveChangesAsync(cancellationToken);

        var created = new List<string>();
        var updated = new List<string>();
        var unchanged = new List<string>();
        var deleted = new List<string>();
        var createdTeams = 0;
        var updatedTeams = 0;
        var deletedTeams = 0;

        // A category may be played by several divisions of the association, and
        // the same season may hold leagues of other associations: existing rows
        // are looked up by association for the re-parenting below, because the
        // (season, external id) index is unique. The pruning, further down, is
        // strictly division-scoped.
        var associationLeagues = await db.Leagues
            .Where(league => league.SeasonId == season.Id
                && league.Division.OrganizationId == organization.Id)
            .Include(league => league.Teams)
            .ToListAsync(cancellationToken);
        var leaguesByExternalId = associationLeagues
            .GroupBy(league => league.ExternalId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var keptLeagueIds = new HashSet<int>();
        var keptTeamIds = new HashSet<int>();

        foreach (var source in leagues)
        {
            var leagueChanged = false;
            if (!leaguesByExternalId.TryGetValue(source.ExternalId, out var league))
            {
                // Same season, another association (or another office of it):
                // the row is re-parented rather than duplicated.
                league = await db.Leagues
                    .FirstOrDefaultAsync(candidate => candidate.SeasonId == season.Id
                        && candidate.ExternalId == source.ExternalId, cancellationToken);

                if (league is null)
                {
                    league = new League
                    {
                        DivisionId = office.Id,
                        SeasonId = season.Id,
                        Name = source.Name,
                        ExternalId = source.ExternalId,
                        CreatedAt = now,
                        UpdatedAt = now,
                    };
                    db.Leagues.Add(league);
                    await db.SaveChangesAsync(cancellationToken);
                    created.Add(source.Name);
                }
                else
                {
                    league.DivisionId = office.Id;
                    league.Name = source.Name;
                    league.UpdatedAt = now;
                    updated.Add(source.Name);
                    await db.SaveChangesAsync(cancellationToken);
                }

                leaguesByExternalId[source.ExternalId] = league;
            }
            else if (league.Name != source.Name || league.DivisionId != office.Id)
            {
                league.Name = source.Name;
                league.DivisionId = office.Id;
                league.UpdatedAt = now;
                leagueChanged = true;
            }

            keptLeagueIds.Add(league.Id);

            var teamsByExternalId = league.Teams
                .GroupBy(team => team.ExternalId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            foreach (var sourceTeam in source.Teams)
            {
                var externalId = sourceTeam.Id.ToString(CultureInfo.InvariantCulture);
                keptTeamIds.Add(sourceTeam.Id);
                if (teamsByExternalId.TryGetValue(externalId, out var team))
                {
                    if (team.Name != sourceTeam.Name)
                    {
                        team.Name = sourceTeam.Name;
                        team.UpdatedAt = now;
                        updatedTeams++;
                    }
                    continue;
                }

                // The team may already live under another league of the
                // season (a category played by several offices, or a team
                // promoted between categories): its Spordle id is unique, so
                // the existing row is re-parented rather than duplicated.
                var relocated = await db.Teams
                    .FirstOrDefaultAsync(candidate => candidate.ExternalId == externalId, cancellationToken);
                if (relocated is null)
                {
                    db.Teams.Add(new Team
                    {
                        LeagueId = league.Id,
                        Name = sourceTeam.Name,
                        ExternalId = externalId,
                        CreatedAt = now,
                        UpdatedAt = now,
                    });
                    createdTeams++;
                }
                else
                {
                    relocated.LeagueId = league.Id;
                    relocated.Name = sourceTeam.Name;
                    relocated.UpdatedAt = now;
                    updatedTeams++;
                }
            }

            if (leagueChanged)
            {
                updated.Add(source.Name);
            }
            else if (!created.Contains(source.Name) && !updated.Contains(source.Name))
            {
                unchanged.Add(source.Name);
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        // Prune only what belongs to the division being updated, for the
        // imported season. A league of a sibling division, another season or
        // another association is never touched, even when the same category id
        // is imported here. The team / league subscriptions pointing at the
        // removed rows go with them (ON DELETE CASCADE), so a user never keeps
        // a subscription to a vanished entity.
        var divisionLeagues = await db.Leagues
            .Where(league => league.SeasonId == season.Id
                && league.DivisionId == office.Id
                && league.Division.OrganizationId == organization.Id)
            .ToListAsync(cancellationToken);

        var staleLeagues = divisionLeagues.Where(league => !keptLeagueIds.Contains(league.Id)).ToList();
        if (staleLeagues.Count > 0)
        {
            deleted.AddRange(staleLeagues.Select(league => league.Name));
            var staleLeagueIds = staleLeagues.Select(league => league.Id).ToList();
            var staleLeagueTeams = await db.Teams
                .Where(team => staleLeagueIds.Contains(team.LeagueId))
                .ToListAsync(cancellationToken);
            db.Teams.RemoveRange(staleLeagueTeams);
            deletedTeams += staleLeagueTeams.Count;
            db.Leagues.RemoveRange(staleLeagues);
        }

        // Teams of the division and season that Spordle no longer publishes.
        // Leagues removed just above are skipped: they are already reported
        // with their own name and their teams are already counted.
        var remainingLeagueIds = divisionLeagues
            .Where(league => keptLeagueIds.Contains(league.Id))
            .Select(league => league.Id)
            .ToList();
        var remainingTeams = remainingLeagueIds.Count == 0
            ? new List<Team>()
            : await db.Teams
                .Where(team => remainingLeagueIds.Contains(team.LeagueId))
                .ToListAsync(cancellationToken);
        // The Spordle ids are text: the membership test runs in memory on the
        // few hundred teams of one division.
        var staleTeams = remainingTeams.Where(team => !IsKept(team.ExternalId, keptTeamIds)).ToList();
        if (staleTeams.Count > 0)
        {
            db.Teams.RemoveRange(staleTeams);
            deletedTeams += staleTeams.Count;
        }

        await db.SaveChangesAsync(cancellationToken);

        return new ReferenceImportResult(
            organizationName, officeName, request.SeasonId,
            created, updated, deleted, unchanged, createdTeams, updatedTeams, deletedTeams, warnings);

        static bool IsKept(string externalId, HashSet<int> keptIds) =>
            int.TryParse(externalId, CultureInfo.InvariantCulture, out var id) && keptIds.Contains(id);
    }
}
