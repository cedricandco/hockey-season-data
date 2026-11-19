/**
 * Pure helpers behind the first-run team selection, kept free of Angular so
 * they are unit testable with `node --test` (see team-onboarding.test.mjs).
 *
 * A brand-new account is provisioned as a coach with no followed team: the app
 * sends it to the team-selection page so the caller picks the team(s) their
 * licence covers before reaching the rest of the app.
 */
import type { AccountProfile } from './account.models';

/** Key of the "continue without a team" choice, scoped to one browser session. */
export const ONBOARDING_SKIP_KEY = 'hockey-season-team-selection-skipped';

/**
 * Whether the account must pick a team first: it holds the COACH licence with
 * room for teams and follows none yet. A profile without `teamCount` (older
 * API), an administrator, and an account whose licence cannot follow a team
 * (no COACH role) are never redirected.
 */
export function needsTeamSelection(
  profile: Pick<AccountProfile, 'teamCount' | 'roles' | 'isAdmin'> | null,
): boolean {
  if (!profile || typeof profile.teamCount !== 'number') return false;
  if (profile.isAdmin || profile.teamCount > 0) return false;
  const teamRole = profile.roles.find((role) => role.code.toLowerCase() === 'coach');
  return teamRole !== undefined && teamRole.maxFollowedEntities !== 0;
}

/** The caller chose "later" during this session: do not send them back. */
export function onboardingSkipped(storage: Pick<Storage, 'getItem'> | null): boolean {
  return storage?.getItem(ONBOARDING_SKIP_KEY) === '1';
}

/** Records the "later" choice for the rest of the browser session. */
export function skipOnboarding(storage: Pick<Storage, 'setItem'> | null): void {
  storage?.setItem(ONBOARDING_SKIP_KEY, '1');
}

/** Clears the "later" choice, e.g. once a team is followed. */
export function clearOnboardingSkip(storage: Pick<Storage, 'removeItem'> | null): void {
  storage?.removeItem(ONBOARDING_SKIP_KEY);
}
