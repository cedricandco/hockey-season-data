using HockeySeason.LeagueApi;
using HockeySeason.LeagueApi.Models;

namespace HockeySeason.Functions;

/// <summary>
/// Database id (<c>teams.id</c>) of a team of the league, keyed on the Spordle
/// team id used by the games of a box score.
/// </summary>
public sealed record TeamPageLink(int ExternalTeamId, int PageTeamId);

/// <summary>
/// Detail of a team read by the team page: the same figures and JSON shape as
/// <see cref="TeamDetailResponse"/>, plus <see cref="TeamLinks"/>.
/// </summary>
/// <remarks>
/// The page selects teams with database ids but the games carry Spordle team
/// ids, so the mapping is what lets a score line of the last or next games open
/// the opponent's page without the client ever handling a Spordle id.
/// </remarks>
public sealed record TeamPageDetailResponse(
    int LeagueId,
    LeagueStanding Team,
    IReadOnlyList<LeagueStanding> Standings,
    IReadOnlyList<TeamPlayedGame> Games,
    IReadOnlyList<LeagueGame> UpcomingGames,
    IReadOnlyList<TeamPlayerStatistics> Players,
    int TotalGoals,
    int TotalAssists,
    int TotalPoints,
    int PowerPlayGoals,
    int ShorthandedGoals,
    int Penalties,
    bool CompleteRosterAvailable,
    IReadOnlyList<TeamPageLink> TeamLinks)
{
    public static TeamPageDetailResponse From(TeamDetailResponse detail, IReadOnlyList<TeamPageLink> teamLinks) => new(
        detail.LeagueId,
        detail.Team,
        detail.Standings,
        detail.Games,
        detail.UpcomingGames,
        detail.Players,
        detail.TotalGoals,
        detail.TotalAssists,
        detail.TotalPoints,
        detail.PowerPlayGoals,
        detail.ShorthandedGoals,
        detail.Penalties,
        detail.CompleteRosterAvailable,
        teamLinks);
}
