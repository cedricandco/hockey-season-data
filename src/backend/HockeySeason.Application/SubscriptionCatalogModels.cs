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
/// A category level of an association — "M11", "M13", "M15", "M17", "M19" or
/// "Junior" — offered by the category-director selector. <see cref="Id"/> is
/// the level itself, the value the team list of the category is queried with;
/// the letters that refine a category ("M13 A", "M13 B", "M13 AA") are ignored,
/// which is the whole point of the level.
/// </summary>
public sealed record DirectorCategory(string Id, string Name, int TeamCount);

/// <summary>
/// A team a category director may follow by picking its category level, with
/// the league and division it plays in so the page can list it without another
/// call. <see cref="Id"/> is the Spordle team id, the value the team
/// subscription endpoints expect.
/// </summary>
public sealed record DirectorTeam(int Id, string Name, string LeagueName, string DivisionName);

/// <summary>
/// Teams of one category level of an association in the latest imported season
/// ("M13" → every M13 team of every division, whatever the letter), optionally
/// narrowed to one team name: a category director follows one name per
/// category, so <see cref="BaseName"/> is the filter that was applied (null
/// when the whole level is listed).
/// </summary>
public sealed record DirectorCategoryOptions(
    int AssociationId,
    string AssociationName,
    string Category,
    string? BaseName,
    IReadOnlyList<DirectorTeam> Teams);

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
    /// <paramref name="divisionId"/> pins the league when the category is
    /// played by several divisions (the guid alone cannot tell them apart).
    /// </summary>
    Task<IReadOnlyList<SubscriptionTeam>> GetTeamsAsync(
        string leagueId, int? divisionId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Teams of one association whose name starts with <paramref name="baseName"/>
    /// (case- and accent-insensitive, whole words), across every division,
    /// league and category of the latest imported season. Used by the
    /// association-president selector, where typing a base name selects all of
    /// its teams at once.
    /// </summary>
    Task<PresidentTeamOptions> GetTeamsByBaseNameAsync(
        int associationId, string baseName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Category levels ("M11", "M13", …, "Junior") the association publishes in
    /// its latest imported season, with the number of teams of each. Used by the
    /// category-director selector, where picking a level selects every letter
    /// of the category at once.
    /// </summary>
    Task<IReadOnlyList<DirectorCategory>> GetCategoriesAsync(
        int associationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Teams of one category level of the association in its latest imported
    /// season, across every division and league: "M13" selects the M13 A, M13 B
    /// and M13 AA teams alike, because the letter is a refinement of the
    /// category, not a different one. <paramref name="baseName"/> narrows the
    /// result to the teams whose name starts with it — a category director
    /// follows one team name per category, never its whole roster.
    /// </summary>
    Task<DirectorCategoryOptions> GetTeamsByCategoryAsync(
        int associationId, string category, string? baseName = null, CancellationToken cancellationToken = default);
}
