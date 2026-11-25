import type { TeamGame, TeamPageLink } from './team.models';

/** Games shown first in the "last games" block, and added by each "show more". */
export const RECENT_PAGE_SIZE = 5;

/** Upcoming games the team page announces. */
export const UPCOMING_COUNT = 2;

/** Every played game, most recent first. */
export function playedGames(games: readonly { game: TeamGame }[]): TeamGame[] {
  return games.map(item => item.game).reverse();
}

/** The most recent games, at most `count` of them. */
export function recentGames(games: readonly { game: TeamGame }[], count = RECENT_PAGE_SIZE): TeamGame[] {
  return playedGames(games).slice(0, count);
}

/** The next games of the schedule, in playing order, at most `count` of them. */
export function nextGames(upcoming: readonly TeamGame[], count = UPCOMING_COUNT): TeamGame[] {
  return upcoming.slice(0, count);
}

/**
 * Spordle team id of the opponent of `teamId` in `game`, or null when the score
 * line does not belong to that team (the API never does, but the page must not
 * link a team to itself).
 */
export function opponentTeamId(game: TeamGame, teamId: number): number | null {
  if (game.homeTeamId === teamId) return game.awayTeamId;
  if (game.awayTeamId === teamId) return game.homeTeamId;
  return null;
}

export function opponentName(game: TeamGame, teamId: number): string {
  return game.homeTeamId === teamId ? game.awayTeamName : game.homeTeamName;
}

/**
 * Database id (`teams.id`) of a team given its Spordle id. 0 when the team is
 * not part of the opened league, in which case the page must not link to it.
 */
export function pageTeamId(links: readonly TeamPageLink[], externalTeamId: number): number {
  return links.find(link => link.externalTeamId === externalTeamId)?.pageTeamId ?? 0;
}

/** One side of a score line, as the team page renders it. */
export interface GameSide {
  /** Team name, as the league publishes it. */
  name: string;
  /** True for the team of the page: it is highlighted, never linked. */
  own: boolean;
  /** Database id of the opponent's team page; 0 for our own team or an unmapped one. */
  pageTeamId: number;
}

/**
 * Both sides of a score line in playing order (home, then away), each knowing
 * whether it is the team of the page and whether it can be opened: only the
 * opponent is linked, and only when the league mapping knows it.
 */
export function gameSides(game: TeamGame, teamId: number, links: readonly TeamPageLink[]): [GameSide, GameSide] {
  const side = (name: string, id: number): GameSide => id === teamId
    ? { name, own: true, pageTeamId: 0 }
    : { name, own: false, pageTeamId: pageTeamId(links, id) };
  return [side(game.homeTeamName, game.homeTeamId), side(game.awayTeamName, game.awayTeamId)];
}

export function teamResult(game: TeamGame, teamId: number): string {
  if (game.homeScore === null || game.awayScore === null) return 'TEAM.UNKNOWN';
  const difference = (game.homeScore - game.awayScore) * (game.homeTeamId === teamId ? 1 : -1);
  return difference > 0 ? 'TEAM.WIN' : difference < 0 ? 'TEAM.LOSS' : 'TEAM.TIE';
}
