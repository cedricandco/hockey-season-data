import { test } from 'node:test';
import assert from 'node:assert/strict';
import { displayName, roleLabel, sameRoleSet, toggleRoleId } from './admin-roles.ts';

test('sameRoleSet ignores order and duplicates but detects changes', () => {
  assert.equal(sameRoleSet([1, 3], [3, 1]), true);
  assert.equal(sameRoleSet([1, 1, 3], [3, 1]), true);
  assert.equal(sameRoleSet([1], [1, 3]), false);
  assert.equal(sameRoleSet([], [1]), false);
  assert.equal(sameRoleSet([], []), true);
});

test('toggleRoleId adds and removes a single role without touching the others', () => {
  assert.deepEqual(toggleRoleId([1], 3, true), [1, 3]);
  assert.deepEqual(toggleRoleId([1, 3], 3, true), [1, 3], 're-checking never duplicates');
  assert.deepEqual(toggleRoleId([1, 3], 3, false), [1]);
  assert.deepEqual(toggleRoleId([], 2, true), [2]);
});

test('roleLabel only mentions the limit of licence roles', () => {
  assert.equal(roleLabel({ name: 'Coach', maxFollowedEntities: 5 }), 'Coach (5)');
  assert.equal(roleLabel({ name: 'Président', maxFollowedEntities: 0 }), 'Président');
});

test('displayName falls back from full name to email to subject', () => {
  assert.equal(displayName({ firstName: 'Marie', lastName: 'Coach', email: 'm@x.ca', auth0UserId: 'auth0|1' }), 'Marie Coach');
  assert.equal(displayName({ firstName: null, lastName: null, email: 'm@x.ca', auth0UserId: 'auth0|1' }), 'm@x.ca');
  assert.equal(displayName({ firstName: null, lastName: null, email: null, auth0UserId: 'auth0|1' }), 'auth0|1');
});
