using System.Globalization;
using HockeySeason.Application;
using Microsoft.EntityFrameworkCore;

namespace HockeySeason.Infrastructure.Data;

/// <summary>
/// Resolves the reference data a caller follows, from the database:
/// association → division → league → team.
/// </summary>
/// <remarks>
/// The follows come from <c>team_subscriptions</c>, <c>league_subscriptions</c>
/// and <c>organization_subscriptions</c>. Following a team opens its whole
/// league — every team of the league is readable (decision 2026-11-24), the
/// division and association remaining read-only parents; a followed league
/// exposes all of its teams; a followed association exposes all of its
/// divisions and leagues. Nothing outside that union is returned, which is the
/// "only what I follow" rule shared by the weekly report, the team page and the
/// MCP tools.
/// </remarks>
public sealed class CallerDataScopeService(
    IDbContextFactory<HockeySeasonDbContext> dbContextFactory) : ICallerDataScopeService
{
    public async Task<CallerDataScope> GetAsync(
        int userId, bool unrestricted = false, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await GetAsync(db, userId, unrestricted, cancellationToken).ConfigureAwait(false);
    }

    public async Task<CallerDataScope> GetForSubjectAsync(
        string? auth0Subject, bool unrestricted = false, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        if (unrestricted)
        {
            return await BuildAsync(db, null, cancellationToken).ConfigureAwait(false);
        }

        // An anonymous or not-yet-provisioned caller follows nothing.
        var userId = string.IsNullOrWhiteSpace(auth0Subject)
            ? null
            : await db.Users
                .AsNoTracking()
                .Where(user => user.Auth0UserId == auth0Subject)
                .Select(user => (int?)user.Id)
                .SingleOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

        return userId is null
            ? CallerDataScope.NothingFollowed()
            : await GetAsync(db, userId.Value, unrestricted: false, cancellationToken).ConfigureAwait(false);
    }
    public async Task<IReadOnlyList<FollowedTeam>> GetFollowedTeamsAsync(
        int userId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        // The team page and the weekly report are about the teams the caller
        // follows, not about what open access unlocks: this query reads
        // team_subscriptions only, whatever Authorization:OpenAccess says.
        var teams = await db.TeamSubscriptions
            .AsNoTracking()
            .Where(subscription => subscription.UserId == userId)
            .Select(subscription => new
            {
                Id = subscription.Team.Id,
                subscription.Team.ExternalId,
                subscription.Team.Name,
                LeagueId = subscription.Team.League.Id,
                LeagueName = subscription.Team.League.Name,
                DivisionId = subscription.Team.League.Division.Id,
                DivisionName = subscription.Team.League.Division.Name,
                AssociationId = subscription.Team.League.Division.Organization.Id,
                AssociationName = subscription.Team.League.Division.Organization.Name,
                Schedules = subscription.Team.League.Schedules
                    .Select(schedule => new { schedule.ExternalId, schedule.Name })
                    .ToList(),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. teams
            // A team without a Spordle id, or a league without a published
            // schedule, has no live figures to read: it is not listed, because
            // selecting it could only fail.
            .Select(team => (Team: team, ExternalId: ParsePositive(team.ExternalId)))
            .Where(item => item.ExternalId > 0)
            .Select(item => new FollowedTeam(
                item.Team.Id,
                item.ExternalId,
                item.Team.Name,
                item.Team.LeagueId,
                item.Team.LeagueName,
                [.. item.Team.Schedules
                    .Select(schedule => (schedule.Name, Id: ParsePositive(schedule.ExternalId)))
                    .Where(schedule => schedule.Id > 0)
                    .OrderBy(schedule => schedule.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(schedule => new FollowedSchedule(schedule.Id, schedule.Name))],
                item.Team.DivisionId,
                item.Team.DivisionName,
                item.Team.AssociationId,
                item.Team.AssociationName))
            .OrderBy(team => team.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(team => team.Id)];
    }

    /// <summary>
    /// Team the team page can open by database id: a followed team, or any
    /// team of a league that contains one (a followed team opens its whole
    /// league). Null outside those leagues — the page answers 403 — or when
    /// the team carries no Spordle id.
    /// </summary>
    public async Task<SelectableTeam?> GetSelectableTeamAsync(
        int userId, int teamId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var team = await db.Teams
            .AsNoTracking()
            .Where(candidate => candidate.Id == teamId)
            .Select(candidate => new
            {
                candidate.Id,
                candidate.Name,
                candidate.ExternalId,
                candidate.LeagueId,
                LeagueName = candidate.League.Name,
                ScheduleExternalIds = candidate.League.Schedules
                    .Select(schedule => schedule.ExternalId)
                    .ToList(),
            })
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (team is null || !await LeagueIsOpenAsync(db, userId, team.LeagueId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var externalId = ParsePositive(team.ExternalId);
        if (externalId == 0)
        {
            // A team without a Spordle id has no live figures to read.
            return null;
        }

        return new SelectableTeam(
            team.Id,
            externalId,
            team.Name,
            team.LeagueId,
            team.LeagueName,
            ParseSchedules(team.ScheduleExternalIds));
    }

    /// <summary>
    /// Every team of one league when one of its teams is followed (or the
    /// league itself), else nothing: following a team opens its whole league.
    /// Ids are database ids, the team page's vocabulary.
    /// </summary>
    public async Task<IReadOnlyList<SelectableTeam>> GetLeagueTeamsAsync(
        int userId, int leagueId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (!await LeagueIsOpenAsync(db, userId, leagueId, cancellationToken).ConfigureAwait(false))
        {
            // A league without a follow is not listed: its teams can neither be
            // reached nor guessed.
            return [];
        }

        var scheduleExternalIds = await db.LeagueSchedules
            .AsNoTracking()
            .Where(schedule => schedule.LeagueId == leagueId)
            .Select(schedule => schedule.ExternalId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var schedules = ParseSchedules(scheduleExternalIds);

        var teams = await db.Teams
            .AsNoTracking()
            .Where(team => team.LeagueId == leagueId)
            .Join(db.Leagues.Where(league => league.Id == leagueId),
                team => team.LeagueId, league => league.Id,
                (team, league) => new { team.Id, team.Name, team.ExternalId, LeagueId = league.Id, LeagueName = league.Name })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return [.. teams
            // A team without a Spordle id has no live figures to read: it is
            // hidden, because selecting it could only fail.
            .Select(team => (Team: team, ExternalId: ParsePositive(team.ExternalId)))
            .Where(item => item.ExternalId > 0)
            .Select(item => new SelectableTeam(
                item.Team.Id, item.ExternalId, item.Team.Name,
                item.Team.LeagueId, item.Team.LeagueName, schedules))
            .OrderBy(team => team.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(team => team.Id)];
    }

    /// <summary>
    /// Whether the league is readable as a whole: followed directly, holding a
    /// team the caller follows, or inside a followed association.
    /// </summary>
    private static async Task<bool> LeagueIsOpenAsync(
        HockeySeasonDbContext db, int userId, int leagueId, CancellationToken cancellationToken)
    {
        // The league is open when any subscription of the caller points at it:
        // the league itself, one of its teams, or an organization of the
        // division that owns it (the whole-league rule of BuildAsync).
        var followedTeamIds = await db.TeamSubscriptions
            .AsNoTracking()
            .Where(subscription => subscription.UserId == userId)
            .Select(subscription => subscription.TeamId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (await db.LeagueSubscriptions
            .AsNoTracking()
            .AnyAsync(subscription => subscription.UserId == userId && subscription.LeagueId == leagueId,
                cancellationToken).ConfigureAwait(false))
        {
            return true;
        }

        var league = await db.Leagues
            .AsNoTracking()
            .Where(item => item.Id == leagueId)
            .Select(item => new { item.Division.OrganizationId, TeamIds = item.Teams.Select(team => team.Id).ToList() })
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (league is null)
        {
            return false;
        }

        return league.TeamIds.Any(teamId => followedTeamIds.Contains(teamId))
            || await db.OrganizationSubscriptions
                .AsNoTracking()
                .AnyAsync(subscription => subscription.UserId == userId
                    && subscription.OrganizationId == league.OrganizationId,
                    cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Parses schedule ids, dropping the unparsable ones; the published name is
    /// resolved by the real schedule rows, so the page shows the id alone.
    /// </summary>
    private static IReadOnlyList<FollowedSchedule> ParseSchedules(IReadOnlyList<string> externalIds) =>
        [.. externalIds
            .Select(ParsePositive)
            .Where(id => id > 0)
            .Select(id => new FollowedSchedule(id, $"Schedule {id}"))
            .OrderBy(schedule => schedule.Id)];

    private static int ParsePositive(string value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 ? id : 0;

    private static async Task<CallerDataScope> GetAsync(
        HockeySeasonDbContext db, int userId, bool unrestricted, CancellationToken cancellationToken)
    {
        if (unrestricted)
        {
            return await BuildAsync(db, null, cancellationToken).ConfigureAwait(false);
        }

        var followedTeams = await db.TeamSubscriptions
            .AsNoTracking()
            .Where(subscription => subscription.UserId == userId)
            .Select(subscription => subscription.Team.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var followedLeagues = await db.LeagueSubscriptions
            .AsNoTracking()
            .Where(subscription => subscription.UserId == userId)
            .Select(subscription => subscription.League.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var followedOrganizations = await db.OrganizationSubscriptions
            .AsNoTracking()
            .Where(subscription => subscription.UserId == userId)
            .Select(subscription => subscription.OrganizationId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return await BuildAsync(
            db,
            new(followedTeams, followedLeagues, followedOrganizations),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Follows of the caller; null means unrestricted.</summary>
    private sealed record Follows(
        IReadOnlyList<int> TeamIds,
        IReadOnlyList<int> LeagueIds,
        IReadOnlyList<int> OrganizationIds);

    /// <summary>
    /// Builds the catalogue. <paramref name="follows"/> null means
    /// "unrestricted": the whole reference data is returned. A non-null value
    /// always restricts, even when every list is empty (the caller follows
    /// nothing and must see nothing).
    /// </summary>
    private static async Task<CallerDataScope> BuildAsync(
        HockeySeasonDbContext db, Follows? follows, CancellationToken cancellationToken)
    {
        var restricted = follows is not null;
        var teamIds = follows?.TeamIds ?? [];
        var leagueIds = follows?.LeagueIds ?? [];
        var organizationIds = follows?.OrganizationIds ?? [];

        // Leagues reachable from the follows: followed directly, owning a
        // followed team, or inside a followed association. A followed team
        // opens its whole league: its sibling teams are readable as well.
        var leagueKeys = await db.Leagues
            .AsNoTracking()
            .Where(league => !restricted
                || leagueIds.Contains(league.Id)
                || league.Teams.Any(team => teamIds.Contains(team.Id))
                || organizationIds.Contains(league.Division.OrganizationId))
            .Select(league => new
            {
                league.Id,
                league.ExternalId,
                league.Name,
                SeasonExternalId = league.Season.ExternalId,
                DivisionExternalId = league.Division.ExternalId,
                DivisionName = league.Division.Name,
                OrganizationId = league.Division.OrganizationId,
                OrganizationName = league.Division.Organization.Name,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (leagueKeys.Count == 0)
        {
            // Restricted callers follow nothing: an empty catalogue, never the
            // whole reference data.
            return follows is null
                ? CallerDataScope.Unrestricted([], [], [], [])
                : CallerDataScope.NothingFollowed();
        }

        // A league is visible as a whole when it is followed directly, is
        // inside a followed association, or holds a followed team: following a
        // team opens its league, so all its teams and schedules are readable —
        // the user needs them to see their team's opponents. That is exactly
        // the leagueKeys filter above, so the teams below are matched on the
        // reachable league ids and not on the category guid, which Spordle
        // shares across seasons (a league is the (season, category) pair).
        var reachableLeagueIds = leagueKeys.Select(league => league.Id).ToList();

        var schedules = await db.LeagueSchedules
            .AsNoTracking()
            .Where(schedule => reachableLeagueIds.Contains(schedule.LeagueId))
            .Select(schedule => new { schedule.LeagueId, schedule.ExternalId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var schedulesByLeague = schedules
            .Select(schedule => (schedule.LeagueId, Id: int.TryParse(
                schedule.ExternalId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : 0))
            .Where(item => item.Id > 0)
            .GroupBy(item => item.LeagueId)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Id).ToList());

        // Every team of the leagues opened above: the followed teams, their
        // opponents of the same league, and the teams of followed leagues and
        // associations (decision 2026-11-24).
        var teams = await db.Teams
            .AsNoTracking()
            .Where(team => reachableLeagueIds.Contains(team.LeagueId))
            .Select(team => new
            {
                team.Id,
                team.Name,
                team.ExternalId,
                LeagueExternalId = team.League.ExternalId,
                team.LeagueId,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var divisions = new Dictionary<int, ScopedDivision>();
        var associations = new Dictionary<int, ScopedAssociation>();
        var leagues = new List<ScopedLeague>();
        foreach (var league in leagueKeys)
        {
            if (!int.TryParse(league.DivisionExternalId, NumberStyles.None, CultureInfo.InvariantCulture, out var divisionId))
            {
                // A division without a Spordle number cannot lead to a Spordle call.
                continue;
            }

            associations.TryAdd(league.OrganizationId, new ScopedAssociation(league.OrganizationId, league.OrganizationName));
            divisions.TryAdd(divisionId, new ScopedDivision(divisionId, league.DivisionName, league.OrganizationId));
            leagues.Add(new ScopedLeague(
                league.Id,
                league.ExternalId,
                league.Name,
                league.SeasonExternalId,
                divisionId,
                schedulesByLeague.TryGetValue(league.Id, out var ids) ? ids : []));
        }

        var scopedTeams = teams
            .Select(team => int.TryParse(team.ExternalId, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                ? new ScopedTeam(id, team.Name, team.LeagueExternalId,
                    schedulesByLeague.TryGetValue(team.LeagueId, out var ids) && ids.Count > 0 ? ids[0] : null)
                : null)
            .OfType<ScopedTeam>()
            .OrderBy(team => team.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new CallerDataScope(
            restricted,
            [.. associations.Values.OrderBy(association => association.Name, StringComparer.OrdinalIgnoreCase)],
            [.. divisions.Values.OrderBy(division => division.Name, StringComparer.OrdinalIgnoreCase)],
            [.. leagues.OrderBy(league => league.Name, StringComparer.OrdinalIgnoreCase)],
            scopedTeams);
    }
}
