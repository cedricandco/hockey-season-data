namespace HockeySeason.Application;

/// <summary>Visual preferences a user may edit (colors + generic extras).</summary>
public sealed record UserSettingsUpdate(string? PrimaryColor, string? SecondaryColor, IReadOnlyDictionary<string, string?>? Extra);

/// <summary>
/// Use case: manage the caller's subscriptions with licence limits, and their
/// visual settings. The 15-day lock on team subscriptions is enforced here.
/// </summary>
public interface IUserService
{
    /// <summary>Lists follows of the user (teams and leagues) with lock windows.</summary>
    Task<UserSubscriptions> GetSubscriptionsAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>Lists profile, roles, limits and settings of the user.</summary>
    Task<CurrentUserProfile?> GetProfileAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>Updates (partial) visual settings; creates the settings row on first update.</summary>
    Task UpdateSettingsAsync(int userId, UserSettingsUpdate update, CancellationToken cancellationToken = default);

    /// <summary>Follows a team. Enforces the COACH limit (5) and the 15-day lock window.</summary>
    Task<SubscriptionResult> AddTeamSubscriptionAsync(int userId, int teamId, CancellationToken cancellationToken = default);

    /// <summary>Unfollows a team; refused while the 15-day lock is active unless admin.</summary>
    Task<SubscriptionError> RemoveTeamSubscriptionAsync(int userId, int teamId, bool isAdmin, CancellationToken cancellationToken = default);

    /// <summary>Marks an unlock request on a locked team subscription (audit trace for admins).</summary>
    Task RequestTeamUnlockAsync(int userId, int teamId, CancellationToken cancellationToken = default);

    /// <summary>Follows a league. Enforces the DIRECTOR limit (5).</summary>
    Task<SubscriptionResult> AddLeagueSubscriptionAsync(int userId, int leagueId, CancellationToken cancellationToken = default);

    /// <summary>Unfollows a league.</summary>
    Task<SubscriptionError> RemoveLeagueSubscriptionAsync(int userId, int leagueId, CancellationToken cancellationToken = default);

    /// <summary>Follows a whole association (PRESIDENT scope).</summary>
    Task<SubscriptionResult> AddOrganizationSubscriptionAsync(int userId, int organizationId, CancellationToken cancellationToken = default);

    /// <summary>Unfollows an association.</summary>
    Task<SubscriptionError> RemoveOrganizationSubscriptionAsync(int userId, int organizationId, CancellationToken cancellationToken = default);
}

/// <summary>Profile of the caller, shown by GET /api/users/me and the frontend.</summary>
public sealed record CurrentUserProfile(
    int Id,
    string Auth0UserId,
    string? Email,
    string? FirstName,
    string? LastName,
    bool IsAdmin,
    IReadOnlyList<UserRoleSummary> Roles,
    string? PrimaryColor,
    string? SecondaryColor,
    IReadOnlyDictionary<string, string?> Extra);
