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
            user.Extra);
    }

    public async Task<UserSubscriptions> GetSubscriptionsAsync(int userId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var teams = await db.TeamSubscriptions
            .AsNoTracking()
            .Where(subscription => subscription.UserId == userId)
            .OrderBy(subscription => subscription.CreatedAt)
            .Select(subscription => new UserSubscriptionEntry(
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
            .Select(subscription => new UserSubscriptionEntry(
                int.Parse(subscription.League.ExternalId),
                subscription.League.Name,
                subscription.CreatedAt,
                default(DateTimeOffset?)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new(teams, leagues);
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

        if (!OpenAccess && !await ReserveSlotAsync(db, userId, CoachRoleId, db.TeamSubscriptions, s => s.UserId == userId, cancellationToken)
            .ConfigureAwait(false))
        {
            return new(SubscriptionError.LimitReached, null);
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
            return new(SubscriptionError.None, subscription.LockedUntil);
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
            return SubscriptionError.LimitReached; // Locked: distinct error surfaced by the API layer as 423/409.
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

    public async Task<SubscriptionResult> AddLeagueSubscriptionAsync(int userId, int leagueId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var league = await db.Leagues
            .AsNoTracking()
            .Where(l => l.ExternalId == leagueId.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Select(l => new { l.Id })
            .SingleOrDefaultAsync(cancellationToken)
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

        if (!OpenAccess && !await ReserveSlotAsync(db, userId, DirectorRoleId, db.LeagueSubscriptions, s => s.UserId == userId, cancellationToken)
            .ConfigureAwait(false))
        {
            return new(SubscriptionError.LimitReached, null);
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
            return new(SubscriptionError.None, null);
        }
        catch (DbUpdateException)
        {
            return new(SubscriptionError.Duplicate, null);
        }
    }

    public async Task<SubscriptionError> RemoveLeagueSubscriptionAsync(int userId, int leagueId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var subscription = await db.LeagueSubscriptions
            .SingleOrDefaultAsync(s => s.UserId == userId && s.League.ExternalId == leagueId.ToString(System.Globalization.CultureInfo.InvariantCulture), cancellationToken)
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
    /// Checks the licence limit for the given role before adding a row. The
    /// role lookup is soft: users without the role fall back to the role's
    /// default limit being 0 (no individual follow) unless they hold it.
    /// </summary>
    private static async Task<bool> ReserveSlotAsync<TSubscription>(
        HockeySeasonDbContext db,
        int userId,
        int roleId,
        IQueryable<TSubscription> subscriptions,
        System.Linq.Expressions.Expression<Func<TSubscription, bool>> userFilter,
        CancellationToken cancellationToken)
        where TSubscription : class
    {
        var maxFollowed = await db.UserRoles
            .AsNoTracking()
            .Where(userRole => userRole.UserId == userId && userRole.RoleId == roleId)
            .Select(userRole => (int?)userRole.Role.MaxFollowedEntities)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (maxFollowed is null)
        {
            // The user does not hold the licence required for this follow type.
            return false;
        }

        var current = await subscriptions
            .Where(userFilter)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);
        return current < maxFollowed;
    }
}
