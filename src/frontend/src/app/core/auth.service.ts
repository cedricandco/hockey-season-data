import { Injectable, inject } from '@angular/core';
import { AuthService as Auth0AuthService } from '@auth0/auth0-angular';

/**
 * Thin adapter over the Auth0 SDK exposing logout with a return-to origin;
 * authentication state flows through the SDK's observables in the components.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly auth0 = inject(Auth0AuthService);

  logout(): void {
    void this.auth0.logout({ logoutParams: { returnTo: window.location.origin } });
  }
}
