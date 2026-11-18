/**
 * Pure helpers behind the logout action, kept free of Angular so they are unit
 * testable with `node --test` (see auth-logout.test.mjs).
 */

/** What the sign-out attempt produced. */
export type LogoutOutcome = 'done' | 'failed' | 'timeout';

/**
 * What the app must do next:
 * - `provider`: the provider redirect is under way, the browser is leaving;
 * - `local`: the provider could not be reached, so the SDK cache is cleared
 *   locally and the app restarts on the login gate.
 */
export type LogoutAction = 'provider' | 'local';

/**
 * The provider logout is the only way to end the identity-provider session: a
 * local-only clear would leave Auth0 signed in, so the next login silently
 * reuses the same account. It is therefore preferred, and the local fallback is
 * reserved for a failed or blocked redirect — being signed out matters more
 * than the provider round trip.
 */
export function logoutAction(outcome: LogoutOutcome): LogoutAction {
  return outcome === 'done' ? 'provider' : 'local';
}

/**
 * Where the provider returns to after the session is cleared: the app root
 * (never a deep link, which would show an authenticated page shell first) with
 * the trailing slash Auth0 requires on an allowed logout URL.
 */
export function logoutReturnTo(origin: string): string {
  return `${origin.replace(/\/+$/, '')}/`;
}

/** Race outcome of the logout attempt: the first of the call and the deadline wins. */
export function logoutOutcome(
  call: Promise<void>,
  deadlineMs: number,
  scheduleTimeout: (resolve: (outcome: LogoutOutcome) => void, ms: number) => void,
): Promise<LogoutOutcome> {
  return Promise.race([
    call.then(() => 'done' as const).catch(() => 'failed' as const),
    new Promise<LogoutOutcome>((resolve) => scheduleTimeout(resolve, deadlineMs)),
  ]);
}
