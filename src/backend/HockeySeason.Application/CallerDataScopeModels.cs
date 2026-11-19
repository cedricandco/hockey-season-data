namespace HockeySeason.Application;

/// <summary>
/// An association the caller may browse: a row of the <c>organizations</c>
/// table reached through one of their follows (a followed team, league or
/// association).
/// </summary>
public sealed record ScopedAssociation(int Id, string Name);

/// <summary>
/// A division (Spordle office) the caller may browse, with its association.
/// <see cref="Id"/> is the Spordle office number (<c>divisions.external_id</c>),
/// the value the league list is queried with.
/// </summary>
public sealed record ScopedDivision(int Id, string Name, int AssociationId);

/// <summary>
/// A league (Spordle category) the caller may browse. <see cref="ExternalId"/>
/// is the Spordle category guid; <see cref="ScheduleIds"/> lists the Spordle
/// schedule ids of the league, which the standings, games and suspensions
/// calls use.
/// </summary>
public sealed record ScopedLeague(
    int Id,
    string ExternalId,
    string Name,
    string SeasonId,
    int DivisionId,
    IReadOnlyList<int> ScheduleIds);

/// <summary>
/// A team the caller may browse. <see cref="Id"/> is the Spordle team id;
/// <see cref="LeagueExternalId"/> is the Spordle category guid of its league
/// and <see cref="ScheduleId"/> the Spordle schedule id used by the read tools.
/// </summary>
public sealed record ScopedTeam(int Id, string Name, string LeagueExternalId, int? ScheduleId);

/// <summary>
/// Data the caller may read, resolved from the database follows: the
/// associations, divisions, leagues and teams of the caller, never the whole
/// reference data. Implements "you can only ask about what you follow": a
/// followed team exposes its league and division as read-only parents, a
/// followed league exposes all its teams, and a followed association exposes
/// all its divisions and leagues.
/// </summary>
/// <remarks>
/// The ids are Spordle ids (teams and offices are integers, leagues are the
/// category guids), the vocabulary of the Spordle read endpoints. When
/// <c>Authorization:OpenAccess</c> is on, or when no database is configured,
/// <see cref="IsRestricted"/> is false and the catalogue is the whole
/// reference data — the temporary open-access phase behaves like the previous
/// unrestricted navigation.
/// </remarks>
public sealed record CallerDataScope(
    bool IsRestricted,
    IReadOnlyList<ScopedAssociation> Associations,
    IReadOnlyList<ScopedDivision> Divisions,
    IReadOnlyList<ScopedLeague> Leagues,
    IReadOnlyList<ScopedTeam> Teams)
{
    /// <summary>Everything readable (unrestricted mode) or nothing followed.</summary>
    public static CallerDataScope Unrestricted(
        IReadOnlyList<ScopedAssociation> associations,
        IReadOnlyList<ScopedDivision> divisions,
        IReadOnlyList<ScopedLeague> leagues,
        IReadOnlyList<ScopedTeam> teams) =>
        new(false, associations, divisions, leagues, teams);

    /// <summary>Nothing is followed: no association, division, league or team.</summary>
    public static CallerDataScope NothingFollowed() => new(true, [], [], [], []);

    /// <summary>Whether the caller follows the team (Spordle team id).</summary>
    public bool ContainsTeam(int teamId) => Teams.Any(team => team.Id == teamId);

    /// <summary>Whether the caller may read the league (Spordle category guid).</summary>
    public bool ContainsLeague(string leagueExternalId) =>
        Leagues.Any(league => string.Equals(league.ExternalId, leagueExternalId, StringComparison.Ordinal));

    /// <summary>Whether the caller may read the division (Spordle office number).</summary>
    public bool ContainsDivision(int divisionId) => Divisions.Any(division => division.Id == divisionId);

    /// <summary>
    /// A Spordle schedule id the caller may read: one they follow through a
    /// league, or the schedule of a team they follow. Used to validate the
    /// Spordle calls (standings, games, suspensions) the tools make.
    /// </summary>
    public bool ContainsSchedule(int scheduleId) =>
        Leagues.Any(league => league.ScheduleIds.Contains(scheduleId))
        || Teams.Any(team => team.ScheduleId == scheduleId);
}

/// <summary>
/// A Spordle schedule of a league the caller follows, with the published name
/// users read ("M13 B Saison Vallée Phase 1") and the id the standings, games
/// and suspensions calls are keyed on.
/// </summary>
public sealed record FollowedSchedule(int Id, string Name);

/// <summary>
/// A team the caller follows directly (a <c>team_subscriptions</c> row) with
/// its league, division and association. Every identifier is a database id;
/// <see cref="ExternalId"/> is the Spordle team id the live read endpoints
/// expect.
/// </summary>
public sealed record FollowedTeam(
    int Id,
    int ExternalId,
    string Name,
    int LeagueId,
    string LeagueName,
    IReadOnlyList<FollowedSchedule> Schedules,
    int DivisionId,
    string DivisionName,
    int AssociationId,
    string AssociationName);

/// <summary>
/// Use case: resolve what a caller may browse from the database
/// (association → division → league → team). This is the single source of the
/// "only what I follow" rule for the weekly report, the team page and the MCP
/// tools.
/// </summary>
public interface ICallerDataScopeService
{
    /// <summary>
    /// Reference data the user follows. <paramref name="unrestricted"/> (the
    /// temporary open-access phase) returns the whole reference data;
    /// otherwise only the follows of <paramref name="userId"/> and their
    /// read-only parents are returned.
    /// </summary>
    Task<CallerDataScope> GetAsync(
        int userId, bool unrestricted = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Teams followed directly by <paramref name="userId"/>, with their league,
    /// division and association. Deliberately independent of open access: the
    /// team page and the weekly report are about *my* teams, so they never
    /// expand to the whole reference data.
    /// </summary>
    Task<IReadOnlyList<FollowedTeam>> GetFollowedTeamsAsync(
        int userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Same as <see cref="GetAsync(int, bool, CancellationToken)"/> for a caller
    /// identified by their Auth0 subject — the shape the MCP tools have. A null
    /// subject (anonymous, or not provisioned yet) follows nothing.
    /// </summary>
    Task<CallerDataScope> GetForSubjectAsync(
        string? auth0Subject, bool unrestricted = false, CancellationToken cancellationToken = default);
}
