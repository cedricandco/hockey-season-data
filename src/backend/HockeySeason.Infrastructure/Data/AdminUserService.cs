using HockeySeason.Application;
using HockeySeason.Domain;
using Microsoft.EntityFrameworkCore;

namespace HockeySeason.Infrastructure.Data;

/// <summary>
/// Admin use case over the persistence model: reads the local accounts of the
/// <c>users</c> table and maintains their licences in the <c>user_roles</c>
/// join table against the seeded <c>roles</c> reference table.
/// </summary>
public sealed class AdminUserService(
    IDbContextFactory<HockeySeasonDbContext> dbContextFactory,
    ITimeProvider timeProvider) : IAdminUserService
{
    public async Task<IReadOnlyList<AdminUserSummary>> GetUsersAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var users = await db.Users
            .AsNoTracking()
            .OrderBy(user => user.Id)
            .Select(user => new
            {
                user.Id,
                user.Auth0UserId,
                user.Email,
                user.FirstName,
                user.LastName,
                user.IsAdmin,
                user.CreatedAt,
                Roles = user.Roles
                    .OrderBy(userRole => userRole.RoleId)
                    .Select(userRole => new { userRole.RoleId, userRole.Role.Code, userRole.Role.Name, userRole.Role.MaxFollowedEntities })
                    .ToList(),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. users.Select(user => new AdminUserSummary(
            user.Id,
            user.Auth0UserId,
            user.Email,
            user.FirstName,
            user.LastName,
            user.IsAdmin,
            [.. user.Roles.Select(role => new AdminRoleAssignment(role.RoleId, role.Code, role.Name, role.MaxFollowedEntities))],
            user.CreatedAt))];
    }

    public async Task<IReadOnlyList<AdminRoleOption>> GetRolesAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        return await db.Roles
            .AsNoTracking()
            .OrderBy(role => role.Id)
            .Select(role => new AdminRoleOption(role.Id, role.Code, role.Name, role.MaxFollowedEntities))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<AdminRoleUpdateResult> SetRolesAsync(int userId, IReadOnlyCollection<int> roleIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roleIds);

        var requested = roleIds.Distinct().ToHashSet();
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var user = await db.Users
            .Include(u => u.Roles)
            .SingleOrDefaultAsync(u => u.Id == userId, cancellationToken)
            .ConfigureAwait(false);
        if (user is null)
        {
            return new(AdminRoleUpdateError.UnknownUser, null);
        }

        var knownRoleIds = await db.Roles
            .Select(role => (int)role.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (requested.Except(knownRoleIds).Any())
        {
            return new(AdminRoleUpdateError.UnknownRole, null);
        }

        var revoked = user.Roles.Where(userRole => !requested.Contains(userRole.RoleId)).ToList();
        db.UserRoles.RemoveRange(revoked);

        var granted = requested.Except(user.Roles.Select(userRole => (int)userRole.RoleId)).ToList();
        foreach (var roleId in granted)
        {
            db.UserRoles.Add(new UserRole
            {
                UserId = userId,
                RoleId = (short)roleId,
                CreatedAt = timeProvider.UtcNow,
            });
        }

        if (revoked.Count > 0 || granted.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        var updated = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new AdminUserSummary(
                u.Id,
                u.Auth0UserId,
                u.Email,
                u.FirstName,
                u.LastName,
                u.IsAdmin,
                u.Roles
                    .OrderBy(userRole => userRole.RoleId)
                    .Select(userRole => new AdminRoleAssignment(userRole.RoleId, userRole.Role.Code, userRole.Role.Name, userRole.Role.MaxFollowedEntities))
                    .ToList(),
                u.CreatedAt))
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);

        return new(AdminRoleUpdateError.None, updated);
    }

    public async Task<bool> IsUserAdminAsync(int userId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        return await db.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => user.IsAdmin)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
