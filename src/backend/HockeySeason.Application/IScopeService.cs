namespace HockeySeason.Application;

/// <summary>
/// Visible data scope of a user: the union of followed teams, the leagues
/// that own them, leagues followed directly, and the leagues of followed
/// organizations (PRESIDENT). Ids are Spordle (external) ids, the vocabulary
/// the public API and the frontend use: teams and offices are Spordle
/// integers, leagues are the Spordle category guids.
/// </summary>
/// <param name="SpordleTeamIds">Followed teams, directly or via leagues/organizations.</param>
/// <param name="SpordleLeagueIds">Leagues followed directly, owning followed teams, or inside followed organizations.</param>
/// <param name="SpordleOfficeIds">Offices (divisions) that own the visible leagues — read-only parents.</param>
/// <param name="WholeSpordleLeagueIds">
/// Leagues visible as a whole: followed directly (<c>league_subscriptions</c>)
/// or belonging to a followed association (<c>organization_subscriptions</c>).
/// Every team and every schedule of these leagues is visible, not only the
/// teams followed individually.
/// </param>
public sealed record UserScope(
    IReadOnlySet<int> SpordleTeamIds,
    IReadOnlySet<string> SpordleLeagueIds,
    IReadOnlySet<int> SpordleOfficeIds,
    IReadOnlySet<string> WholeSpordleLeagueIds)
{
    /// <summary>Empty scope: a user without any subscription.</summary>
    public static readonly UserScope Empty = new(
        new HashSet<int>(), new HashSet<string>(StringComparer.Ordinal), new HashSet<int>(),
        new HashSet<string>(StringComparer.Ordinal));

    /// <summary>
    /// Whether the league is visible as a whole, so all its teams are visible
    /// even when they are not followed individually.
    /// </summary>
    public bool FollowsWholeLeague(string leagueExternalId) =>
        WholeSpordleLeagueIds.Contains(leagueExternalId);
}

/// <summary>
/// Use case: compute the visible data scope of a user. Implementations are
/// expected to cache briefly (60 s) because the scope changes rarely.
/// </summary>
public interface IScopeService
{
    Task<UserScope> GetScopeAsync(int userId, CancellationToken cancellationToken = default);
}
