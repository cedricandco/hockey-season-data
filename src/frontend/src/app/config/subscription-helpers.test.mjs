import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  alreadyFollowed,
  baseNameOf,
  canAddTeam,
  canFollowBaseName,
  canFollowMoreTeams,
  canLoadDivisions,
  canLoadLeagues,
  canLoadTeams,
  formatUsage,
  isAssociationPresident,
  isRemovable,
  limitOf,
  matchesBaseName,
  normalizeTeamName,
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

test('isAssociationPresident only offers the batch flow to the PRESIDENT licence', () => {
  assert.equal(isAssociationPresident(['coach']), false);
  assert.equal(isAssociationPresident(['coach', 'president']), true);
  assert.equal(isAssociationPresident(['PRESIDENT']), true);
  assert.equal(isAssociationPresident([]), false);
  assert.equal(isAssociationPresident(), false);
});

test('canFollowBaseName needs a typed base name and at least one match', () => {
  assert.equal(canFollowBaseName('grizzly', 3), true);
  assert.equal(canFollowBaseName('  ', 3), false);
  assert.equal(canFollowBaseName('grizzly', 0), false);
  // While the batch add runs the button stays disabled.
  assert.equal(canFollowBaseName('grizzly', 3, true), false);
});

test('normalizeTeamName mirrors the backend base-name match', () => {
  assert.equal(normalizeTeamName('GRIZZLY 1 STE-JULIE'), 'grizzly 1 ste julie');
  assert.equal(normalizeTeamName('AIGLES ST-JÉRÔME'), 'aigles st jerome');
  assert.equal(normalizeTeamName('  Côté  Saint--Luc '), 'cote saint luc');
  assert.equal(normalizeTeamName(null), '');
});

test('matchesBaseName mirrors the backend base-name match', () => {
  assert.equal(matchesBaseName('GRIZZLY 1 STE-JULIE', 'grizzly'), true);
  assert.equal(matchesBaseName('GRIZZLY 2 ST-HYACINTHE', 'GRIZZLY'), true);
  assert.equal(matchesBaseName('AIGLES ST-JÉRÔME', 'aigles st-jerome'), true);
  assert.equal(matchesBaseName('GRIZZLYS 3 LONGUEUIL', 'grizzly'), false);
  assert.equal(matchesBaseName('GRIZZLY 1 STE-JULIE', 'grizzly 2'), false);
  assert.equal(matchesBaseName('GRIZZLY 1', ''), false);
  assert.equal(matchesBaseName(null, 'grizzly'), false);
});

test('baseNameOf suggests the first word of a published team name', () => {
  assert.equal(baseNameOf('GRIZZLY 1 STE-JULIE'), 'grizzly');
  assert.equal(baseNameOf('AIGLES ST-JÉRÔME'), 'aigles');
  assert.equal(baseNameOf(null), '');
});