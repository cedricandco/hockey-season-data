namespace HockeySeason.Application;

/// <summary>
/// Claims of the authenticated caller needed to provision a local user row:
/// the stable subject (`sub`) plus the profile claims published by the
/// identity provider. Built by the host (Api/Functions) from the JWT or the
/// userinfo profile — the application layer stays host-agnostic.
/// </summary>
public sealed record ApplicationUserClaims(string Subject, string? Email, string? FirstName, string? LastName);

/// <summary>Result of an idempotent provisioning run.</summary>
/// <param name="UserId">Local id of the account.</param>
/// <param name="Created">Whether the row was created by this call.</param>
/// <param name="ProfileIncomplete">
/// Whether the stored profile still misses the email, the first or the last
/// name. The host then completes the claims from the provider's userinfo
/// profile instead of calling it on every request.
/// </param>
public sealed record UserProvisioningResult(int UserId, bool Created, bool ProfileIncomplete = false);

/// <summary>
/// Use case: ensure the local <c>users</c> row exists for the caller
/// (Auth0-managed identity), on first authenticated request, and keep its
/// profile in sync with the identity provider on every later request.
/// Idempotent by construction — SELECT before INSERT.
/// </summary>
public interface IUserProvisioningService
{
    /// <summary>
    /// Upserts the caller's user row and returns its local id. A new account is
    /// created with the COACH licence (the default of the software) and the
    /// profile fields (email, first and last name) carried by the claims. An
    /// existing row has those three fields replaced by the claims — the
    /// identity provider is the source of truth, so a name or email changed in
    /// Auth0 reaches the database on the next authenticated request. A claim
    /// that is missing from the token leaves the stored value alone.
    /// </summary>
    Task<UserProvisioningResult> ProvisionAsync(ApplicationUserClaims claims, CancellationToken cancellationToken = default);
}
