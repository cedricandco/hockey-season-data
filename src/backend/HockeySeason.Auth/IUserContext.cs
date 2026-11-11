namespace HockeySeason.Auth;

/// <summary>
/// Resolves the identity-provider profile of the current request's caller.
/// Implementations read the bearer token from the request headers (standard
/// <c>Authorization</c>, mirrored by the SPA on <c>X-Authorization</c> when
/// proxies strip the standard header) and call the provider's
/// <c>/userinfo</c> endpoint on demand.
/// </summary>
public interface IUserContext
{
    /// <summary>
    /// Returns the profile of the caller, or <c>null</c> when the request
    /// carries no usable bearer token or the provider rejects it.
    /// </summary>
    Task<UserInfo?> GetCurrentUserAsync(CancellationToken cancellationToken = default);
}
