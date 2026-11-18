import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { ApiService } from '../core/api.service';
import { AdminRole, AdminUser } from '../core/account.models';
import { displayName, roleLabel, sameRoleSet, toggleRoleId } from './admin-roles';

/**
 * Admin page (administrator only): lists the local accounts of the `users`
 * table and lets an administrator grant or revoke licence roles, persisted in
 * `user_roles` against the reference `roles` table. The backend re-checks the
 * administrator flag on every request; this page is only the UI.
 */
@Component({
  selector: 'app-admin',
  standalone: true,
  imports: [RouterLink, RouterLinkActive, TranslatePipe],
  styleUrl: './admin.component.scss',
  template: `
    <section class="page-container admin" aria-labelledby="admin-title">
      <h1 id="admin-title" class="page-title">{{ 'ADMIN.TITLE' | translate }}</h1>
      <p class="page-subtitle">{{ 'ADMIN.SUBTITLE' | translate }}</p>

      <nav class="admin-tabs" [attr.aria-label]="'ADMIN.SECTIONS' | translate">
        <a class="admin-tab" routerLink="/admin" routerLinkActive="admin-tab-active"
          [routerLinkActiveOptions]="{ exact: true }" ariaCurrentWhenActive="page">
          {{ 'ADMIN.TAB_USERS' | translate }}
        </a>
        <a class="admin-tab" routerLink="/admin/reference" routerLinkActive="admin-tab-active"
          ariaCurrentWhenActive="page">
          {{ 'ADMIN.TAB_REFERENCE' | translate }}
        </a>
      </nav>

      @if (loading()) {
        <div class="admin-state" role="status">
          <span class="spinner" aria-hidden="true"></span> {{ 'ADMIN.LOADING' | translate }}
        </div>
      } @else if (error()) {
        <div class="admin-notice" role="alert">
          {{ 'ADMIN.ERROR' | translate }}
          <button type="button" class="admin-link" (click)="load()">{{ 'ADMIN.RETRY' | translate }}</button>
        </div>
      } @else if (users().length === 0) {
        <div class="empty-state"><p>{{ 'ADMIN.EMPTY' | translate }}</p></div>
      } @else {
        @if (saveError()) {
          <div class="admin-notice admin-notice-save" role="alert">{{ 'ADMIN.SAVE_ERROR' | translate }}</div>
        }
        <div class="admin-card">
          <div class="admin-table-wrap">
            <table class="admin-table">
              <caption class="sr-only">{{ 'ADMIN.TABLE_LABEL' | translate }}</caption>
              <thead>
                <tr>
                  <th scope="col">{{ 'ADMIN.USER' | translate }}</th>
                  <th scope="col">{{ 'ADMIN.EMAIL' | translate }}</th>
                  <th scope="col">{{ 'ADMIN.ROLES' | translate }}</th>
                  <th scope="col"><span class="sr-only">{{ 'ADMIN.ACTIONS' | translate }}</span></th>
                </tr>
              </thead>
              <tbody>
                @for (user of users(); track user.id) {
                  <tr>
                    <th scope="row" class="admin-user">
                      <span class="admin-name">{{ displayName(user) }}</span>
                      @if (user.isAdmin) {
                        <span class="admin-badge">{{ 'ADMIN.ADMIN_BADGE' | translate }}</span>
                      }
                    </th>
                    <td class="admin-email">{{ user.email ?? '—' }}</td>
                    <td>
                      <ul class="admin-roles">
                        @for (role of roles(); track role.roleId) {
                          <li>
                            <label>
                              <input type="checkbox"
                                [checked]="isSelected(user.id, role.roleId)"
                                [disabled]="savingUserId() === user.id"
                                (change)="toggleRole(user.id, role.roleId, $event)" />
                              <span>{{ roleLabel(role) }}</span>
                            </label>
                          </li>
                        }
                      </ul>
                    </td>
                    <td class="admin-actions">
                      <button type="button" class="admin-save"
                        [disabled]="!hasChanges(user.id) || savingUserId() === user.id"
                        (click)="save(user.id)">
                        {{ (savingUserId() === user.id ? 'ADMIN.SAVING' : 'ADMIN.SAVE') | translate }}
                      </button>
                      @if (savedUserId() === user.id) {
                        <span class="admin-saved" role="status">{{ 'ADMIN.SAVED' | translate }}</span>
                      }
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        </div>
      }
    </section>
  `,
})
export class AdminComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly destroyRef = inject(DestroyRef);

  readonly users = signal<AdminUser[]>([]);
  readonly roles = signal<AdminRole[]>([]);
  /** Pending selection per user id; the saved state lives in `users()`. */
  readonly drafts = signal<Record<number, number[]>>({});
  readonly loading = signal(true);
  readonly error = signal(false);
  readonly saveError = signal(false);
  readonly savingUserId = signal<number | null>(null);
  readonly savedUserId = signal<number | null>(null);

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(false);
    this.saveError.set(false);
    this.savedUserId.set(null);

    let pending = 2;
    const settle = () => {
      pending--;
      if (pending === 0) this.loading.set(false);
    };
    const fail = () => {
      this.error.set(true);
      this.loading.set(false);
    };

    this.api.getRoles().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: ({ roles }) => { this.roles.set(roles); settle(); },
      error: fail,
    });
    this.api.getUsers().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: ({ users }) => {
        this.users.set(users);
        this.drafts.set(Object.fromEntries(users.map((user) => [user.id, user.roles.map((role) => role.roleId)])));
        settle();
      },
      error: fail,
    });
  }

  displayName(user: AdminUser): string {
    return displayName(user);
  }

  roleLabel(role: AdminRole): string {
    return roleLabel(role);
  }

  isSelected(userId: number, roleId: number): boolean {
    return this.drafts()[userId]?.includes(roleId) ?? false;
  }

  hasChanges(userId: number): boolean {
    const saved = (this.users().find((user) => user.id === userId)?.roles ?? []).map((role) => role.roleId);
    return !sameRoleSet(this.drafts()[userId] ?? [], saved);
  }

  toggleRole(userId: number, roleId: number, event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    this.drafts.update((drafts) => ({
      ...drafts,
      [userId]: toggleRoleId(drafts[userId] ?? [], roleId, checked),
    }));
    this.savedUserId.set(null);
  }

  save(userId: number): void {
    if (this.savingUserId() !== null) return;

    this.savingUserId.set(userId);
    this.savedUserId.set(null);
    this.saveError.set(false);
    this.api.setUserRoles(userId, this.drafts()[userId] ?? [])
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (updated) => {
          this.users.update((users) => users.map((user) => (user.id === updated.id ? updated : user)));
          this.drafts.update((drafts) => ({ ...drafts, [updated.id]: updated.roles.map((role) => role.roleId) }));
          this.savingUserId.set(null);
          this.savedUserId.set(updated.id);
        },
        error: () => {
          this.savingUserId.set(null);
          this.saveError.set(true);
        },
      });
  }
}
