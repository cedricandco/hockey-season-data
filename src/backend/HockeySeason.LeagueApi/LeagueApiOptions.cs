using System.ComponentModel.DataAnnotations;

namespace HockeySeason.LeagueApi;

/// <summary>
/// Connection settings for the public Spordle league API.
/// Bound from configuration section "LeagueApi".
/// </summary>
public sealed class LeagueApiOptions
{
    /// <summary>Configuration section bound to these options.</summary>
    public const string SectionName = "LeagueApi";

    /// <summary>Base address of the public API.</summary>
    [Required]
    public Uri BaseAddress { get; set; } = new("https://pub-api.play.spordle.com/api/");

    /// <summary>API key sent as "API-Key" on every request.</summary>
    [Required]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Default league (schedule) identifier used when a tool call does not provide one,
    /// for example <c>194908</c> (M13 A Saison Vallée Phase 1).
    /// </summary>
    [Range(1, int.MaxValue)]
    public int DefaultLeagueId { get; set; } = 194908;

    /// <summary>
    /// Default office identifier used to list leagues, for example <c>6193</c> (Sainte-Julie).
    /// </summary>
    [Range(1, int.MaxValue)]
    public int DefaultOfficeId { get; set; } = 6193;

    /// <summary>
    /// Default season identifier used to list leagues, for example <c>2026-27</c>.
    /// </summary>
    [Required]
    public string DefaultSeasonId { get; set; } = "2026-27";

    /// <summary>
    /// Whether manual rankings published by the league should replace the computed ranking.
    /// </summary>
    public bool HonourManualRanking { get; set; }
}
