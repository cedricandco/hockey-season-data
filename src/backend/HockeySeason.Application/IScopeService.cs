namespace HockeySeason.Application;

/// <summary>
/// Visible data scope of a user: the union of followed teams, the leagues
/// that own them, leagues followed directly, and the leagues of followed
/// organizations (PRESIDENT). Ids are Spordle (external) ids, the vocabulary
/// the public API and the frontend use.
/// </summary>
/// <param name="SpordleTeamIds">Followed teams, directly or via leagues/organizations.</param>
/// <param name="SpordleLeagueIds">Leagues followed directly, owning followed teams, or inside followed organizations.</param>
/// <param name="SpordleOfficeIds">Offices (divisions) that own the visible leagues — read-only parents.</param>
public sealed record UserScope(
    IReadOnlySet<int> SpordleTeamIds,
    IReadOnlySet<int> SpordleLeagueIds,
    IReadOnlySet<int> SpordleOfficeIds)
{
    /// <summary>Empty scope: a user without any subscription.</summary>
    public static readonly UserScope Empty = new(
        new HashSet<int>(), new HashSet<int>(), new HashSet<int>());
}

/// <summary>
/// Use case: compute the visible data scope of a user. Implementations are
/// expected to cache briefly (60 s) because the scope changes rarely.
/// </summary>
public interface IScopeService
{
    Task<UserScope> GetScopeAsync(int userId, CancellationToken cancellationToken = default);
}
