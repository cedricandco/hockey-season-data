namespace HockeySeason.Application;

/// <summary>
/// Claims of the authenticated caller needed to provision a local user row:
/// the stable subject (`sub`) plus the profile claims published by the
/// identity provider. Built by the host (Api/Functions) from the JWT or the
/// userinfo profile — the application layer stays host-agnostic.
/// </summary>
public sealed record ApplicationUserClaims(string Subject, string? Email, string? FirstName, string? LastName);

/// <summary>Result of an idempotent provisioning run.</summary>
public sealed record UserProvisioningResult(int UserId, bool Created);

/// <summary>
/// Use case: ensure the local <c>users</c> row exists for the caller
/// (Auth0-managed identity), on first authenticated request. Idempotent by
/// construction — SELECT before INSERT, no row mutated when it already exists,
/// email never overwritten.
/// </summary>
public interface IUserProvisioningService
{
    /// <summary>
    /// Upserts the caller's user row and returns its local id. When the user
    /// already exists, nothing is written.
    /// </summary>
    Task<UserProvisioningResult> ProvisionAsync(ApplicationUserClaims claims, CancellationToken cancellationToken = default);
}
