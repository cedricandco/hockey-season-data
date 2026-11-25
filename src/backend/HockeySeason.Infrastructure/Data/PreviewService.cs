using System.Globalization;
using HockeySeason.Application;
using Microsoft.EntityFrameworkCore;

namespace HockeySeason.Infrastructure.Data;

/// <summary>
/// Read-only preview of the reference data, used by the landing page of a
/// visitor who has no account yet: the league team list and the detail of one
/// picked team.
/// </summary>
/// <remarks>
/// Unlike <see cref="CallerDataScopeService"/> it enforces no follows — the
/// preview is anonymous by design — but it still resolves the published
/// Spordle schedules from the database so the live detail call uses stored
/// schedule ids rather than an id typed by the client. The three attempts a
/// visitor gets are counted by the browser, not here.
/// </remarks>
public sealed class PreviewService(
    IDbContextFactory<HockeySeasonDbContext> dbContextFactory) : IPreviewService
{
    public async Task<IReadOnlyList<PreviewTeam>> GetTeamsAsync(
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

        return await db.Teams
            .AsNoTracking()
            .Where(team => team.LeagueId == latestSeason.Value)
            .OrderBy(team => team.Name)
            .Select(team => new PreviewTeam(team.Id, team.Name))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<SelectableTeam?> GetTeamAsync(int teamId, CancellationToken cancellationToken = default)
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
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (team is null)
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

    public async Task<IReadOnlyList<PreviewTeamLink>> GetLeagueTeamLinksAsync(
        int leagueId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var teams = await db.Teams
            .AsNoTracking()
            .Where(team => team.LeagueId == leagueId)
            .Select(team => new { team.Id, team.ExternalId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. teams
            .Select(team => (team.Id, ExternalId: ParsePositive(team.ExternalId)))
            .Where(item => item.ExternalId > 0)
            .Select(item => new PreviewTeamLink(item.Id, item.ExternalId))
            .OrderBy(link => link.ExternalId)];
    }

    private static IReadOnlyList<FollowedSchedule> ParseSchedules(IReadOnlyList<string> externalIds) =>
        [.. externalIds
            .Select(ParsePositive)
            .Where(id => id > 0)
            .Select(id => new FollowedSchedule(id, $"Schedule {id}"))
            .OrderBy(schedule => schedule.Id)];

    private static int ParsePositive(string value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 ? id : 0;
}
