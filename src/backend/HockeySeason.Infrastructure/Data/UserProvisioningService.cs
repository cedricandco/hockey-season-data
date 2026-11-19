using System.Security.Claims;
using HockeySeason.Application;
using HockeySeason.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HockeySeason.Infrastructure.Data;

/// <summary>
/// Application use case implementation over the PostgreSQL persistence model:
/// creates the <c>users</c> row for a caller on first authenticated request,
/// grants the default COACH licence and fills the profile claims (email, first
/// and last name) published by the identity provider. Idempotent: a SELECT runs
/// first, a value already stored is never overwritten, and the COACH role is
/// granted at most once.
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
            // Idempotent refresh: only fields the identity provider now knows
            // and the row is still missing are filled. Stored values win, so an
            // email or name is never overwritten.
            var changed = FillMissingProfile(user, claims);
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
    /// Copies the profile claims the row is still missing; returns whether
    /// anything changed. Existing values are never replaced.
    /// </summary>
    private static bool FillMissingProfile(User user, ApplicationUserClaims claims)
    {
        var changed = false;

        if (string.IsNullOrWhiteSpace(user.Email) && EmptyToNull(claims.Email) is { } email)
        {
            user.Email = email;
            changed = true;
        }
        if (string.IsNullOrWhiteSpace(user.FirstName) && EmptyToNull(claims.FirstName) is { } firstName)
        {
            user.FirstName = firstName;
            changed = true;
        }
        if (string.IsNullOrWhiteSpace(user.LastName) && EmptyToNull(claims.LastName) is { } lastName)
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
