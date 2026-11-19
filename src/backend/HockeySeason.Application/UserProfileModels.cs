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

/// <summary>Teams, leagues and associations the user follows.</summary>
public sealed record UserSubscriptions(
    IReadOnlyList<UserTeamSubscription> Teams,
    IReadOnlyList<UserLeagueSubscription> Leagues,
    IReadOnlyList<UserOrganizationSubscription> Organizations);

/// <summary>
/// One followed team, with its lock window: the identifier is the Spordle team
/// id and <see cref="LockedUntil"/> the end of the 15-day removal lock.
/// </summary>
public sealed record UserTeamSubscription(int TeamId, string? Name, DateTimeOffset CreatedAt, DateTimeOffset? LockedUntil);

/// <summary>One followed league: the identifier is the Spordle category id (guid).</summary>
public sealed record UserLeagueSubscription(string LeagueId, string? Name, DateTimeOffset CreatedAt);

/// <summary>
/// One followed association (PRESIDENT scope): the identifier is the Spordle
/// id (<c>organizations.external_id</c>), like teams and leagues.
/// </summary>
public sealed record UserOrganizationSubscription(int OrganizationId, string? Name, DateTimeOffset CreatedAt);

