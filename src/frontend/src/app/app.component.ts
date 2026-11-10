import { Component, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { LoginComponent } from './core/login.component';
import { AuthService } from './core/auth.service';

/**
 * Application root: shows a login gate until the user authenticates, then a
 * full-height shell with three tab pages (Chat / Weekly report / Settings).
 * Authentication state is reactive, so a 401 anywhere in the app
 * (token expired/revoked) flips back to the login screen automatically.
 */
@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, TranslatePipe, LoginComponent],
  template: `
    @if (auth.isLoggedIn) {
      <div class="app-shell">
        <header class="site-header">
          <div class="header-inner">
            <a class="brand" href="/chat" [attr.aria-label]="'APP.TITLE' | translate">
              <span class="brand-mark" aria-hidden="true">
                <svg viewBox="0 0 32 32" fill="none">
                  <path d="M7 6l12 15m6-15L13 21M9 24h14M11 27h10" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"/>
                  <circle cx="16" cy="21" r="3" fill="currentColor"/>
                </svg>
              </span>
              <span class="brand-name">HOCKEY <span>SEASON DATA</span></span>
            </a>
            <div class="header-actions">
              <nav class="main-nav" [attr.aria-label]="'NAV.LABEL' | translate">
                <a routerLink="/chat" routerLinkActive="nav-active">{{ 'NAV.CHAT' | translate }}</a>
                <a routerLink="/team" routerLinkActive="nav-active">{{ 'NAV.TEAM' | translate }}</a>
                <a routerLink="/teams-weekly-report" routerLinkActive="nav-active">{{ 'NAV.REPORT' | translate }}</a>
                <a routerLink="/configuration" routerLinkActive="nav-active">{{ 'NAV.CONFIG' | translate }}</a>
              </nav>
              <button class="header-button language-button" type="button" (click)="toggleLanguage()"
                [attr.aria-label]="'LANGUAGE.BUTTON' | translate">
                <span aria-hidden="true">◎</span> {{ language() === 'fr' ? 'EN' : 'FR' }}
              </button>
              <button class="header-button logout-button" type="button" (click)="logout()"
                [attr.aria-label]="'LOGOUT.BUTTON' | translate">
                <span aria-hidden="true">⎋</span><span class="settings-label">{{ 'LOGOUT.BUTTON' | translate }}</span>
              </button>
            </div>
          </div>
        </header>
        <main class="app-outlet">
          <router-outlet />
        </main>
      </div>
    } @else {
      <app-login />
    }
  `,
})
export class AppComponent {
  private readonly translate = inject(TranslateService);
  private readonly router = inject(Router);
  readonly auth = inject(AuthService);
  readonly language = signal<'fr' | 'en'>('fr');

  toggleLanguage(): void {
    const next: 'fr' | 'en' = this.language() === 'fr' ? 'en' : 'fr';
    this.language.set(next);
    document.documentElement.lang = next;
    this.translate.use(next);
  }

  logout(): void {
    this.auth.logout();
    void this.router.navigate(['/chat']);
  }
}
