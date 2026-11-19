using HockeySeason.Application;
using HockeySeason.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HockeySeason.Infrastructure.Data;

/// <summary>
/// Subscription and settings use case over the PostgreSQL persistence model.
/// Business rules implemented here: licence limits from the seeded roles
/// (COACH 5 teams, DIRECTOR 5 leagues), the 15-day team subscription lock and
/// the admin bypass, and upsert semantics for visual settings. While
/// <see cref="AuthorizationOptions.OpenAccess"/> is on, licence limits and the
/// 15-day lock are lifted: every authenticated caller follows anything.
/// </summary>
public sealed class UserService(
    IDbContextFactory<HockeySeasonDbContext> dbContextFactory,
    ITimeProvider timeProvider,
    IOptions<AuthorizationOptions> authorization) : IUserService
{
    private static readonly TimeSpan TeamLockDuration = TimeSpan.FromDays(15);

    private const int CoachRoleId = 1;
    private const int DirectorRoleId = 2;
    private const int PresidentRoleId = 3;

    /// <summary>Follow kind names returned by <see cref="GetLimitsAsync"/>.</summary>
    public const string TeamKind = "team";
    public const string LeagueKind = "league";
    public const string OrganizationKind = "organization";

    /// <summary>Licence limits and locks are lifted while open access is on.</summary>
    private bool OpenAccess => authorization.Value.OpenAccess;

    public async Task<CurrentUserProfile?> GetProfileAsync(int userId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var user = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new
            {
                u.Id,
                u.Auth0UserId,
                u.Email,
                u.FirstName,
                u.LastName,
                u.IsAdmin,
                Roles = u.Roles.Select(ur => new { ur.RoleId, ur.Role.Code, ur.Role.Name, ur.Role.MaxFollowedEntities }).ToList(),
                u.Setting!.PrimaryColor,
                u.Setting.SecondaryColor,
                u.Setting.Extra,
                TeamCount = u.TeamSubscriptions.Count,
            })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return user is null ? null : new(
            user.Id,
            user.Auth0UserId,
            user.Email,
            user.FirstName,
            user.LastName,
            user.IsAdmin,
            [.. user.Roles.Select(role => new UserRoleSummary(
                role.RoleId, (UserRoleCode)role.RoleId, role.Name, role.MaxFollowedEntities))],
            user.PrimaryColor,
            user.SecondaryColor,
            user.Extra,
            user.TeamCount);
    }

    public async Task<UserSubscriptions> GetSubscriptionsAsync(int userId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var teams = await db.TeamSubscriptions
            .AsNoTracking()
            .Where(subscription => subscription.UserId == userId)
            .OrderBy(subscription => subscription.CreatedAt)
            .Select(subscription => new UserTeamSubscription(
                int.Parse(subscription.Team.ExternalId),
                subscription.Team.Name,
                subscription.CreatedAt,
                subscription.LockedUntil))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var leagues = await db.LeagueSubscriptions
            .AsNoTracking()
            .Where(subscription => subscription.UserId == userId)
            .OrderBy(subscription => subscription.CreatedAt)
            .Select(subscription => new UserLeagueSubscription(
                subscription.League.ExternalId,
                subscription.League.Name,
                subscription.CreatedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // The subscription endpoints identify an association by its Spordle id
        // (organizations.external_id), like teams and leagues; a row without a
        // mirror cannot be unsubscribed by id and is skipped.
        var organizations = await db.OrganizationSubscriptions
            .AsNoTracking()
            .Where(subscription => subscription.UserId == userId)
            .OrderBy(subscription => subscription.CreatedAt)
            .Select(subscription => new
            {
                subscription.Organization.ExternalId,
                subscription.Organization.Name,
                subscription.CreatedAt,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new(
            teams,
            leagues,
            [.. organizations
                .Select(organization => int.TryParse(organization.ExternalId, out var id)
                    ? new UserOrganizationSubscription(id, organization.Name, organization.CreatedAt)
                    : null)
                .OfType<UserOrganizationSubscription>()]);
    }

    /// <summary>
    /// Licence headroom of every follow kind. The maximum comes from the
    /// caller's role of that kind (COACH → teams, DIRECTOR → leagues,
    /// PRESIDENT → organizations) and the usage from the rows followed, so the
    /// configuration page can show "3 / 5" and disable a full kind. While
    /// open access is on there is no limit: <see cref="SubscriptionLimit.Max"/>
    /// is reported as unlimited and <see cref="SubscriptionLimit.CanAddMore"/>
    /// stays true.
    /// </summary>
    public async Task<IReadOnlyList<SubscriptionLimit>> GetLimitsAsync(int userId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var used = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [TeamKind] = await db.TeamSubscriptions.CountAsync(s => s.UserId == userId, cancellationToken).ConfigureAwait(false),
            [LeagueKind] = await db.LeagueSubscriptions.CountAsync(s => s.UserId == userId, cancellationToken).ConfigureAwait(false),
            [OrganizationKind] = await db.OrganizationSubscriptions.CountAsync(s => s.UserId == userId, cancellationToken).ConfigureAwait(false),
        };

        if (OpenAccess)
        {
            // No licence is enforced during the open-access phase: every kind
            // is unlimited, so the UI never blocks the add button.
            return [.. used.Select(pair => new SubscriptionLimit(pair.Key, null, pair.Value))];
        }

        var maxFollowed = await MaxFollowedByKindAsync(db, userId, cancellationToken).ConfigureAwait(false);
        // A PRESIDENT licence (whole association) has a stored limit of 0: the
        // role means "every league of the association", not a cap of zero, so
        // it is reported as unlimited rather than 0.
        var organizationMax = maxFollowed.GetValueOrDefault(PresidentRoleId) > 0
            ? maxFollowed[PresidentRoleId]
            : (int?)null;
        return
        [
            new(TeamKind, maxFollowed.GetValueOrDefault(CoachRoleId), used[TeamKind]),
            new(LeagueKind, maxFollowed.GetValueOrDefault(DirectorRoleId), used[LeagueKind]),
            new(OrganizationKind, organizationMax, used[OrganizationKind]),
        ];
    }

    /// <summary>
    /// Largest <c>roles.max_followed_entities</c> of the governing role of each
    /// kind, 0 when the caller does not hold it. A PRESIDENT limit of 0 is a
    /// whole-association scope, not a cap: the UI shows it as unlimited.
    /// </summary>
    private static async Task<Dictionary<int, int>> MaxFollowedByKindAsync(
        HockeySeasonDbContext db, int userId, CancellationToken cancellationToken)
    {
        var roleLimits = await db.UserRoles
            .AsNoTracking()
            .Where(userRole => userRole.UserId == userId)
            .Select(userRole => new { userRole.RoleId, userRole.Role.MaxFollowedEntities })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return roleLimits
            .GroupBy(role => (int)role.RoleId)
            .ToDictionary(
                group => group.Key,
                group => group.Max(role => role.MaxFollowedEntities));
    }

    public async Task UpdateSettingsAsync(int userId, UserSettingsUpdate update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var setting = await db.UserSettings
            .SingleOrDefaultAsync(s => s.UserId == userId, cancellationToken)
            .ConfigureAwait(false);
        if (setting is null)
        {
            var now = timeProvider.UtcNow;
            setting = new UserSetting
            {
                UserId = userId,
                Extra = [],
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.UserSettings.Add(setting);
        }

        // Partial update semantics: null leaves the current value untouched.
        if (update.PrimaryColor is not null)
        {
            setting.PrimaryColor = update.PrimaryColor;
        }
        if (update.SecondaryColor is not null)
        {
            setting.SecondaryColor = update.SecondaryColor;
        }
        if (update.Extra is not null)
        {
            setting.Extra = new Dictionary<string, string?>(update.Extra);
        }
        setting.UpdatedAt = timeProvider.UtcNow;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<SubscriptionResult> AddTeamSubscriptionAsync(int userId, int teamId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var team = await db.Teams
            .AsNoTracking()
            .Where(t => t.ExternalId == teamId.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Select(t => new { t.Id })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (team is null)
        {
            return new(SubscriptionError.UnknownEntity, null);
        }

        var exists = await db.TeamSubscriptions
            .AnyAsync(s => s.UserId == userId && s.TeamId == team.Id, cancellationToken)
            .ConfigureAwait(false);
        if (exists)
        {
            return new(SubscriptionError.Duplicate, null);
        }

        var limit = await SlotAsync(db, userId, CoachRoleId, TeamKind,
            db.TeamSubscriptions, s => s.UserId == userId, cancellationToken).ConfigureAwait(false);
        if (!limit.CanAddMore)
        {
            return new(SubscriptionError.LimitReached, null, limit);
        }

        var now = timeProvider.UtcNow;
        var subscription = new TeamSubscription
        {
            UserId = userId,
            TeamId = team.Id,
            CreatedAt = now,
            UpdatedAt = now,
            LockedUntil = now.Add(TeamLockDuration),
        };
        db.TeamSubscriptions.Add(subscription);
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return new(SubscriptionError.None, subscription.LockedUntil, limit with { Used = limit.Used + 1 });
        }
        catch (DbUpdateException)
        {
            return new(SubscriptionError.Duplicate, null);
        }
    }

    public async Task<SubscriptionError> RemoveTeamSubscriptionAsync(int userId, int teamId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var subscription = await db.TeamSubscriptions
            .SingleOrDefaultAsync(s => s.UserId == userId && s.Team.ExternalId == teamId.ToString(System.Globalization.CultureInfo.InvariantCulture), cancellationToken)
            .ConfigureAwait(false);
        if (subscription is null)
        {
            return SubscriptionError.UnknownEntity;
        }
        if (!isAdmin && !OpenAccess && subscription.LockedUntil > timeProvider.UtcNow)
        {
            // Refused removal, distinct from the licence cap: the API maps it
            // to 423 Locked so the page can say when the team unlocks.
            return SubscriptionError.Locked;
        }

        db.TeamSubscriptions.Remove(subscription);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return SubscriptionError.None;
    }

    public async Task RequestTeamUnlockAsync(int userId, int teamId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var subscription = await db.TeamSubscriptions
            .SingleOrDefaultAsync(s => s.UserId == userId && s.Team.ExternalId == teamId.ToString(System.Globalization.CultureInfo.InvariantCulture), cancellationToken)
            .ConfigureAwait(false);
        if (subscription is null)
        {
            return;
        }

        subscription.UnlockRequestedAt = timeProvider.UtcNow;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<SubscriptionResult> AddLeagueSubscriptionAsync(int userId, string leagueId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leagueId);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var league = await db.Leagues
            .AsNoTracking()
            .Where(l => l.ExternalId == leagueId)
            .Select(l => new { l.Id })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (league is null)
        {
            return new(SubscriptionError.UnknownEntity, null);
        }

        var exists = await db.LeagueSubscriptions
            .AnyAsync(s => s.UserId == userId && s.LeagueId == league.Id, cancellationToken)
            .ConfigureAwait(false);
        if (exists)
        {
            return new(SubscriptionError.Duplicate, null);
        }

        var limit = await SlotAsync(db, userId, DirectorRoleId, LeagueKind,
            db.LeagueSubscriptions, s => s.UserId == userId, cancellationToken).ConfigureAwait(false);
        if (!limit.CanAddMore)
        {
            return new(SubscriptionError.LimitReached, null, limit);
        }

        var now = timeProvider.UtcNow;
        db.LeagueSubscriptions.Add(new LeagueSubscription
        {
            UserId = userId,
            LeagueId = league.Id,
            CreatedAt = now,
            UpdatedAt = now,
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return new(SubscriptionError.None, null, limit with { Used = limit.Used + 1 });
        }
        catch (DbUpdateException)
        {
            return new(SubscriptionError.Duplicate, null);
        }
    }

    public async Task<SubscriptionError> RemoveLeagueSubscriptionAsync(int userId, string leagueId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leagueId);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var subscription = await db.LeagueSubscriptions
            .SingleOrDefaultAsync(s => s.UserId == userId && s.League.ExternalId == leagueId, cancellationToken)
            .ConfigureAwait(false);
        if (subscription is null)
        {
            return SubscriptionError.UnknownEntity;
        }

        db.LeagueSubscriptions.Remove(subscription);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return SubscriptionError.None;
    }

    public async Task<SubscriptionResult> AddOrganizationSubscriptionAsync(int userId, int organizationId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var organization = await db.Organizations
            .AsNoTracking()
            .Where(o => o.ExternalId == organizationId.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Select(o => new { o.Id })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (organization is null)
        {
            return new(SubscriptionError.UnknownEntity, null);
        }

        var exists = await db.OrganizationSubscriptions
            .AnyAsync(s => s.UserId == userId && s.OrganizationId == organization.Id, cancellationToken)
            .ConfigureAwait(false);
        if (exists)
        {
            return new(SubscriptionError.Duplicate, null);
        }

        var now = timeProvider.UtcNow;
        db.OrganizationSubscriptions.Add(new OrganizationSubscription
        {
            UserId = userId,
            OrganizationId = organization.Id,
            CreatedAt = now,
            UpdatedAt = now,
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return new(SubscriptionError.None, null);
        }
        catch (DbUpdateException)
        {
            return new(SubscriptionError.Duplicate, null);
        }
    }

    public async Task<SubscriptionError> RemoveOrganizationSubscriptionAsync(int userId, int organizationId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var subscription = await db.OrganizationSubscriptions
            .SingleOrDefaultAsync(s => s.UserId == userId && s.Organization.ExternalId == organizationId.ToString(System.Globalization.CultureInfo.InvariantCulture), cancellationToken)
            .ConfigureAwait(false);
        if (subscription is null)
        {
            return SubscriptionError.UnknownEntity;
        }

        db.OrganizationSubscriptions.Remove(subscription);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return SubscriptionError.None;
    }

    /// <summary>
    /// Checks the licence limit for the given role before adding a row and
    /// returns the headroom of that follow kind. The role lookup is soft: a
    /// caller without the role has a maximum of 0 (no individual follow).
    /// </summary>
    private async Task<SubscriptionLimit> SlotAsync<TSubscription>(
        HockeySeasonDbContext db,
        int userId,
        int roleId,
        string kind,
        IQueryable<TSubscription> subscriptions,
        System.Linq.Expressions.Expression<Func<TSubscription, bool>> userFilter,
        CancellationToken cancellationToken)
        where TSubscription : class
    {
        var used = await subscriptions
            .Where(userFilter)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);
        if (OpenAccess)
        {
            return new(kind, null, used);
        }

        var maximums = await MaxFollowedByKindAsync(db, userId, cancellationToken).ConfigureAwait(false);
        return new(kind, maximums.GetValueOrDefault(roleId), used);
    }
}
