using HockeySeason.LeagueApi.Http;
using HockeySeason.LeagueApi.Models;

namespace HockeySeason.LeagueApi;

/// <summary>One of the supported Spordle league offices.</summary>
public sealed record LeagueOffice(int Id, string Name);

public static class LeagueOffices
{
    public static IReadOnlyList<LeagueOffice> All { get; } =
    [
        new(6193, "Hockey Richelieu Simple Lettre"),
        new(6194, "Hockey Richelieu Double Lettre"),
    ];
}

/// <summary>Points accumulated from published game events.</summary>
public sealed record TeamPlayerStatistics(int ParticipantId, string Name, int Number, int Goals, int Assists, int Points);

/// <summary>A completed game and its full box score.</summary>
public sealed record TeamPlayedGame(LeagueGame Game, LeagueGameDetail Detail);

/// <summary>Team standings, games and player statistics for a single schedule.</summary>
public sealed record TeamDetailResponse(
    int LeagueId, LeagueStanding Team, IReadOnlyList<LeagueStanding> Standings,
    IReadOnlyList<TeamPlayedGame> Games, IReadOnlyList<LeagueGame> UpcomingGames,
    IReadOnlyList<TeamPlayerStatistics> Players, int TotalGoals, int TotalAssists, int TotalPoints,
    IReadOnlyList<TeamPlayerStatistics> TopScorers, IReadOnlyList<TeamPlayerStatistics> TopAssistProviders,
    int PowerPlayGoals, int ShorthandedGoals, int Penalties, bool CompleteRosterAvailable);

public interface ITeamDetailService
{
    Task<TeamDetailResponse?> GetAsync(int leagueId, int teamId, CancellationToken cancellationToken);
}

public sealed class TeamDetailService(LeagueApiService league) : ITeamDetailService
{
    public async Task<TeamDetailResponse?> GetAsync(int leagueId, int teamId, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(leagueId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(teamId);
        var standings = await league.GetLeagueStandingsAsync(leagueId, cancellationToken);
        var team = standings.FirstOrDefault(row => row.TeamId == teamId);
        if (team is null) return null;

        var games = await league.GetAllTeamGamesAsync(leagueId, teamId, cancellationToken);
        var localNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("America/Toronto"));
        var today = DateOnly.FromDateTime(localNow.DateTime);
        var played = games.Where(game => !game.IsLive && game.HomeScore.HasValue && game.AwayScore.HasValue &&
            DateOnly.ParseExact(game.Date, "yyyy-MM-dd") <= today).ToArray();
        using var limit = new SemaphoreSlim(6);
        var details = await Task.WhenAll(played.Select(async game =>
        {
            await limit.WaitAsync(cancellationToken);
            try { return new TeamPlayedGame(game, await league.GetLeagueGameDetailAsync(game.Id, cancellationToken)); }
            finally { limit.Release(); }
        }));
        var completed = details.Where(item => !item.Detail.IsLive).ToArray();
        var players = new Dictionary<int, (LeagueGameParticipant Player, int Goals, int Assists)>();
        foreach (var item in completed)
        {
            foreach (var goal in item.Detail.Goals.Where(goal => goal.TeamId == teamId))
            {
                Add(goal.Scorer, true);
                foreach (var assist in goal.Assists) Add(assist, false);
            }
            foreach (var penalty in item.Detail.Penalties.Where(penalty => penalty.TeamId == teamId && !penalty.IsBenchPenalty))
                Add(penalty.Player, null);
        }
        void Add(LeagueGameParticipant participant, bool? goal)
        {
            if (participant.ParticipantId <= 0)
                throw new InvalidDataException("A game event has no valid participant identifier.");
            players.TryGetValue(participant.ParticipantId, out var current);
            players[participant.ParticipantId] = (participant, current.Goals + (goal == true ? 1 : 0),
                current.Assists + (goal == false ? 1 : 0));
        }
        var rankings = players.Values.Select(p => new TeamPlayerStatistics(p.Player.ParticipantId, p.Player.Name,
                p.Player.Number, p.Goals, p.Assists, p.Goals + p.Assists))
            .OrderByDescending(p => p.Points).ThenByDescending(p => p.Goals)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        var goals = completed.SelectMany(item => item.Detail.Goals).Where(goal => goal.TeamId == teamId).ToArray();
        return new TeamDetailResponse(leagueId, team,
            standings.Where(row => row.Division == team.Division && row.GroupId == team.GroupId).ToArray(), completed,
            games.Where(game => !game.HomeScore.HasValue && !game.AwayScore.HasValue &&
                DateTime.ParseExact($"{game.Date} {game.Time}", "yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture)
                    >= localNow.DateTime).ToArray(),
            rankings, rankings.Sum(p => p.Goals), rankings.Sum(p => p.Assists), rankings.Sum(p => p.Points),
            Leaders(rankings, player => player.Goals), Leaders(rankings, player => player.Assists),
            goals.Count(goal => goal.IsPowerPlay), goals.Count(goal => goal.IsShorthanded),
            completed.Sum(item => item.Detail.Penalties.Count(penalty => penalty.TeamId == teamId)), false);
    }

    private static IReadOnlyList<TeamPlayerStatistics> Leaders(
        TeamPlayerStatistics[] players, Func<TeamPlayerStatistics, int> value)
    {
        var maximum = players.Length == 0 ? 0 : players.Max(value);
        return maximum == 0 ? [] : players.Where(player => value(player) == maximum).ToArray();
    }
}
