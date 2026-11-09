namespace HockeySeason.LeagueApi.Models;

/// <summary>
/// A league (schedule) published by the league office, for example "M13 A Saison Vallée Phase 1".
/// </summary>
public sealed record League(int Id, string Name);

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
    double PenaltyKillPercentage);

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
    int? AwayScore);

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
