import { Component, computed, inject, signal } from '@angular/core';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { AuthService, LoginOutcome } from './auth.service';

/**
 * Login gate: collects the 6-digit access code (one input per digit style is
 * unnecessary here — a single numeric field keeps keyboard and paste flows
 * working) and exchanges it for the OIDC-style JWT at /api/auth/login.
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

        <label class="login-label" for="access-code">{{ 'LOGIN.CODE_LABEL' | translate }}</label>
        <input
          id="access-code"
          class="login-input"
          type="text"
          inputmode="numeric"
          autocomplete="one-time-code"
          maxlength="6"
          [value]="code()"
          (input)="onInput($event)"
          [placeholder]="'LOGIN.PLACEHOLDER' | translate"
          [attr.aria-invalid]="outcome() === 'invalid'"
          autofocus />
        @if (outcome() === 'invalid') {
          <p class="login-error" role="alert">{{ 'LOGIN.INVALID' | translate }}</p>
        } @else if (outcome() === 'throttled') {
          <p class="login-error" role="alert">{{ 'LOGIN.THROTTLED' | translate }}</p>
        } @else if (outcome() === 'network') {
          <p class="login-error" role="alert">{{ 'LOGIN.NETWORK' | translate }}</p>
        }

        <button class="login-submit" type="submit" [disabled]="!valid() || busy()">
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
  private readonly auth = inject(AuthService);
  private readonly translate = inject(TranslateService);

  readonly code = signal('');
  readonly busy = signal(false);
  readonly outcome = this.auth.outcome;

  readonly valid = computed(() => /^\d{6}$/.test(this.code()));

  onInput(event: Event): void {
    const value = (event.target as HTMLInputElement).value.replace(/\D/g, '').slice(0, 6);
    this.code.set(value);
    if (this.outcome() !== 'idle') {
      this.auth.outcome.set('idle');
    }
  }

  submit(event: Event): void {
    event.preventDefault();
    if (!this.valid() || this.busy()) return;
    this.busy.set(true);
    void this.auth.login(this.code()).finally(() => this.busy.set(false));
  }
}
