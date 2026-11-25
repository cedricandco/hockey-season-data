import assert from 'node:assert/strict';
import test from 'node:test';
import { gameSides, nextGames, opponentName, opponentTeamId, pageTeamId, playedGames, recentGames, teamResult } from './team-results.ts';

const game = (id, homeScore, awayScore) => ({
  id, homeTeamId: 1, awayTeamId: 2, homeScore, awayScore,
});

test('shows at most five games with the most recent first', () => {
  const games = Array.from({ length: 7 }, (_, index) => ({ game: game(index + 1, 1, 0) }));
  assert.deepEqual(recentGames(games).map(item => item.id), [7, 6, 5, 4, 3]);
  assert.deepEqual(games.map(item => item.game.id), [1, 2, 3, 4, 5, 6, 7]);
});

test('every played game is available for paging, most recent first', () => {
  const games = Array.from({ length: 7 }, (_, index) => ({ game: game(index + 1, 1, 0) }));
  assert.deepEqual(playedGames(games).map(item => item.id), [7, 6, 5, 4, 3, 2, 1]);
  assert.deepEqual(recentGames(games, 10).map(item => item.id), [7, 6, 5, 4, 3, 2, 1]);
  assert.deepEqual(recentGames([], 5), []);
});

test('only the next two games of the schedule are announced', () => {
  const upcoming = [game(20, null, null), game(21, null, null), game(22, null, null)];
  assert.deepEqual(nextGames(upcoming).map(item => item.id), [20, 21]);
  assert.deepEqual(nextGames([], 2), []);
});

test('the opponent is resolved on both sides of a score line', () => {
  const line = { ...game(1, 3, 2), homeTeamName: 'Alpha', awayTeamName: 'Beta' };
  assert.equal(opponentTeamId(line, 1), 2);
  assert.equal(opponentTeamId(line, 2), 1);
  assert.equal(opponentName(line, 1), 'Beta');
  assert.equal(opponentName(line, 2), 'Alpha');
  // A score line of another team never links a team to itself.
  assert.equal(opponentTeamId(line, 9), null);
});

test('a Spordle team id maps to the database id of the team page, 0 when unknown', () => {
  const links = [{ externalTeamId: 181303, pageTeamId: 500 }, { externalTeamId: 181305, pageTeamId: 502 }];
  assert.equal(pageTeamId(links, 181303), 500);
  assert.equal(pageTeamId(links, 181305), 502);
  assert.equal(pageTeamId(links, 999999), 0);
  assert.equal(pageTeamId([], 181303), 0);
});

test('both sides of a score line know their own team and the linkable opponent', () => {
  const links = [{ externalTeamId: 2, pageTeamId: 502 }];
  const line = { ...game(1, 3, 2), homeTeamId: 1, awayTeamId: 2, homeTeamName: 'Alpha', awayTeamName: 'Beta' };

  // Our own team is never linked, even when the mapping knows it.
  assert.deepEqual(gameSides(line, 1, [...links, { externalTeamId: 1, pageTeamId: 500 }]), [
    { name: 'Alpha', own: true, pageTeamId: 0 },
    { name: 'Beta', own: false, pageTeamId: 502 },
  ]);
  // An opponent the league mapping does not know stays plain text.
  assert.deepEqual(gameSides(line, 1, []), [
    { name: 'Alpha', own: true, pageTeamId: 0 },
    { name: 'Beta', own: false, pageTeamId: 0 },
  ]);
});

test('results use the selected team and preserve draws and missing scores', () => {
  assert.equal(teamResult(game(1, 4, 3), 1), 'TEAM.WIN');
  assert.equal(teamResult(game(1, 4, 3), 2), 'TEAM.LOSS');
  assert.equal(teamResult(game(1, 3, 3), 2), 'TEAM.TIE');
  assert.equal(teamResult(game(1, null, null), 1), 'TEAM.UNKNOWN');
});
