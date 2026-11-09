using System.Text.Json.Serialization;

namespace HockeySeason.LeagueApi.Http;

/// <summary>
/// Wire format of the Spordle pub API payloads. Names match the upstream JSON
/// (case-insensitive binding); abbreviations such as diff/pim are mapped to
/// idiomatic domain names by the mappers.
/// </summary>
internal sealed record ScheduleRowDto
{
    public required int Id { get; init; }

    public required string Name { get; init; }
}

internal sealed record StandingsRowDto
{
    public required int TeamId { get; init; }

    public required int Ranking { get; init; }

    [JsonPropertyName("manualRanking")]
    public int? ManualRanking { get; init; }

    [JsonPropertyName("gamesPlayed")]
    public required int GamesPlayed { get; init; }

    public required int Wins { get; init; }

    public required int Losses { get; init; }

    public required int Ties { get; init; }

    public required int Points { get; init; }

    [JsonPropertyName("goalFor")]
    public required int GoalsFor { get; init; }

    [JsonPropertyName("goalAgainst")]
    public required int GoalsAgainst { get; init; }

    [JsonPropertyName("diff")]
    public required int GoalDifferential { get; init; }

    [JsonPropertyName("pim")]
    public required int PenaltyMinutes { get; init; }

    [JsonPropertyName("powerplayPct")]
    public required double PowerPlayPercentage { get; init; }

    [JsonPropertyName("penaltyKillPct")]
    public required double PenaltyKillPercentage { get; init; }

    public required StandingsTeamDto Team { get; init; }
}

internal sealed record StandingsTeamDto
{
    public required int Id { get; init; }

    public required string Name { get; init; }

    [JsonPropertyName("shortName")]
    public required string ShortName { get; init; }

    public required string Division { get; init; }

    public required string Gender { get; init; }
}

internal sealed record GameRowDto
{
    public required int Id { get; init; }

    public required string Date { get; init; }

    public required string StartTime { get; init; }

    public string? TimeZone { get; init; }

    public required int HomeTeamId { get; init; }

    public required int AwayTeamId { get; init; }

    public required GameTeamDto HomeTeam { get; init; }

    public required GameTeamDto AwayTeam { get; init; }

    /// <summary>Goals per team identifier. Absent until the game is played.</summary>
    public Dictionary<string, int> Score { get; init; } = new(StringComparer.Ordinal);
}

internal sealed record GameTeamDto
{
    public required int Id { get; init; }

    public required string Name { get; init; }

    [JsonPropertyName("logoUrl")]
    public string? LogoUrl { get; init; }
}

internal sealed record SuspensionRowDto
{
    public required string Id { get; init; }

    public required string Status { get; init; }

    /// <summary>Absent for team-official suspensions.</summary>
    public SuspensionParticipantDto? Participant { get; init; }

    public required SuspensionTeamDto Team { get; init; }

    public required string Code { get; init; }

    public required int GameId { get; init; }

    public required string GameNumber { get; init; }

    public required int RequiredGames { get; init; }

    public IReadOnlyList<int> PurgedGames { get; init; } = [];
}

internal sealed record SuspensionParticipantDto
{
    public required string FullName { get; init; }

    public required int Number { get; init; }

    public required string Position { get; init; }
}

internal sealed record SuspensionTeamDto
{
    public required int Id { get; init; }

    public required string Name { get; init; }
}

internal sealed record GameDetailRowDto
{
    public required int Id { get; init; }

    public required string Number { get; init; }

    public required string Date { get; init; }

    public required string StartTime { get; init; }

    public string? TimeZone { get; init; }

    public required string Division { get; init; }

    public required string SeasonId { get; init; }

    /// <summary>The single-game resource omits the member entirely, so it defaults to false.</summary>
    public bool IsLive { get; init; }

    public required bool IsApproved { get; init; }

    public required GameTeamDto HomeTeam { get; init; }

    public required GameTeamDto AwayTeam { get; init; }

    public IReadOnlyList<GameTeamStatsRowDto> TeamStats { get; init; } = [];
}

internal sealed record GameTeamStatsRowDto
{
    public required int TeamId { get; init; }

    [JsonPropertyName("goalFor")]
    public required int GoalsFor { get; init; }

    [JsonPropertyName("goalAgainst")]
    public required int GoalsAgainst { get; init; }

    [JsonPropertyName("gameResult")]
    public required string GameResult { get; init; }

    [JsonPropertyName("winLossType")]
    public required string WinLossType { get; init; }

    [JsonPropertyName("pim")]
    public required int PenaltyMinutes { get; init; }

    [JsonPropertyName("ppg")]
    public required int PowerPlayGoals { get; init; }

    [JsonPropertyName("ppo")]
    public required int PowerPlayOpportunities { get; init; }

    [JsonPropertyName("pk")]
    public required int PowerPlayGoalsAgainst { get; init; }

    [JsonPropertyName("pko")]
    public required int TimesShorthanded { get; init; }
}

internal sealed record BoxScoreRowDto
{
    public required string GameNumber { get; init; }

    public IReadOnlyList<GoalRowDto> Goals { get; init; } = [];

    public IReadOnlyList<PenaltyRowDto> Penalties { get; init; } = [];
}

internal sealed record GoalRowDto
{
    public required GameTimeRowDto GameTime { get; init; }

    public required int TeamId { get; init; }

    public required ParticipantRowDto Participant { get; init; }

    public IReadOnlyList<ParticipantRowDto> Assists { get; init; } = [];

    public required bool IsPowerplay { get; init; }

    public required bool IsShorthanded { get; init; }

    public required bool IsEmptyNet { get; init; }

    public required bool IsPenaltyShot { get; init; }

    /// <summary>Null when the member is published but carries no value.</summary>
    public bool? IsOwnGoal { get; init; }
}

internal sealed record PenaltyRowDto
{
    public required GameTimeRowDto GameTime { get; init; }

    public required int TeamId { get; init; }

    public required string Infraction { get; init; }

    public required DurationRowDto Duration { get; init; }

    public required ParticipantRowDto Participant { get; init; }

    public required bool IsBenchPenalty { get; init; }

    public PenaltyI18nRowDto? I18n { get; init; }
}

internal sealed record DurationRowDto
{
    public required string Name { get; init; }
}

internal sealed record PenaltyI18nRowDto
{
    public PenaltyI18nFrRowDto? Fr { get; init; }
}

internal sealed record PenaltyI18nFrRowDto
{
    public string? Infraction { get; init; }
}

internal sealed record GameTimeRowDto
{
    public required string Period { get; init; }

    public required int ElapsedMinutes { get; init; }

    public required int ElapsedSeconds { get; init; }
}

internal sealed record ParticipantRowDto
{
    public required string FullName { get; init; }

    public required int Number { get; init; }

    public required int ParticipantId { get; init; }

    public required bool IsAffiliate { get; init; }
}
