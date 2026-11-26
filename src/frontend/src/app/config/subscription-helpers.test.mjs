import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  alreadyFollowed,
  baseNameOf,
  canAddTeam,
  canFollowBaseName,
  canFollowCategory,
  canFollowMoreTeams,
  canLoadDivisions,
  canLoadLeagues,
  canLoadTeams,
  canPreviewCategoryTeams,
  categoryLevelOf,
  formatUsage,
  isAssociationPresident,
  isCategoryDirector,
  isRemovable,
  limitOf,
  lockedCategoryName,
  matchesBaseName,
  matchesCategory,
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

test('isCategoryDirector only offers the category flow to the DIRECTOR licence', () => {
  assert.equal(isCategoryDirector(['coach']), false);
  assert.equal(isCategoryDirector(['coach', 'director']), true);
  assert.equal(isCategoryDirector(['DIRECTOR']), true);
  assert.equal(isCategoryDirector(['president']), false);
  assert.equal(isCategoryDirector([]), false);
  assert.equal(isCategoryDirector(), false);
});

test('canFollowCategory needs a level, a typed name and at least one match', () => {
  assert.equal(canFollowCategory('M13', 'grizzly', 2), true);
  assert.equal(canFollowCategory('Junior', 'grizzly', 1), true);
  assert.equal(canFollowCategory(null, 'grizzly', 2), false);
  assert.equal(canFollowCategory('  ', 'grizzly', 2), false);
  // A name with no match cannot be followed: the preview must list teams first.
  assert.equal(canFollowCategory('M13', 'grizzly', 0), false);
  assert.equal(canFollowCategory('M13', '  ', 2), false);
  // While the batch add runs the button stays disabled.
  assert.equal(canFollowCategory('M13', 'grizzly', 2, true), false);
  // A name other than the one the level follows is blocked; the same name is not.
  assert.equal(canFollowCategory('M13', 'mousquetaire', 1, false, 'grizzly'), false);
  assert.equal(canFollowCategory('M13', 'GRIZZLY', 2, false, 'grizzly'), true);
  // Another suffix of the same family keeps the same base name.
  assert.equal(canFollowCategory('M13', 'GRIZZLY 2', 1, false, 'grizzly'), true);
  assert.equal(canFollowCategory('M13', 'grizzly', 2, false, null), true);
});

test('canPreviewCategoryTeams needs a level and a typed name', () => {
  assert.equal(canPreviewCategoryTeams('M13', 'grizzly'), true);
  assert.equal(canPreviewCategoryTeams(null, 'grizzly'), false);
  assert.equal(canPreviewCategoryTeams('M13', '   '), false);
});

test('lockedCategoryName locks a category to the name it already follows', () => {
  const teams = [
    { name: 'GRIZZLY 1 STE-JULIE', category: 'M13' },
    { name: 'AIGLES 1 ST-BRUNO', category: 'M15' },
  ];
  assert.equal(lockedCategoryName(teams, 'M13'), 'grizzly');
  // Another level is independent: M15 keeps its own name.
  assert.equal(lockedCategoryName(teams, 'M15'), 'aigles');
  assert.equal(lockedCategoryName(teams, 'M11'), null);
  // No level picked, nothing locked; an unknown level neither.
  assert.equal(lockedCategoryName(teams, null), null);
  // A team without a published category never locks a level.
  assert.equal(lockedCategoryName([{ name: 'GRIZZLY 1', category: null }], 'M13'), null);
  assert.equal(lockedCategoryName([], 'M13'), null);
});

test('categoryLevelOf reads the level of a published league name', () => {
  assert.equal(categoryLevelOf('M13 B'), 'M13');
  assert.equal(categoryLevelOf('M13 AA'), 'M13');
  assert.equal(categoryLevelOf('M13A'), 'M13');
  assert.equal(categoryLevelOf(' m11 a '), 'M11');
  assert.equal(categoryLevelOf('Junior A'), 'JUNIOR');
  assert.equal(categoryLevelOf('Juvénil B'), 'JUNIOR');
  assert.equal(categoryLevelOf('Atome BB'), null);
  assert.equal(categoryLevelOf(''), null);
  assert.equal(categoryLevelOf(null), null);
});

test('matchesCategory folds the letters into the category', () => {
  assert.equal(matchesCategory('M13 B', 'M13'), true);
  assert.equal(matchesCategory('M13 AA', 'm13'), true);
  assert.equal(matchesCategory('M13', ' M13 '), true);
  assert.equal(matchesCategory('Junior A', 'junior'), true);
  assert.equal(matchesCategory('M15 A', 'M13'), false);
  assert.equal(matchesCategory('M13 B', ''), false);
  assert.equal(matchesCategory(null, 'M13'), false);
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