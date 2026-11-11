import { Component, DestroyRef, ElementRef, computed, inject, signal } from '@angular/core';
import { DOCUMENT } from '@angular/common';
import { toSignal } from '@angular/core/rxjs-interop';
import { TranslatePipe } from '@ngx-translate/core';
import { startWith } from 'rxjs';
import { AuthService as Auth0AuthService } from '@auth0/auth0-angular';
import { ApiService } from './api.service';
import { UserProfile } from './user.models';

/**
 * Header user area replaces the plain logout button: the caller's identity-
 * provider avatar (from `GET /api/me`) with a dropdown menu offering the
 * profile entry point and the logout action. Closes on outside click, on
 * Escape and after an item is chosen.
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
        @if (me()?.picture) {
          <img class="user-avatar" [src]="me()!.picture!" alt="" loading="lazy" referrerpolicy="no-referrer"
            [attr.alt]="'USER.AVATAR_ALT' | translate: { name: displayName() }" />
        } @else {
          <span class="user-avatar user-avatar-fallback" aria-hidden="true">{{ initials() }}</span>
        }
      </button>
      @if (open()) {
        <div class="user-menu-panel" role="menu" [attr.aria-label]="'USER.MENU' | translate">
          <div class="user-menu-identity" role="none">
            @if (me()?.name) { <span class="user-menu-name">{{ me()!.name }}</span> }
            @if (me()?.email) { <span class="user-menu-email">{{ me()!.email }}</span> }
            @if (!me()) { <span class="user-menu-name">{{ 'USER.LOADING' | translate }}</span> }
          </div>
          <button class="user-menu-item" role="menuitem" type="button" (click)="close()">
            <span aria-hidden="true">⚙</span> {{ 'USER.SETTINGS' | translate }}
          </button>
          <button class="user-menu-item user-menu-item-danger" role="menuitem" type="button" (click)="logout()">
            <span aria-hidden="true">⎋</span> {{ 'LOGOUT.BUTTON' | translate }}
          </button>
        </div>
      }
    </div>
  `,
})
export class UserMenuComponent {
  private readonly api = inject(ApiService);
  private readonly auth0 = inject(Auth0AuthService);
  private readonly root = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly destroyRef = inject(DestroyRef);

  /** Profile fetched once per app session; null until the first response. */
  readonly me = toSignal(this.api.getMe().pipe(startWith(null)), { initialValue: null });
  readonly open = signal(false);

  readonly displayName = computed(() => this.me()?.name ?? this.me()?.nickname ?? this.me()?.email ?? '');
  readonly initials = computed(() => {
    const source = this.displayName() || '?';
    return source.split(/[\s@.|-]/).filter(Boolean).slice(0, 2).map((part) => part[0]!.toUpperCase()).join('');
  });

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

  logout(): void {
    this.close();
    void this.auth0.logout({ logoutParams: { returnTo: window.location.origin } });
  }
}
