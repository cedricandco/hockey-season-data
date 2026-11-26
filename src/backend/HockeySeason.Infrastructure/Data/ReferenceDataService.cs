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
/// The import is keyed on Spordle identifiers scoped to the imported division:
/// a league is the (division, season, category id) triple and a team/schedule
/// is its Spordle id inside its league. Existing rows of that division are
/// updated in place, missing ones are created, and leagues/teams that
/// disappeared from the published season are deleted together with their user
/// subscriptions (a followed team that no longer exists cannot stay in a
/// scope).
/// </para>
/// <para>
/// Nothing outside the imported division is ever modified: a Spordle category
/// id is shared by every office that plays it (13 of the 17 categories of
/// office 5956 are also published by 6193), so a league is never looked up by
/// (season, category) alone, and never re-parented. Importing one division
/// must leave the leagues and teams of every other division untouched.
/// </para>
/// <para>
/// The pruning is also conditional on a complete read: when Spordle fails to
/// answer for a category or a schedule, the run is partial and deletes
/// nothing, because the missing rows are unknown, not vanished.
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
        catch (Exception exception) when (IsUnavailable(exception))
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
        // A category or a schedule Spordle momentarily fails to publish is
        // skipped, not deleted: the run is then "incomplete" and the pruning of
        // the vanishings is disabled, so a transient failure never destroys the
        // data that is still there.
        var complete = true;
        foreach (var category in categories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var schedules = await ReadCategorySchedulesAsync(
                officeId, request.SeasonId, category, warnings, cancellationToken);
            if (schedules is null)
            {
                complete = false;
                continue;
            }
            if (schedules.Count == 0)
            {
                // Nothing to expose: a category without a published team has no
                // standings, and keeping an empty league would only add noise.
                continue;
            }

            var (teams, teamsComplete) = await ReadSchedulesTeamsAsync(schedules, warnings, cancellationToken);
            complete &= teamsComplete;
            if (teams.Count == 0)
            {
                continue;
            }

            leagues.Add(new ReferenceLeague(
                category.Id,
                category.FrenchName,
                teams,
                [.. schedules.Select(schedule => new ReferenceSchedule(schedule.Id, schedule.Name))]));
        }

        if (!complete)
        {
            warnings.Add(
                "Import partiel : Spordle n'a pas répondu pour toutes les données, "
                + "aucune ligue ni équipe n'a été supprimée.");
        }

        return await PersistAsync(request, organization.Id, organization.Name, officeId, office.Name,
            leagues, warnings, complete, cancellationToken);
    }

    /// <summary>
    /// Schedules of one category, or null with a warning when Spordle fails —
    /// null (not an empty list) so the caller knows the category was not read
    /// and must not be treated as vanished.
    /// </summary>
    private async Task<IReadOnlyList<HockeySeason.LeagueApi.Models.League>?> ReadCategorySchedulesAsync(
        int officeId,
        string seasonId,
        LeagueCategory category,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        try
        {
            return await leagueApi.GetCategorySchedulesAsync(
                officeId, seasonId, category.Id, cancellationToken);
        }
        catch (Exception exception) when (IsUnavailable(exception))
        {
            logger.LogWarning(exception, "Schedules of category {CategoryId} are unavailable.", category.Id);
            warnings.Add($"Les horaires de la ligue « {category.FrenchName} » sont indisponibles : la ligue est ignorée.");
            return null;
        }
    }

    /// <summary>
    /// Teams of one category: the union of the standings of every schedule the
    /// category is played in ("Rives", "Vallée"…), by Spordle team id. The flag
    /// is false when a schedule could not be read, so the caller knows the
    /// roster is partial and must not prune the teams it did not see.
    /// </summary>
    private async Task<(IReadOnlyList<ReferenceTeam> Teams, bool Complete)> ReadSchedulesTeamsAsync(
        IReadOnlyList<HockeySeason.LeagueApi.Models.League> schedules,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var teams = new Dictionary<int, ReferenceTeam>();
        var complete = true;
        foreach (var schedule in schedules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<HockeySeason.LeagueApi.Models.LeagueTeam> scheduleTeams;
            try
            {
                scheduleTeams = await leagueApi.GetLeagueTeamsAsync(schedule.Id, cancellationToken);
            }
            catch (Exception exception) when (IsUnavailable(exception))
            {
                logger.LogWarning(exception, "Teams of schedule {ScheduleId} are unavailable.", schedule.Id);
                warnings.Add($"Les équipes de l'horaire « {schedule.Name} » sont indisponibles.");
                complete = false;
                continue;
            }

            foreach (var team in scheduleTeams)
            {
                teams.TryAdd(team.Id, new ReferenceTeam(team.Id, team.Name));
            }
        }

        return ([.. teams.Values.OrderBy(team => team.Name, StringComparer.OrdinalIgnoreCase)], complete);
    }

    /// <summary>
    /// True for a Spordle read that failed without invalidating the whole
    /// import: an unreachable endpoint, a cancelled/timed-out attempt, or a
    /// payload whose shape is not the expected one. Those are reported as a
    /// warning so the remaining schedules still import.
    /// </summary>
    private static bool IsUnavailable(Exception exception) =>
        exception is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException;

    private async Task<ReferenceImportResult> PersistAsync(
        ReferenceImportRequest request,
        int organizationId,
        string organizationName,
        int officeId,
        string officeName,
        IReadOnlyList<ReferenceLeague> leagues,
        IReadOnlyList<string> warnings,
        bool sourceComplete,
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

        // Every row this run may touch belongs to the imported division: a
        // category id is shared by the offices that play it, so looking a league
        // up by (season, external id) alone would find — and then steal — the row
        // another division imported. The pruning further down is scoped the same
        // way.
        var divisionLeagues = await db.Leagues
            .Where(league => league.SeasonId == season.Id && league.DivisionId == office.Id)
            .Include(league => league.Teams)
            .Include(league => league.Schedules)
            .ToListAsync(cancellationToken);
        var leaguesByExternalId = divisionLeagues
            .GroupBy(league => league.ExternalId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var keptLeagueIds = new HashSet<int>();
        var keptTeamIds = new HashSet<int>();

        foreach (var source in leagues)
        {
            var leagueChanged = false;
            if (!leaguesByExternalId.TryGetValue(source.ExternalId, out var league))
            {
                // The league of this division for this season does not exist
                // yet: it is created, never borrowed from another division that
                // happens to play the same category.
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
                leaguesByExternalId[source.ExternalId] = league;
            }
            else if (league.Name != source.Name)
            {
                league.Name = source.Name;
                league.UpdatedAt = now;
                leagueChanged = true;
            }

            keptLeagueIds.Add(league.Id);

            // Schedules carry the Spordle ids the standings, games and
            // suspensions calls are keyed on, so they follow the league.
            var schedulesByExternalId = league.Schedules
                .GroupBy(schedule => schedule.ExternalId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            var keptScheduleIds = new HashSet<int>();
            foreach (var sourceSchedule in source.Schedules)
            {
                var scheduleExternalId = sourceSchedule.Id.ToString(CultureInfo.InvariantCulture);
                keptScheduleIds.Add(sourceSchedule.Id);
                if (schedulesByExternalId.TryGetValue(scheduleExternalId, out var schedule))
                {
                    if (schedule.Name != sourceSchedule.Name)
                    {
                        schedule.Name = sourceSchedule.Name;
                        schedule.UpdatedAt = now;
                        leagueChanged = true;
                    }
                    continue;
                }

                // No row of this league carries that schedule yet: it is added
                // here. A schedule row of another league is never moved — the
                // (league, external id) key allows the same Spordle schedule to
                // be mirrored by two divisions (a region republishes its child
                // offices' schedules).
                db.LeagueSchedules.Add(new LeagueSchedule
                {
                    LeagueId = league.Id,
                    ExternalId = scheduleExternalId,
                    Name = sourceSchedule.Name,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
                leagueChanged = true;
            }

            // A schedule / team that disappeared from Spordle is removed only
            // when the run read everything: an unreadable category or schedule
            // leaves the rows in place.
            if (sourceComplete)
            {
                var staleSchedules = league.Schedules
                    .Where(schedule => !IsKept(schedule.ExternalId, keptScheduleIds))
                    .ToList();
                if (staleSchedules.Count > 0)
                {
                    db.LeagueSchedules.RemoveRange(staleSchedules);
                    leagueChanged = true;
                }
            }

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

                // A team of this league whose Spordle id is not in the
                // standings is new here: it is created. A team row of another
                // league is never moved — the same Spordle team may be published
                // by two divisions, and importing one must not steal it.
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

        // Prune only when the run read the division completely. A category or a
        // schedule Spordle failed to answer for was skipped above, so what is
        // missing from `leagues` is unknown, not vanished: deleting it would
        // destroy rows that are still published. The same applies to a run that
        // read nothing at all (Spordle answered with an empty category list),
        // which is far more likely a feed problem than a wiped division.
        if (!sourceComplete || leagues.Count == 0)
        {
            return new ReferenceImportResult(
                organizationName, officeName, request.SeasonId,
                created, updated, deleted, unchanged, createdTeams, updatedTeams, deletedTeams, warnings);
        }

        // Prune only what belongs to the division being updated, for the
        // imported season: the division of the run is the only scope that may
        // lose rows. The team / league subscriptions pointing at the removed
        // rows go with them (ON DELETE CASCADE), so a user never keeps a
        // subscription to a vanished entity.
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
