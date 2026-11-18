import { Injectable, inject } from '@angular/core';
import { AuthService as Auth0AuthService } from '@auth0/auth0-angular';
import { firstValueFrom } from 'rxjs';
import { AccountService } from './account.service';
import { logoutAction, logoutOutcome, logoutReturnTo } from './auth-logout';

/** How long the Auth0 logout redirect may take before the local session is cleared instead. */
const LOGOUT_TIMEOUT_MS = 4000;

/**
 * Authentication use cases over the Auth0 SDK. Logout really ends the session:
 *
 * 1. the cached local account (roles + admin flag) is dropped, so the header
 *    and the admin guard stop trusting the previous identity immediately;
 * 2. the SDK clears its token cache and redirects to Auth0 `/v2/logout`,
 *    which clears the identity-provider session (otherwise the next
 *    `loginWithRedirect` silently reuses the previous user);
 * 3. when that redirect fails or is blocked (unreachable tenant, popup
 *    restrictions, logout URL not allowed), the SDK cache is cleared locally
 *    and the app reloads into the login gate, so the caller is never left
 *    signed in.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly auth0 = inject(Auth0AuthService);
  private readonly account = inject(AccountService);

  /** Ends the session; resolves once the browser navigates or the local session is cleared. */
  async logout(): Promise<void> {
    this.account.clear();

    const outcome = await logoutOutcome(
      firstValueFrom(this.auth0.logout({ logoutParams: { returnTo: logoutReturnTo(window.location.origin) } })),
      LOGOUT_TIMEOUT_MS,
      (resolve, ms) => { window.setTimeout(() => resolve('timeout'), ms); },
    );

    if (logoutAction(outcome) === 'local') {
      console.error('Auth0 logout did not complete, clearing the local session', outcome);
      await this.clearLocalSession();
    }
  }

  /** Clears the SDK cache without navigating, then restarts the app on the login gate. */
  private async clearLocalSession(): Promise<void> {
    try {
      // openUrl: false drops the stored tokens only: no Auth0 round trip.
      await firstValueFrom(this.auth0.logout({ openUrl: false }));
    } catch (error) {
      console.error('Clearing the local Auth0 session failed', error);
    }
    // A full reload resets every in-memory signal (account, chat, guards).
    window.location.replace('/');
  }
}

