import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  canImport,
  changedTeams,
  defaultOrganization,
  defaultSeasonId,
  parseOfficeNumber,
  summarizeImport,
} from './reference-helpers.ts';

const organization = (id, externalId) => ({ id, name: `Association ${externalId}`, externalId });
const season = (externalId, isCurrent = false) => ({ externalId, name: externalId, isCurrent });

test('parseOfficeNumber accepts Spordle office numbers only', () => {
  assert.equal(parseOfficeNumber('6193'), 6193);
  assert.equal(parseOfficeNumber(' 6194 '), 6194);
  assert.equal(parseOfficeNumber(6194), 6194);
  assert.equal(parseOfficeNumber(''), 0);
  assert.equal(parseOfficeNumber('abc'), 0);
  assert.equal(parseOfficeNumber('61a3'), 0);
  assert.equal(parseOfficeNumber('0'), 0);
  assert.equal(parseOfficeNumber('-6193'), 0);
  assert.equal(parseOfficeNumber(null), 0);
});

test('defaultSeasonId prefers the current season, then the first, then the fallback', () => {
  assert.equal(defaultSeasonId([season('2025-26'), season('2026-27', true)], '2026-27'), '2026-27');
  assert.equal(defaultSeasonId([season('2025-26'), season('2024-25')], '2026-27'), '2025-26');
  assert.equal(defaultSeasonId([], '2026-27'), '2026-27');
});

test('defaultOrganization preselects the first row of the organizations table', () => {
  const organizations = [organization(1, '6157'), organization(2, '6158')];
  assert.equal(defaultOrganization(organizations)?.externalId, '6157');
  // The association list is the table itself: an empty table offers nothing.
  assert.equal(defaultOrganization([]), null);
});

test('canImport requires an association, a division number and a season', () => {
  assert.equal(canImport(organization(1, '6157'), 6193, '2026-27'), true);
  assert.equal(canImport(null, 6193, '2026-27'), false);
  assert.equal(canImport(organization(1, '6157'), 0, '2026-27'), false);
  assert.equal(canImport(organization(1, '6157'), 6193, '  '), false);
});

test('summaries count leagues, teams and deletions', () => {
  const result = {
    organizationName: 'HOCKEY RICHELIEU', officeName: 'Office', seasonId: '2026-27',
    leagues: { created: ['M13 B'], updated: ['M15 A'], deleted: ['M11 A'], unchanged: ['M17 A'] },
    teams: { created: 12, updated: 3, deleted: 2 },
    warnings: [],
  };
  assert.equal(changedTeams(result), 15);
  assert.equal(summarizeImport(result), '2 ligue(s), 15 équipe(s), 3 suppression(s)');
});
