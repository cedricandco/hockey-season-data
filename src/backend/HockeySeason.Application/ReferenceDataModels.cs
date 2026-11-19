namespace HockeySeason.Application;

/// <summary>
/// A team as published by Spordle, reduced to what the persistence model
/// stores. The identifier is the Spordle team id.
/// </summary>
public sealed record ReferenceTeam(int Id, string Name);

/// <summary>
/// A league (Spordle category) of an association for one season, with the
/// teams the category publishes through its schedules. <see cref="Name"/> is
/// the French name, which is what the users read.
/// </summary>
/// <param name="ExternalId">Spordle category id (guid).</param>
/// <param name="Name">French display name.</param>
/// <param name="Teams">Teams published by any of the league schedules.</param>
/// <param name="Schedules">
/// Spordle schedules of the category, with the id the standings, games and
/// suspensions endpoints expect. Stored so the read tools resolve ids from the
/// database instead of calling Spordle.
/// </param>
public sealed record ReferenceLeague(
    string ExternalId,
    string Name,
    IReadOnlyList<ReferenceTeam> Teams,
    IReadOnlyList<ReferenceSchedule> Schedules);

/// <summary>A Spordle schedule of a league: the id used by the Spordle read endpoints.</summary>
public sealed record ReferenceSchedule(int Id, string Name);

/// <summary>Request of an association/season import.</summary>
/// <param name="OrganizationId">
/// Local identifier of the <c>organizations</c> row the import belongs to (the
/// association picked in the page).
/// </param>
/// <param name="DivisionExternalId">
/// <c>divisions.external_id</c> — the Spordle office number the administrator
/// typed (6193, 6194…). This is the Spordle side of the import.
/// </param>
/// <param name="SeasonId">Spordle season id, for example "2026-27".</param>
public sealed record ReferenceImportRequest(int OrganizationId, string DivisionExternalId, string SeasonId);

/// <summary>
/// Outcome of one association/season import: what was created, updated and
/// deleted, plus the non-blocking warnings of the run.
/// </summary>
public sealed record ReferenceImportResult(
    string OrganizationName,
    string OfficeName,
    string SeasonId,
    IReadOnlyList<string> CreatedLeagues,
    IReadOnlyList<string> UpdatedLeagues,
    IReadOnlyList<string> DeletedLeagues,
    IReadOnlyList<string> UnchangedLeagues,
    int CreatedTeams,
    int UpdatedTeams,
    int DeletedTeams,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Association offered by the import page: one row of the <c>organizations</c>
/// table. The division (office) is not listed here — the administrator types
/// its Spordle number, which is the <c>divisions.external_id</c>.
/// </summary>
/// <param name="Id">Local identifier.</param>
/// <param name="Name">Name of the association.</param>
/// <param name="ExternalId">Spordle identifier mirroring the association.</param>
public sealed record ReferenceOrganization(int Id, string Name, string ExternalId);

/// <summary>Season offered by the import page.</summary>
public sealed record ReferenceSeason(string ExternalId, string Name, bool IsCurrent);

/// <summary>
/// Admin use case: import the reference data of a Spordle association from the
/// public league API — the division (office), the season, every category
/// ("league", named in French) and every team. Idempotent: existing rows are
/// updated, missing ones are created and the disappearances are deleted.
/// </summary>
public interface IReferenceDataService
{
    /// <summary>
    /// Associations that can be imported: the <c>organizations</c> table.
    /// </summary>
    Task<IReadOnlyList<ReferenceOrganization>> GetOrganizationsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Seasons offered for a division (its <c>divisions.external_id</c>): the
    /// seasons Spordle publishes, completed with the seasons already imported.
    /// </summary>
    Task<IReadOnlyList<ReferenceSeason>> GetSeasonsAsync(
        int officeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the organization mirroring <paramref name="externalId"/>,
    /// creating it under <paramref name="name"/> when absent. Used by the
    /// historical <c>import/offices</c> alias, whose callers do not pass an
    /// association id.
    /// </summary>
    Task<ReferenceOrganization> EnsureOrganizationAsync(
        string externalId,
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Imports the leagues and teams of one office for one season, creating the
    /// organization, division and season rows when needed. Leagues and teams
    /// that disappeared from Spordle are deleted, together with the user
    /// subscriptions pointing at them.
    /// </summary>
    Task<ReferenceImportResult> ImportAsync(
        ReferenceImportRequest request,
        CancellationToken cancellationToken = default);
}
