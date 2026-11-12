using System.Collections.Immutable;
using HockeySeason.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace HockeySeason.Infrastructure.Data;

/// <summary>
/// Computes the visible data scope of a user: the union of team subscriptions
/// (direct), the leagues owning those teams, league subscriptions, and every
/// league of the organizations the user follows (PRESIDENT scope, decision
/// A2). Spordle (external) ids are returned — the vocabulary the frontend and
/// the public league API use. Results are cached 60 seconds per user.
/// </summary>
public sealed class ScopeService(
    IDbContextFactory<HockeySeasonDbContext> dbContextFactory,
    IMemoryCache cache) : IScopeService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

    public async Task<UserScope> GetScopeAsync(int userId, CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue(CacheKey(userId), out UserScope? cached) && cached is not null)
        {
            return cached;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var teamIds = new HashSet<int>();
        var leagueIds = new HashSet<int>();
        var officeIds = new HashSet<int>();

        // Direct team subscriptions → their owning leagues.
        var followedTeams = await db.TeamSubscriptions
            .AsNoTracking()
            .Where(subscription => subscription.UserId == userId)
            .Select(subscription => new
            {
                TeamExternalId = subscription.Team.ExternalId,
                LeagueExternalId = subscription.Team.League.ExternalId,
                DivisionExternalId = subscription.Team.League.Division.ExternalId,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var team in followedTeams)
        {
            teamIds.Add(int.Parse(team.TeamExternalId));
            leagueIds.Add(int.Parse(team.LeagueExternalId));
            officeIds.Add(int.Parse(team.DivisionExternalId));
        }

        // Direct league subscriptions.
        var followedLeagues = await db.LeagueSubscriptions
            .AsNoTracking()
            .Where(subscription => subscription.UserId == userId)
            .Select(subscription => new
            {
                LeagueExternalId = subscription.League.ExternalId,
                DivisionExternalId = subscription.League.Division.ExternalId,
                TeamExternalIds = subscription.League.Teams.Select(team => team.ExternalId).ToList(),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var league in followedLeagues)
        {
            leagueIds.Add(int.Parse(league.LeagueExternalId));
            officeIds.Add(int.Parse(league.DivisionExternalId));
            foreach (var teamExternalId in league.TeamExternalIds)
            {
                teamIds.Add(int.Parse(teamExternalId));
            }
        }

        // Organization subscriptions (PRESIDENT/A2): every league of the org.
        var followedOrganizations = await db.OrganizationSubscriptions
            .AsNoTracking()
            .Where(subscription => subscription.UserId == userId)
            .Select(subscription => new
            {
                LeagueExternalIds = subscription.Organization.Divisions
                    .SelectMany(division => division.Leagues)
                    .Select(league => league.ExternalId)
                    .ToList(),
                DivisionExternalIds = subscription.Organization.Divisions
                    .Select(division => division.ExternalId)
                    .ToList(),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var organization in followedOrganizations)
        {
            foreach (var leagueExternalId in organization.LeagueExternalIds)
            {
                leagueIds.Add(int.Parse(leagueExternalId));
            }
            foreach (var divisionExternalId in organization.DivisionExternalIds)
            {
                officeIds.Add(int.Parse(divisionExternalId));
            }
        }

        var scope = new UserScope(
            teamIds.ToImmutableHashSet(),
            leagueIds.ToImmutableHashSet(),
            officeIds.ToImmutableHashSet());
        cache.Set(CacheKey(userId), scope, CacheDuration);
        return scope;
    }

    private static string CacheKey(int userId) => $"scope:user:{userId}";
}
