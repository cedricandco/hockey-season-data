using System.Text.Json;

namespace HockeySeason.LeagueApi.Http;

public sealed partial class LeagueApiService
{
    internal Task<JsonElement> GetRawStandingsAsync(int leagueId, CancellationToken cancellationToken) =>
        GetJsonAsync<JsonElement>(
            $"sp/schedules/{leagueId}/teamStats?filter={Uri.EscapeDataString(TeamStatsFilter)}",
            cancellationToken);

    internal Task<JsonElement> GetRawGamesAsync(
        int leagueId, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken) =>
        GetJsonAsync<JsonElement>(
            $"sp/games?filter={Uri.EscapeDataString(BuildGamesFilter(leagueId, null, startDate, endDate))}",
            cancellationToken);

    internal Task<JsonElement> GetRawSuspensionsAsync(int leagueId, CancellationToken cancellationToken) =>
        GetJsonAsync<JsonElement>($"sp/schedules/{leagueId}/suspensions", cancellationToken);
}
