import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ApiService } from './api.service';
import { AccountProfile } from './account.models';

/**
 * Local account of the caller (`GET /api/users/me`), cached for the session.
 * Exposes the effective admin flag used to show the admin navigation entry and
 * to guard the admin page; the backend re-checks it on every admin request.
 */
@Injectable({ providedIn: 'root' })
export class AccountService {
  private readonly api = inject(ApiService);

  private readonly profileSignal = signal<AccountProfile | null>(null);
  private pending: Promise<AccountProfile | null> | null = null;

  readonly isAdmin = computed(() => this.profileSignal()?.isAdmin === true);

  /**
   * Fetches the account once (deduplicated across concurrent callers) and
   * returns it; a failed request resolves to null instead of throwing so
   * navigation does not break when the API is unreachable.
   */
  load(force = false): Promise<AccountProfile | null> {
    if (!force && this.profileSignal()) {
      return Promise.resolve(this.profileSignal());
    }
    this.pending ??= firstValueFrom(this.api.getAccount())
      .then((profile) => {
        this.profileSignal.set(profile);
        return profile;
      })
      .catch(() => null)
      .finally(() => {
        this.pending = null;
      });
    return this.pending;
  }

  /** Drops the cached account (e.g. after logout). */
  clear(): void {
    this.profileSignal.set(null);
    this.pending = null;
  }
}
