namespace HockeySeason.Auth;

/// <summary>Shared constants for the auth layer.</summary>
public static class AuthConstants
{
    /// <summary>Claim marking that the principal authenticated with the shared access code.</summary>
    public const string CodeAuthenticationMethod = "code";

    /// <summary>Claim carrying the per-deployment secret stamp rotated with the PIN.</summary>
    public const string SecurityStampClaim = "security_stamp";

    /// <summary>Cookie/local-storage key used by the frontend.</summary>
    public const string TokenStorageKey = "hockey-season-auth-token";
}
