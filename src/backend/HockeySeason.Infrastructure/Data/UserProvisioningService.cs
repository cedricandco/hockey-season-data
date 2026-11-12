using System.Security.Claims;
using HockeySeason.Application;
using HockeySeason.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HockeySeason.Infrastructure.Data;

/// <summary>
/// Application use case implementation over the PostgreSQL persistence model:
/// creates the <c>users</c> row (and role rows) for a caller on first
/// authenticated request. Idempotent: a SELECT runs first, existing rows are
/// never mutated and emails are never overwritten.
/// </summary>
public sealed class UserProvisioningService(
    IDbContextFactory<HockeySeasonDbContext> dbContextFactory,
    ILogger<UserProvisioningService> logger) : IUserProvisioningService
{
    public async Task<UserProvisioningResult> ProvisionAsync(ApplicationUserClaims claims, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claims);
        ArgumentException.ThrowIfNullOrWhiteSpace(claims.Subject);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var user = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Auth0UserId == claims.Subject, cancellationToken)
            .ConfigureAwait(false);

        if (user is not null)
        {
            return new(user.Id, Created: false);
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
        };
        db.Users.Add(created);
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            logger.LogInformation(
                "Provisioned local user {UserId} for Auth0 subject {SubjectLength} character id.",
                created.Id, claims.Subject.Length);
            return new(created.Id, Created: true);
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

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation };
}
