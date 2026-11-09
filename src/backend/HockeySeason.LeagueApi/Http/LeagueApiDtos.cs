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
