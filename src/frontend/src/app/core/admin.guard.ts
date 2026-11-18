import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AccountService } from './account.service';

/**
 * Guards administrator-only pages: the caller's local account must be flagged
 * `isAdmin`. Non-admins are redirected to the chat page.
 */
export const adminGuard: CanActivateFn = async () => {
  const account = inject(AccountService);
  const router = inject(Router);

  const profile = await account.load();
  return profile?.isAdmin ? true : router.createUrlTree(['/chat']);
};
