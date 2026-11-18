namespace HockeySeason.Application;

/// <summary>
/// Admin view of a local account: identity, admin flag and the licence roles
/// granted through the <c>user_roles</c> join table.
/// </summary>
public sealed record AdminUserSummary(
    int Id,
    string Auth0UserId,
    string? Email,
    string? FirstName,
    string? LastName,
    bool IsAdmin,
    IReadOnlyList<AdminRoleAssignment> Roles,
    DateTimeOffset CreatedAt);

/// <summary>One licence granted to a user, with its limit.</summary>
public sealed record AdminRoleAssignment(int RoleId, string Code, string Name, int MaxFollowedEntities);

/// <summary>An assignable role of the <c>roles</c> reference table.</summary>
public sealed record AdminRoleOption(int RoleId, string Code, string Name, int MaxFollowedEntities);

/// <summary>Failure reason of a role assignment.</summary>
public enum AdminRoleUpdateError
{
    None,
    /// <summary>The targeted user does not exist.</summary>
    UnknownUser,
    /// <summary>At least one role id is not part of the roles table.</summary>
    UnknownRole,
}

/// <summary>Outcome of a role assignment: the updated user on success.</summary>
public sealed record AdminRoleUpdateResult(AdminRoleUpdateError Error, AdminUserSummary? User);

/// <summary>
/// Admin use case: list local accounts and grant/replace their licence roles.
/// Only callers flagged as administrators may use it.
/// </summary>
public interface IAdminUserService
{
    /// <summary>Lists every local account with its granted roles.</summary>
    Task<IReadOnlyList<AdminUserSummary>> GetUsersAsync(CancellationToken cancellationToken = default);

    /// <summary>Lists the roles that can be granted (seeded <c>roles</c> table).</summary>
    Task<IReadOnlyList<AdminRoleOption>> GetRolesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the whole role set of a user: roles absent from
    /// <paramref name="roleIds"/> are revoked, missing ones are granted.
    /// </summary>
    Task<AdminRoleUpdateResult> SetRolesAsync(int userId, IReadOnlyCollection<int> roleIds, CancellationToken cancellationToken = default);

    /// <summary>Whether the account is flagged <c>is_admin</c> in the database.</summary>
    Task<bool> IsUserAdminAsync(int userId, CancellationToken cancellationToken = default);
}
