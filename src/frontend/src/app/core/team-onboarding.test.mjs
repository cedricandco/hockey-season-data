import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  ONBOARDING_SKIP_KEY,
  clearOnboardingSkip,
  needsTeamSelection,
  onboardingSkipped,
  skipOnboarding,
} from './team-onboarding.ts';

const coach = { roleId: 1, code: 'COACH', displayName: 'Coach', maxFollowedEntities: 5 };

/** Minimal in-memory Storage stand-in (the helpers only use get/set/remove). */
function memoryStorage(initial = {}) {
  const data = { ...initial };
  return {
    getItem: (key) => (key in data ? data[key] : null),
    setItem: (key, value) => { data[key] = String(value); },
    removeItem: (key) => { delete data[key]; },
  };
}

test('needsTeamSelection asks a brand-new coach to pick a team', () => {
  assert.equal(needsTeamSelection({ teamCount: 0, roles: [coach], isAdmin: false }), true);
});

test('needsTeamSelection stays away once a team is followed', () => {
  assert.equal(needsTeamSelection({ teamCount: 1, roles: [coach], isAdmin: false }), false);
  assert.equal(needsTeamSelection({ teamCount: 3, roles: [], isAdmin: false }), false);
});

test('needsTeamSelection ignores callers that cannot follow a team', () => {
  assert.equal(needsTeamSelection({ teamCount: 0, roles: [{ ...coach, maxFollowedEntities: 0 }], isAdmin: false }), false);
  // An account without the COACH licence cannot use the selection page.
  assert.equal(needsTeamSelection({ teamCount: 0, roles: [], isAdmin: false }), false);
  // An administrator has the app available regardless of their teams.
  assert.equal(needsTeamSelection({ teamCount: 0, roles: [coach], isAdmin: true }), false);
  // No profile at all (or an older API without teamCount) is never redirected.
  assert.equal(needsTeamSelection(null), false);
  assert.equal(needsTeamSelection({ roles: [coach], isAdmin: false }), false);
});

test('the "later" choice survives the session and can be cleared', () => {
  const storage = memoryStorage();
  assert.equal(onboardingSkipped(storage), false);

  skipOnboarding(storage);
  assert.equal(storage.getItem(ONBOARDING_SKIP_KEY), '1');
  assert.equal(onboardingSkipped(storage), true);

  clearOnboardingSkip(storage);
  assert.equal(onboardingSkipped(storage), false);

  // Storage can be unavailable (private mode): helpers stay silent.
  assert.equal(onboardingSkipped(null), false);
  assert.doesNotThrow(() => skipOnboarding(null));
  assert.doesNotThrow(() => clearOnboardingSkip(null));
});
