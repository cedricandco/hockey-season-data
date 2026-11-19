import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  alreadyFollowed,
  canAddTeam,
  canFollowMoreTeams,
  canLoadDivisions,
  canLoadLeagues,
  canLoadTeams,
  formatUsage,
  isRemovable,
  limitOf,
} from './subscription-helpers.ts';

const limit = (kind, max, used) => ({ kind, max, used, canAddMore: max === null || used < max });

test('canFollowMoreTeams follows the licence headroom of the team kind', () => {
  assert.equal(canFollowMoreTeams({ limits: [limit('team', 5, 2)] }), true);
  assert.equal(canFollowMoreTeams({ limits: [limit('team', 5, 5)] }), false);
  // No COACH licence: the team kind is capped at 0.
  assert.equal(canFollowMoreTeams({ limits: [limit('team', 0, 0)] }), false);
  // Unlimited (open access / president): adding stays possible.
  assert.equal(canFollowMoreTeams({ limits: [limit('team', null, 7)] }), true);
  assert.equal(canFollowMoreTeams(null), false);
  assert.equal(canFollowMoreTeams({ limits: [] }), false);
});

test('limitOf selects a kind and tolerates missing data', () => {
  const subscriptions = { limits: [limit('team', 5, 1), limit('league', 5, 0)] };
  assert.equal(limitOf(subscriptions, 'league')?.max, 5);
  assert.equal(limitOf(subscriptions, 'organization'), null);
  assert.equal(limitOf(null, 'team'), null);
});

test('formatUsage renders the licence consumption, the infinity and the unknown', () => {
  assert.equal(formatUsage(limit('team', 5, 3)), '3 / 5');
  assert.equal(formatUsage(limit('team', null, 3)), '3 / ∞');
  assert.equal(formatUsage(limit('team', 0, 0)), '0 / 0');
  assert.equal(formatUsage(null), '—');
});

test('isRemovable honours the 15-day removal lock window', () => {
  const now = new Date('2026-11-18T12:00:00Z');
  assert.equal(isRemovable({ lockedUntil: '2026-11-25T12:00:00Z' }, now), false);
  assert.equal(isRemovable({ lockedUntil: '2026-11-01T12:00:00Z' }, now), true);
  assert.equal(isRemovable({ lockedUntil: null }, now), true);
  // An unparsable window never blocks the removal.
  assert.equal(isRemovable({ lockedUntil: 'not-a-date' }, now), true);
});

test('alreadyFollowed detects the team picked in the selector', () => {
  const teams = [{ teamId: 181303 }];
  assert.equal(alreadyFollowed(teams, 181303), true);
  assert.equal(alreadyFollowed(teams, 181304), false);
  assert.equal(alreadyFollowed(teams, null), false);
});

test('each selector level requires its parent', () => {
  assert.equal(canLoadDivisions(null), false);
  assert.equal(canLoadDivisions(0), true);
  assert.equal(canLoadLeagues(null), false);
  assert.equal(canLoadLeagues(6193), true);
  assert.equal(canLoadTeams(null), false);
  assert.equal(canLoadTeams('13c38dd1-e464-4835-af5f-75be8561daf6'), true);
});

test('canAddTeam requires a picked team and licence headroom', () => {
  const open = { limits: [limit('team', 5, 1)] };
  assert.equal(canAddTeam(181303, open), true);
  assert.equal(canAddTeam(null, open), false);
  assert.equal(canAddTeam(181303, { limits: [limit('team', 5, 5)] }), false);
});
