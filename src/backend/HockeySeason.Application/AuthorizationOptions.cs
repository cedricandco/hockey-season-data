namespace HockeySeason.Application;

/// <summary>
/// Authorization switches of the API. Currently owns the temporary
/// open-access phase: while <see cref="OpenAccess"/> is true, every
/// authenticated caller has full access whatever the database says — no
/// subscription scope, no licence role, no admin check, no team lock.
/// Authentication itself stays enforced.
/// </summary>
public sealed class AuthorizationOptions
{
    /// <summary>Configuration section bound to these options.</summary>
    public const string SectionName = "Authorization";

    /// <summary>
    /// Temporary blanket access. Default <c>false</c> (secure); currently
    /// enabled via <c>appsettings.json</c> until roles/licences are actually
    /// provisioned in the database. Close it later with the env var
    /// <c>Authorization__OpenAccess=false</c>.
    /// </summary>
    public bool OpenAccess { get; set; }
}