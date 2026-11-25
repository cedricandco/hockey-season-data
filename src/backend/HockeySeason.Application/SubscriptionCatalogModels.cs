namespace HockeySeason.Application;

/// <summary>
/// An association offered by the subscription selector: one row of the
/// <c>organizations</c> table. <see cref="Id"/> is the local identifier, the
/// one the divisions of the association are listed with.
/// </summary>
public sealed record SubscriptionAssociation(int Id, string Name);

/// <summary>
/// A division (Spordle office) of an association. <see cref="Id"/> is the
/// Spordle office number (<c>divisions.external_id</c>, e.g. 6193), the value
/// the league list is queried with.
/// </summary>
public sealed record SubscriptionDivision(int Id, string Name);

/// <summary>
/// A league (Spordle category) of a division for one season.
/// <see cref="Id"/> is the Spordle category guid (<c>leagues.external_id</c>),
/// the value the team list and the league subscription endpoints use.
/// </summary>
public sealed record SubscriptionLeague(string Id, string Name, string SeasonId);

/// <summary>
/// A team of a league. <see cref="Id"/> is the Spordle team id
/// (<c>teams.external_id</c>), the identifier the team subscription endpoints
/// expect.
/// </summary>
public sealed record SubscriptionTeam(int Id, string Name);

/// <summary>
/// Leagues of a division for the selected season (the latest imported season
/// when none is requested), plus the season that was used.
/// </summary>
public sealed record SubscriptionLeagueOptions(string? SeasonId, IReadOnlyList<SubscriptionLeague> Leagues);

/// <summary>
/// A team an association president may follow by typing its base name, with
/// the league and division it plays in so the page can list it without another
/// call. <see cref="Id"/> is the Spordle team id, the value the team
/// subscription endpoints expect.
/// </summary>
public sealed record PresidentTeam(int Id, string Name, string LeagueName, string DivisionName);

/// <summary>
/// Teams of an association whose name starts with the typed base name, in the
/// latest imported season of the association ("grizzly" → every GRIZZLY team
/// of every league, division and category).
/// </summary>
public sealed record PresidentTeamOptions(
    int AssociationId,
    string AssociationName,
    string BaseName,
    IReadOnlyList<PresidentTeam> Teams);

/// <summary>
/// Read-only navigation for the "follow a team" selector: association →
/// division → league → team, over the reference data mirrored from Spordle.
/// Every identifier returned is the one the next level expects, and the team
/// id is the Spordle team id used by the subscription endpoints. Any
/// authenticated caller may read it; only the reference data is exposed.
/// </summary>
public interface ISubscriptionCatalogService
{
    /// <summary>Associations that own at least one division, by name.</summary>
    Task<IReadOnlyList<SubscriptionAssociation>> GetAssociationsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Divisions (Spordle offices) of one association, by name.</summary>
    Task<IReadOnlyList<SubscriptionDivision>> GetDivisionsAsync(
        int associationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Leagues of one division for a season — the latest imported one when
    /// <paramref name="seasonId"/> is omitted — with their French name.
    /// </summary>
    Task<SubscriptionLeagueOptions> GetLeaguesAsync(
        int divisionId, string? seasonId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Teams of one league (Spordle category guid), by name. The identifier is
    /// the Spordle team id accepted by <see cref="IUserService"/>.
    /// </summary>
    Task<IReadOnlyList<SubscriptionTeam>> GetTeamsAsync(
        string leagueId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Teams of one association whose name starts with <paramref name="baseName"/>
    /// (case- and accent-insensitive, whole words), across every division,
    /// league and category of the latest imported season. Used by the
    /// association-president selector, where typing a base name selects all of
    /// its teams at once.
    /// </summary>
    Task<PresidentTeamOptions> GetTeamsByBaseNameAsync(
        int associationId, string baseName, CancellationToken cancellationToken = default);
}
