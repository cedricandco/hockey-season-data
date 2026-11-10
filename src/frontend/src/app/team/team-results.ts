import type { TeamGame } from './team.models';

export function recentGames(games: readonly { game: TeamGame }[]): TeamGame[] {
  return games.slice(-5).reverse().map(item => item.game);
}

export function teamResult(game: TeamGame, teamId: number): string {
  if (game.homeScore === null || game.awayScore === null) return 'TEAM.UNKNOWN';
  const difference = (game.homeScore - game.awayScore) * (game.homeTeamId === teamId ? 1 : -1);
  return difference > 0 ? 'TEAM.WIN' : difference < 0 ? 'TEAM.LOSS' : 'TEAM.TIE';
}
