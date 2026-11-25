import { Component, DestroyRef, ElementRef, computed, inject, signal } from '@angular/core';
import { DOCUMENT } from '@angular/common';
import { toSignal } from '@angular/core/rxjs-interop';
import { AuthService as Auth0AuthService } from '@auth0/auth0-angular';
import { TranslatePipe } from '@ngx-translate/core';
import { catchError, map, of, startWith } from 'rxjs';
import { ApiService } from './api.service';
import { AuthService } from './auth.service';
import { MenuProfileState, menuView } from './user-profile-state';

/**
 * Header user area replaces the plain logout button: the caller's identity-
 * provider avatar (from `GET /api/me`) with a dropdown menu offering the
 * profile entry point and the logout action. The logout goes through
 * `AuthService`, which clears the provider session (not only the local token
 * cache) and falls back to a local sign-out when Auth0 cannot be reached.
 * Closes on outside click, on Escape and after an item is chosen.
 *
 * The profile lookup is advisory only: it decorates the menu with a name,
 * email and avatar, so a failed or slow `GET /api/me` must never hide the
 * menu. An unhandled error would surface while the template reads the signal
 * and take the whole dropdown (hence the only logout entry) down with it, so
 * the stream always resolves and the SDK's id-token claims stand in for the
 * missing profile.
 */

@Component({
  selector: 'app-user-menu',
  standalone: true,
  imports: [TranslatePipe],
  template: `
    <div class="user-menu" #root>
      <button class="user-menu-trigger header-button" type="button" (click)="toggle()"
        [attr.aria-expanded]="open()" aria-haspopup="menu"
        [attr.aria-label]="'USER.MENU' | translate">
        @if (picture()) {
          <img class="user-avatar" [src]="picture()" alt="" loading="lazy" referrerpolicy="no-referrer"
            [attr.alt]="'USER.AVATAR_ALT' | translate: { name: displayName() }" />
        } @else {
          <span class="user-avatar user-avatar-fallback" aria-hidden="true">{{ initials() }}</span>
        }
      </button>
      @if (open()) {
        <div class="user-menu-panel" role="menu" [attr.aria-label]="'USER.MENU' | translate">
          <div class="user-menu-identity" role="none">
            @if (displayName()) { <span class="user-menu-name">{{ displayName() }}</span> }
            @if (email()) { <span class="user-menu-email">{{ email() }}</span> }
            @if (loading()) { <span class="user-menu-name">{{ 'USER.LOADING' | translate }}</span> }
          </div>
          <button class="user-menu-item" role="menuitem" type="button" (click)="close()">
            <span aria-hidden="true">⚙</span> {{ 'USER.SETTINGS' | translate }}
          </button>
          <button class="user-menu-item user-menu-item-danger" role="menuitem" type="button"
            [disabled]="loggingOut()" (click)="logout()">
            <span aria-hidden="true">⎋</span>
            {{ (loggingOut() ? 'LOGOUT.BUSY' : 'LOGOUT.BUTTON') | translate }}
          </button>
        </div>
      }
    </div>
  `,
})
export class UserMenuComponent {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly auth0 = inject(Auth0AuthService);
  private readonly root = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly destroyRef = inject(DestroyRef);

  /**
   * Profile fetched once per app session. The error arm matters: without it an
   * unhandled HTTP error is rethrown by `toSignal` while the template reads it,
   * which prevented the dropdown (and its logout entry) from ever rendering.
   */
  private readonly profileState = toSignal(
    this.api.getMe().pipe(
      map((profile): MenuProfileState => ({ status: 'ready', profile })),
      catchError((reason: unknown) => {
        console.error('Unable to load the caller profile for the user menu', reason);
        return of({ status: 'unavailable' } as MenuProfileState);
      }),
      startWith({ status: 'loading' } as MenuProfileState),
    ),
    { initialValue: { status: 'loading' } as MenuProfileState },
  );

  /** Identity-provider claims of the SDK, the fallback when `/api/me` fails. */
  private readonly claims = toSignal(
    this.auth0.user$.pipe(catchError(() => of(null))),
    { initialValue: null },
  );

  readonly open = signal(false);
  /** True while the provider session is being ended, to block double clicks. */
  readonly loggingOut = signal(false);

  private readonly view = computed(() => menuView(this.profileState(), this.claims()));

  readonly loading = computed(() => this.view().loading);
  readonly displayName = computed(() => this.view().displayName);
  readonly email = computed(() => this.view().email);
  readonly picture = computed(() => this.view().picture);
  readonly initials = computed(() => this.view().initials);

  constructor() {
    const document = inject(DOCUMENT);
    const outsideClick = (event: MouseEvent) => {
      if (this.open() && !this.root.nativeElement.contains(event.target as Node)) this.close();
    };
    const escapeKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') this.close();
    };
    document.addEventListener('mousedown', outsideClick);
    document.addEventListener('keydown', escapeKey);
    this.destroyRef.onDestroy(() => {
      document.removeEventListener('mousedown', outsideClick);
      document.removeEventListener('keydown', escapeKey);
    });
  }

  toggle(): void {
    this.open.update((value) => !value);
  }

  close(): void {
    this.open.set(false);
  }

  async logout(): Promise<void> {
    if (this.loggingOut()) return;
    this.loggingOut.set(true);
    this.close();
    try {
      await this.auth.logout();
    } finally {
      // The page navigates on success; this only matters when it cannot.
      this.loggingOut.set(false);
    }
  }
}
