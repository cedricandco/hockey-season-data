namespace HockeySeason.LeagueApi.Models;

/// <summary>
/// A league (schedule) published by the league office, for example "M13 A Saison Vallée Phase 1".
/// </summary>
public sealed record League(int Id, string Name);

/// <summary>
/// A season published by a league office, for example "2026-27". The
/// identifier is the value expected by the <c>seasonId</c> query parameter of
/// the schedules, categories and seasons endpoints.
/// </summary>
public sealed record LeagueSeason(string SeasonId, string Name, bool IsCurrent);

/// <summary>
/// An office of the Spordle organization tree: the association (region), a
/// league office ("division" in the application model) or a club. The parent
/// identifier is what links an office to its association.
/// </summary>
public sealed record LeagueOfficeDetail(int Id, string Name, string? Type, int? ParentId);

/// <summary>
/// A category of an office and season, for example "M13 B". Categories are the
/// season-independent level that groups the schedules (one category is played
/// in several schedules, such as "Rives" and "Vallée").
/// <see cref="Name"/> is the published name (English) and <see cref="NameFr"/>
/// the French one, when the office publishes it.
/// </summary>
public sealed record LeagueCategory(string Id, string Name, string? NameFr)
{
    /// <summary>Display name: the French label when published, the English one otherwise.</summary>
    public string FrenchName => string.IsNullOrWhiteSpace(NameFr) ? Name : NameFr;
}

/// <summary>
/// One row of a league standings table.
/// </summary>
public sealed record LeagueStanding(
    int TeamId,
    string TeamName,
    string ShortName,
    string Division,
    string Gender,
    int Ranking,
    int GamesPlayed,
    int Wins,
    int Losses,
    int Ties,
    int Points,
    int GoalsFor,
    int GoalsAgainst,
    int GoalDifferential,
    int PenaltyMinutes,
    double PowerPlayPercentage,
    double PenaltyKillPercentage,
    int? GroupId = null);

/// <summary>
/// A team belonging to a league, as published by the standings endpoint.
/// </summary>
public sealed record LeagueTeam(int Id, string Name);

/// <summary>
/// A game played (or scheduled) in a league, reduced to scoreboard figures.
/// </summary>
public sealed record LeagueGame(
    int Id,
    string Date,
    string Time,
    int HomeTeamId,
    string HomeTeamName,
    string? HomeTeamLogoUrl,
    int AwayTeamId,
    string AwayTeamName,
    string? AwayTeamLogoUrl,
    int? HomeScore,
    int? AwayScore,
    bool IsLive = false);

/// <summary>
/// A suspension published by the league for one of its games.
/// </summary>
public sealed record LeagueSuspension(
    string Id,
    string Status,
    string? PlayerName,
    int? PlayerNumber,
    string? PlayerPosition,
    int TeamId,
    string TeamName,
    string Code,
    int GameId,
    string GameNumber,
    int RequiredGames,
    int PurgedGames);

/// <summary>
/// A team as it appears on a game scoreboard.
/// </summary>
public sealed record LeagueGameTeam(
    int Id,
    string Name,
    string? LogoUrl);

/// <summary>
/// One team's line on a game scoreboard.
/// </summary>
public sealed record LeagueGameTeamStats(
    int TeamId,
    int GoalsFor,
    int GoalsAgainst,
    string Result,
    string WinLossType,
    int PenaltyMinutes,
    string PowerPlay,
    string PenaltyKill);

/// <summary>
/// A person involved in a game event, such as the scorer or a penalized player.
/// <see cref="Number"/> is null when the league publishes no jersey number,
/// for example when a coach is penalized.
/// </summary>
public sealed record LeagueGameParticipant(
    string Name,
    int? Number,
    bool IsAffiliate,
    int ParticipantId = 0);

/// <summary>
/// A goal, placed on the game clock.
/// </summary>
public sealed record LeagueGameGoal(
    int TeamId,
    int Period,
    string Time,
    LeagueGameParticipant Scorer,
    IReadOnlyList<LeagueGameParticipant> Assists,
    bool IsPowerPlay,
    bool IsShorthanded,
    bool IsEmptyNet,
    bool IsPenaltyShot);

/// <summary>
/// Number of regulation or overtime goals scored by one team in one period.
/// </summary>
public sealed record LeagueGamePeriodGoals(
    int TeamId,
    int Period,
    int Goals);

/// <summary>
/// A penalty, placed on the game clock.
/// </summary>
public sealed record LeagueGamePenalty(
    int TeamId,
    int Period,
    string Time,
    LeagueGameParticipant Player,
    string Infraction,
    string Duration,
    bool IsBenchPenalty);

/// <summary>
/// The full box score of one game: both teams, scoreboard figures per team,
/// every goal and every penalty.
/// </summary>
public sealed record LeagueGameDetail(
    int Id,
    string Number,
    string Date,
    string Time,
    string Division,
    string SeasonId,
    bool IsLive,
    bool IsApproved,
    LeagueGameTeam HomeTeam,
    LeagueGameTeam AwayTeam,
    IReadOnlyList<LeagueGameTeamStats> TeamStats,
    IReadOnlyList<LeagueGamePeriodGoals> PeriodGoals,
    IReadOnlyList<LeagueGameGoal> Goals,
    IReadOnlyList<LeagueGamePenalty> Penalties);
