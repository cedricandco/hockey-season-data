using System.Security.Claims;
using HockeySeason.Application;
using HockeySeason.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HockeySeason.Infrastructure.Data;

/// <summary>
/// Application use case implementation over the PostgreSQL persistence model:
/// creates the <c>users</c> row for a caller on first authenticated request,
/// grants the default COACH licence and keeps the profile (email, first and
/// last name) in sync with the identity provider on every call. Idempotent: a
/// SELECT runs first, the COACH role is granted at most once, and a claim the
/// provider does not publish leaves the stored value untouched (so a profile
/// collected from userinfo is never wiped by a token without the claims).
/// </summary>
public sealed class UserProvisioningService(
    IDbContextFactory<HockeySeasonDbContext> dbContextFactory,
    ILogger<UserProvisioningService> logger) : IUserProvisioningService
{
    /// <summary>Default licence of a new account (seeded <c>roles.id = 1</c>, "coach").</summary>
    private const short DefaultRoleId = 1;

    public async Task<UserProvisioningResult> ProvisionAsync(ApplicationUserClaims claims, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claims);
        ArgumentException.ThrowIfNullOrWhiteSpace(claims.Subject);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var user = await db.Users
            .FirstOrDefaultAsync(u => u.Auth0UserId == claims.Subject, cancellationToken)
            .ConfigureAwait(false);

        if (user is not null)
        {
            // Every authenticated request refreshes the profile from the
            // identity provider, which is the source of truth: a name or an
            // email changed in Auth0 lands in the local row on the next call.
            var changed = ApplyProfile(user, claims);
            if (changed)
            {
                user.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            return new(user.Id, Created: false, ProfileIncomplete: IsProfileIncomplete(user));
        }

        var now = DateTimeOffset.UtcNow;
        var created = new User
        {
            Auth0UserId = claims.Subject,
            Email = EmptyToNull(claims.Email),
            FirstName = EmptyToNull(claims.FirstName),
            LastName = EmptyToNull(claims.LastName),
            IsAdmin = false,
            CreatedAt = now,
            UpdatedAt = now,
            // A brand-new account starts as a coach: it may follow teams right
            // away, which is what the team-selection page offers after signup.
            // Added through the navigation so the insert is a single transaction.
            Roles = [new UserRole { RoleId = DefaultRoleId, CreatedAt = now }],
        };
        db.Users.Add(created);
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            logger.LogInformation(
                "Provisioned local user {UserId} as coach for Auth0 subject {SubjectLength} character id.",
                created.Id, claims.Subject.Length);
            return new(created.Id, Created: true, ProfileIncomplete: IsProfileIncomplete(created));
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            // A concurrent request provisioned the same subject first — the
            // race resolves to the winner's row, staying idempotent.
            db.ChangeTracker.Clear();
            var winner = await db.Users
                .AsNoTracking()
                .FirstAsync(u => u.Auth0UserId == claims.Subject, cancellationToken)
                .ConfigureAwait(false);
            logger.LogDebug("User provisioning race for an existing Auth0 subject; resolved to user {UserId}.", winner.Id);
            return new(winner.Id, Created: false);
        }
    }

    /// <summary>
    /// Copies the profile onto the row, replacing what is stored; returns
    /// whether anything changed. A claim the identity provider does not publish
    /// (null) leaves the stored value alone, so a token without the custom
    /// claims never wipes a profile already collected from userinfo.
    /// </summary>
    private static bool ApplyProfile(User user, ApplicationUserClaims claims)
    {
        var changed = false;

        if (EmptyToNull(claims.Email) is { } email && user.Email != email)
        {
            user.Email = email;
            changed = true;
        }
        if (EmptyToNull(claims.FirstName) is { } firstName && user.FirstName != firstName)
        {
            user.FirstName = firstName;
            changed = true;
        }
        if (EmptyToNull(claims.LastName) is { } lastName && user.LastName != lastName)
        {
            user.LastName = lastName;
            changed = true;
        }
        return changed;
    }

    /// <summary>
    /// Whether the stored profile still misses a field the identity provider
    /// may publish: the host only calls userinfo in that case.
    /// </summary>
    private static bool IsProfileIncomplete(User user) =>
        string.IsNullOrWhiteSpace(user.Email)
        || string.IsNullOrWhiteSpace(user.FirstName)
        || string.IsNullOrWhiteSpace(user.LastName);

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation };
}
