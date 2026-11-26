namespace HockeySeason.Auth;

/// <summary>
/// Why a caller profile could not be resolved. The message endpoint answers a
/// generic 502 to the client, so the reason has to reach the logs — without it
/// every failure looked like the same opaque <c>userinfo_unavailable</c>.
/// </summary>
public enum UserInfoFailure
{
    /// <summary>No bearer token reached the userinfo call.</summary>
    MissingToken,

    /// <summary>The provider refused the token (401/403): wrong audience, expired, or not an OIDC token.</summary>
    ProviderRejected,

    /// <summary>The provider answered non-success (other than a refusal).</summary>
    ProviderError,

    /// <summary>The provider could not be reached (DNS, TLS, timeout).</summary>
    Unreachable,

    /// <summary>The payload could not be mapped: a claim has an unexpected JSON type or shape.</summary>
    UnparseablePayload,

    /// <summary>The payload carried no usable <c>sub</c> claim.</summary>
    MissingSubject,
}

/// <summary>Outcome of a userinfo resolution: the profile, or why it is absent.</summary>
public sealed record UserInfoResult(UserInfo? Profile, UserInfoFailure? Failure)
{
    public static UserInfoResult Success(UserInfo profile) => new(profile, null);

    public static UserInfoResult Failed(UserInfoFailure failure) => new(null, failure);
}

/// <summary>
/// Resolves the identity-provider profile of a caller. Implementations call
/// the provider's <c>/userinfo</c> endpoint on demand.
/// </summary>
public interface IUserContext
{
    /// <summary>
    /// Returns the profile of the caller, or <c>null</c> when the request
    /// carries no usable bearer token or the provider rejects it.
    /// </summary>
    Task<UserInfo?> GetCurrentUserAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the profile of the caller for an explicitly supplied bearer
    /// token. Hosts call this overload: the token comes from the invocation's
    /// own <c>HttpContext</c>, which is populated on every host, whereas
    /// relying on an ambient <c>IHttpContextAccessor</c> fails wherever that
    /// accessor is not registered (the isolated Azure Functions worker
    /// registers no <c>IHttpContextAccessor</c>).
    /// </summary>
    Task<UserInfo?> GetUserAsync(string? bearerToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Same as <see cref="GetUserAsync"/>, additionally reporting why the
    /// profile is unavailable so the host can log a diagnosable reason instead
    /// of returning an opaque failure.
    /// </summary>
    Task<UserInfoResult> TryGetUserAsync(string? bearerToken, CancellationToken cancellationToken = default);
}
