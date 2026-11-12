namespace HockeySeason.Application;

/// <summary>
/// Licence role of a user, mirroring the seeded <c>roles</c> table and the
/// software licensing model: COACH (up to 5 followed teams), DIRECTOR
/// (up to 5 followed leagues), PRESIDENT (whole association).
/// </summary>
public enum UserRoleCode
{
    Coach = 1,
    Director = 2,
    President = 3,
}

/// <summary>Role granted to a user, with its licence limit.</summary>
public sealed record UserRoleSummary(int RoleId, UserRoleCode Code, string DisplayName, int MaxFollowedEntities);

/// <summary>Teams and leagues the user follows directly.</summary>
public sealed record UserSubscriptions(IReadOnlyList<UserSubscriptionEntry> Teams, IReadOnlyList<UserSubscriptionEntry> Leagues);

/// <summary>One followed entity, with its lock window (team subscriptions only).</summary>
public sealed record UserSubscriptionEntry(int EntityId, string? Name, DateTimeOffset CreatedAt, DateTimeOffset? LockedUntil);
