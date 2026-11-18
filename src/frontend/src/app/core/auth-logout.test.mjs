import { test } from 'node:test';
import assert from 'node:assert/strict';
import { logoutAction, logoutOutcome, logoutReturnTo } from './auth-logout.ts';

test('logoutAction only trusts the provider redirect when it completed', () => {
  assert.equal(logoutAction('done'), 'provider');
  assert.equal(logoutAction('failed'), 'local', 'a failed redirect must still sign the user out');
  assert.equal(logoutAction('timeout'), 'local', 'a blocked redirect must still sign the user out');
});

test('logoutReturnTo normalizes the origin to an allowed logout URL', () => {
  assert.equal(logoutReturnTo('https://app.example.com'), 'https://app.example.com/');
  assert.equal(logoutReturnTo('https://app.example.com/'), 'https://app.example.com/');
  assert.equal(logoutReturnTo('https://app.example.com//'), 'https://app.example.com/');
  assert.equal(logoutReturnTo('http://localhost:4200'), 'http://localhost:4200/');
});

test('logoutOutcome reports success, failure and timeout distinctly', async () => {
  assert.equal(await logoutOutcome(Promise.resolve(), 1000, () => { /* no deadline */ }), 'done');
  assert.equal(await logoutOutcome(Promise.reject(new Error('offline')), 1000, () => { /* no deadline */ }), 'failed');

  let fireTimeout = () => {};
  const never = new Promise(() => { /* the redirect hangs, as when blocked */ });
  const outcome = logoutOutcome(never, 1000, (resolve) => { fireTimeout = () => resolve('timeout'); });
  fireTimeout();
  assert.equal(await outcome, 'timeout');
});
