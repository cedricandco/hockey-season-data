import { Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { TranslatePipe } from '@ngx-translate/core';
import { AuthService as Auth0AuthService } from '@auth0/auth0-angular';

/**
 * Login gate: hands the visitor over to Auth0 Universal Login (email +
 * password, signup included) and lands back on `window.location.origin` via
 * the OIDC Authorization Code + PKCE flow handled by the SDK.
 */
@Component({
  selector: 'app-login',
  standalone: true,
  imports: [TranslatePipe],
  template: `
    <div class="login-shell" role="main">
      <form class="login-card" (submit)="submit($event)" aria-labelledby="login-title">
        <span class="brand-mark login-brand" aria-hidden="true">
          <svg viewBox="0 0 32 32" fill="none">
            <path d="M7 6l12 15m6-15L13 21M9 24h14M11 27h10" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"/>
            <circle cx="16" cy="21" r="3" fill="currentColor"/>
          </svg>
        </span>
        <h1 id="login-title">{{ 'LOGIN.TITLE' | translate }}</h1>
        <p class="login-subtitle">{{ 'LOGIN.SUBTITLE' | translate }}</p>

        @if (error()) {
          <p class="login-error" role="alert">{{ 'LOGIN.NETWORK' | translate }}</p>
        }

        <button class="login-submit" type="submit" [disabled]="busy()">
          @if (busy()) {
            <span aria-hidden="true">⌛</span> {{ 'LOGIN.BUSY' | translate }}
          } @else {
            {{ 'LOGIN.ACTION' | translate }}
          }
        </button>
      </form>
    </div>
  `,
})
export class LoginComponent {
  private readonly auth = inject(Auth0AuthService);

  readonly busy = toSignal(this.auth.isLoading$, { initialValue: true });
  readonly error = toSignal(this.auth.error$, { initialValue: undefined });

  submit(event: Event): void {
    event.preventDefault();
    this.auth.loginWithRedirect();
  }
}
