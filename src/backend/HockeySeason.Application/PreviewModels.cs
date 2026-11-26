namespace HockeySeason.Application;

/// <summary>
/// A team offered by the anonymous preview of the landing page.
/// <see cref="Id"/> is the database id (<c>teams.id</c>) the preview detail is
/// read with, so the visitor never has to handle a Spordle identifier.
/// </summary>
public sealed record PreviewTeam(int Id, string Name);

/// <summary>
/// Database id of a team of a league, keyed on its Spordle id: the preview
/// detail returns the same <c>teamLinks</c> bridge as the team page, so the
/// shared statistics component can map the score lines' Spordle ids.
/// </summary>
public sealed record PreviewTeamLink(int TeamId, int ExternalId);

/// <summary>
/// Anonymous read model of the landing-page preview: the reference data a
/// visitor without an account may browse (association → division → league →
/// team), plus the detail of one picked team.
/// </summary>
/// <remarks>
/// It is deliberately independent of <see cref="ICallerDataScopeService"/>:
/// the preview is not tied to a caller, so it has no follows to restrict it
/// to. The three attempts of a visitor are tracked by the browser
/// (<c>localStorage</c>), not by this service.
/// </remarks>
public interface IPreviewService
{
    /// <summary>
    /// Teams of one league (Spordle category guid) in its latest imported
    /// season, keyed on the database id the preview detail expects.
    /// <paramref name="divisionId"/> pins the league when the category is
    /// played by several divisions (the guid alone cannot tell them apart).
    /// </summary>
    Task<IReadOnlyList<PreviewTeam>> GetTeamsAsync(
        string leagueId, int? divisionId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// One team the preview can open by database id, with the published
    /// schedules of its league the detail call tries. Null when the team does
    /// not exist or carries no Spordle id.
    /// </summary>
    Task<SelectableTeam?> GetTeamAsync(int teamId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every team of one league, keyed on both ids: the Spordle id carried by
    /// the games and the database id the preview navigates with.
    /// </summary>
    Task<IReadOnlyList<PreviewTeamLink>> GetLeagueTeamLinksAsync(
        int leagueId, CancellationToken cancellationToken = default);
}
