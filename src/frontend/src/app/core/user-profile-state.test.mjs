import { test } from 'node:test';
import assert from 'node:assert/strict';
import { menuView } from './user-profile-state.ts';

test('menuView uses the profile name, email and avatar when it loads', () => {
  const view = menuView(
    { status: 'ready', profile: { name: 'Test Hockey', email: 'test@example.com', picture: 'https://img/a.png' } },
    { name: 'Claim Name', email: 'claim@example.com', picture: 'https://img/claim.png' },
  );
  assert.equal(view.loading, false);
  assert.equal(view.displayName, 'Test Hockey');
  assert.equal(view.email, 'test@example.com');
  assert.equal(view.picture, 'https://img/a.png');
  assert.equal(view.initials, 'TH');
});

test('menuView falls back to the SDK claims when the profile lookup failed', () => {
  const view = menuView(
    { status: 'unavailable' },
    { name: 'Test Hockey', email: 'test@example.com', picture: 'https://img/claim.png' },
  );
  assert.equal(view.loading, false);
  assert.equal(view.displayName, 'Test Hockey');
  assert.equal(view.email, 'test@example.com');
  assert.equal(view.picture, 'https://img/claim.png');
});

test('menuView stays renderable when neither profile nor claims resolve', () => {
  for (const state of [{ status: 'loading' }, { status: 'unavailable' }]) {
    const view = menuView(state, null);
    assert.equal(view.displayName, '');
    assert.equal(view.email, '');
    assert.equal(view.picture, '');
    assert.equal(view.initials, '?', 'the trigger must never be empty');
  }
  assert.equal(menuView({ status: 'loading' }, null).loading, true);
  assert.equal(menuView({ status: 'unavailable' }, null).loading, false);
});

test('menuView derives initials from the nickname or email when there is no name', () => {
  assert.equal(menuView({ status: 'ready', profile: { nickname: 'coach patin' } }, null).initials, 'CP');
  assert.equal(menuView({ status: 'ready', profile: { email: 'jane.doe@example.com' } }, null).initials, 'JD');
});
