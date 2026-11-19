import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AccountService } from './account.service';
import { needsTeamSelection, onboardingSkipped } from './team-onboarding';

/**
 * First-run gate of the landing page: an account created by the signup flow is
 * provisioned as a COACH with no followed team, so it is sent to the team
 * selection page before the rest of the app. The choice "later" is kept for the
 * browser session (session storage) so the guard does not loop.
 */
export const teamOnboardingGuard: CanActivateFn = async () => {
  const account = inject(AccountService);
  const router = inject(Router);

  const profile = await account.load();
  const storage = typeof sessionStorage === 'undefined' ? null : sessionStorage;
  return needsTeamSelection(profile) && !onboardingSkipped(storage)
    ? router.createUrlTree(['/team-selection'])
    : true;
};
