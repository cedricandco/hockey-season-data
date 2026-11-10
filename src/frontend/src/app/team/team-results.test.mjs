import assert from 'node:assert/strict';
import test from 'node:test';
import { recentGames, teamResult } from './team-results.ts';

const game = (id, homeScore, awayScore) => ({
  id, homeTeamId: 1, awayTeamId: 2, homeScore, awayScore,
});

test('shows at most five games with the most recent first', () => {
  const games = Array.from({ length: 7 }, (_, index) => ({ game: game(index + 1, 1, 0) }));
  assert.deepEqual(recentGames(games).map(item => item.id), [7, 6, 5, 4, 3]);
  assert.deepEqual(games.map(item => item.game.id), [1, 2, 3, 4, 5, 6, 7]);
});

test('results use the selected team and preserve draws and missing scores', () => {
  assert.equal(teamResult(game(1, 4, 3), 1), 'TEAM.WIN');
  assert.equal(teamResult(game(1, 4, 3), 2), 'TEAM.LOSS');
  assert.equal(teamResult(game(1, 3, 3), 2), 'TEAM.TIE');
  assert.equal(teamResult(game(1, null, null), 1), 'TEAM.UNKNOWN');
});
