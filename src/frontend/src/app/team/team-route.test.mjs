import assert from 'node:assert/strict';
import test from 'node:test';
import { nextSelectionStep, sameSelection } from './team-route.ts';

const selection = (association = 0, division = 0, league = 0, team = 0) =>
  ({ association, division, league, team });

const loaded = (overrides = {}) => ({
  associations: [1, 2], divisions: [10, 20], leagues: [100, 200], teams: [500, 502],
  selection: selection(),
  ...overrides,
});

test('an unread level is fetched before anything is concluded from it', () => {
  const wanted = selection(1, 10, 100, 500);
  assert.deepEqual(nextSelectionStep(loaded({ associations: null }), wanted),
    { selection: selection(), action: 'load-associations' });
  assert.deepEqual(nextSelectionStep(loaded(), wanted),
    { selection: selection(1), action: 'load-divisions' });
  assert.deepEqual(nextSelectionStep(loaded({ selection: selection(1) }), wanted),
    { selection: selection(1, 10), action: 'load-leagues' });
  assert.deepEqual(nextSelectionStep(loaded({ selection: selection(1, 10) }), wanted),
    { selection: selection(1, 10, 100), action: 'load-teams' });
  assert.deepEqual(nextSelectionStep(loaded({ selection: selection(1, 10, 100) }), wanted),
    { selection: selection(1, 10, 100, 500), action: 'load-team' });
});

test('a level that does not exist in the follows stops the walk at the deepest valid one', () => {
  // The association is not followed: nothing is selected and nothing is fetched.
  assert.deepEqual(nextSelectionStep(loaded({ associations: [] }), selection(9, 90, 900, 500)),
    { selection: selection(), action: 'settled' });
  // The division is not one of the association's divisions.
  assert.deepEqual(nextSelectionStep(loaded({ selection: selection(1) }), selection(1, 99, 900, 500)),
    { selection: selection(1), action: 'settled' });
  // The league does not belong to the division.
  assert.deepEqual(nextSelectionStep(loaded({ selection: selection(1, 10) }), selection(1, 10, 999, 500)),
    { selection: selection(1, 10), action: 'settled' });
  // The team does not play in the league: the league is shown, not the team.
  assert.deepEqual(nextSelectionStep(loaded({ selection: selection(1, 10, 100) }), selection(1, 10, 100, 999)),
    { selection: selection(1, 10, 100), action: 'settled' });
});

test('an already applied selection asks for nothing', () => {
  const applied = selection(1, 10, 100, 500);
  assert.deepEqual(nextSelectionStep(loaded({ selection: applied }), applied),
    { selection: applied, action: 'settled' });
  assert.equal(sameSelection(applied, selection(1, 10, 100, 500)), true);
  assert.equal(sameSelection(applied, selection(1, 10, 100, 502)), false);
});

test('an empty URL clears every level, even when the page shows a team', () => {
  assert.deepEqual(nextSelectionStep(loaded({ selection: selection(1, 10, 100, 500) }), selection()),
    { selection: selection(), action: 'settled' });
  // Dropping only the team keeps the league: the selector stays where it was.
  assert.deepEqual(nextSelectionStep(loaded({ selection: selection(1, 10, 100, 500) }), selection(1, 10, 100)),
    { selection: selection(1, 10, 100), action: 'settled' });
});
