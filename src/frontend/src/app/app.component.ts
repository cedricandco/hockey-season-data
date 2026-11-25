import { Component, effect, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { AuthService as Auth0AuthService } from '@auth0/auth0-angular';
import { LandingComponent } from './landing/landing.component';
import { UserMenuComponent } from './core/user-menu.component';
import { AccountService } from './core/account.service';
import { clearOnboardingSkip } from './core/team-onboarding';

/**
 * Application root: shows the public landing page until the visitor
 * authenticates with Auth0 Universal Login, then the full-height shell with
 * the tab pages (Chat / Team / Weekly report / Settings). Login state stays
 * reactive via the SDK's observables converted to signals for zoneless change
 * detection.
 *
 * The landing page is public on purpose: it explains the site, lets a visitor
 * preview a team's statistics (three free attempts, counted in `localStorage`)
 * and only then asks for the free account.
 */
@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, TranslatePipe, LandingComponent, UserMenuComponent],
  template: `
    @if (isLoading()) {
      <!-- Universal Login redirect in progress -->
    } @else if (isLoggedIn()) {
      <div class="app-shell">
        <header class="site-header">
          <div class="header-inner">
            <a class="brand" href="/chat" [attr.aria-label]="'APP.TITLE' | translate">
              <img class="brand-mark" src="assets/logo/game-tracker-mark.png" alt="" />
            </a>
            <div class="header-actions">
              <nav class="main-nav" [attr.aria-label]="'NAV.LABEL' | translate">
                <a routerLink="/chat" routerLinkActive="nav-active" ariaCurrentWhenActive="page">
                  <svg class="nav-icon" aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M4 5h16v11H9l-5 4V5Z"/><path d="M8 9h8M8 12h5"/></svg><span>{{ 'NAV.CHAT' | translate }}</span>
                </a>
                <a routerLink="/team" routerLinkActive="nav-active" ariaCurrentWhenActive="page">
                  <svg class="nav-icon" aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><circle cx="9" cy="8" r="3"/><path d="M3 19v-1a6 6 0 0 1 12 0v1H3ZM16 5a3 3 0 0 1 0 6M17 13a5 5 0 0 1 4 5v1h-3"/></svg><span>{{ 'NAV.TEAM' | translate }}</span>
                </a>
                <a routerLink="/teams-weekly-report" routerLinkActive="nav-active" ariaCurrentWhenActive="page">
                  <svg class="nav-icon" aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M5 3h14v18H5zM8 8h8M8 12h8M8 16h6"/></svg><span class="nav-desktop-label">{{ 'NAV.REPORT' | translate }}</span><span class="nav-mobile-label">{{ 'NAV.REPORT_SHORT' | translate }}</span>
                </a>
                <a routerLink="/configuration" routerLinkActive="nav-active" ariaCurrentWhenActive="page">
                  <svg class="nav-icon" aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M4 7h16M4 17h16M7 4v6M17 14v6"/></svg><span class="nav-desktop-label">{{ 'NAV.CONFIG' | translate }}</span><span class="nav-mobile-label">{{ 'NAV.CONFIG_SHORT' | translate }}</span>
                </a>
                @if (account.isAdmin()) {
                  <a routerLink="/admin" routerLinkActive="nav-active" ariaCurrentWhenActive="page">
                    <svg class="nav-icon" aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M12 3l7 3v6c0 4.2-2.9 7.9-7 9-4.1-1.1-7-4.8-7-9V6l7-3Z"/><path d="M9 12l2 2 4-4"/></svg><span class="nav-desktop-label">{{ 'NAV.ADMIN' | translate }}</span><span class="nav-mobile-label">{{ 'NAV.ADMIN_SHORT' | translate }}</span>
                  </a>
                }
              </nav>
              <button class="header-button language-button" type="button" (click)="toggleLanguage()"
                [attr.aria-label]="'LANGUAGE.BUTTON' | translate">
                <span aria-hidden="true">◎</span> {{ language() === 'fr' ? 'EN' : 'FR' }}
              </button>
              <app-user-menu />
            </div>
          </div>
        </header>
        <main class="app-outlet">
          <router-outlet />
        </main>
      </div>
    } @else {
      <app-landing />
    }
  `,
})
export class AppComponent {
  private readonly translate = inject(TranslateService);
  private readonly auth0 = inject(Auth0AuthService);  /** Reactive login state for the zoneless template (Auth0 observables). */
  readonly isLoggedIn = toSignal(this.auth0.isAuthenticated$, { initialValue: false });
  readonly isLoading = toSignal(this.auth0.isLoading$, { initialValue: true });
  readonly language = signal<'fr' | 'en'>('fr');
  /** Local account (roles + admin flag) drives the administrator nav entry. */
  readonly account = inject(AccountService);

  constructor() {
    // Loads the local account once authenticated: the admin tab and the admin
    // route guard both rely on the `isAdmin` flag it carries.
    let wasLoggedIn = this.isLoggedIn();
    effect(() => {
      const loggedIn = this.isLoggedIn();
      if (loggedIn) {
        void this.account.load();
      } else if (wasLoggedIn) {
        // Only a real sign-out clears the session state: the SDK also reports
        // `false` while it initializes, which must not drop the "later" choice
        // the first-run guard relies on.
        this.account.clear();
        clearOnboardingSkip(typeof sessionStorage === 'undefined' ? null : sessionStorage);
      }
      wasLoggedIn = loggedIn;
    });
  }

  toggleLanguage(): void {
    const next: 'fr' | 'en' = this.language() === 'fr' ? 'en' : 'fr';
    this.language.set(next);
    document.documentElement.lang = next;
    this.translate.use(next);
  }
}
