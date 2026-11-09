import { Injectable, inject, signal } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { environment } from '../../environments/environment';
import { AuthStorage, LoginResponse } from './auth-storage';

/** Outcome of a login attempt surfaced to the login screen. */
export type LoginOutcome = 'idle' | 'invalid' | 'throttled' | 'network';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly storage = inject(AuthStorage);
  private readonly baseUrl = environment.apiUrl;

  /** Last login attempt outcome (drives UI messaging). */
  readonly outcome = signal<LoginOutcome>('idle');

  get isLoggedIn(): boolean {
    return this.storage.isLoggedIn();
  }

  get accessToken(): string | null {
    return this.storage.accessToken;
  }

  async login(code: string): Promise<void> {
    this.outcome.set('idle');
    try {
      const response = await this.http
        .post<LoginResponse>(`${this.baseUrl}/auth/login`, { code })
        .toPromise();
      if (!response?.accessToken) {
        this.outcome.set('invalid');
        return;
      }
      this.storage.setToken(response.accessToken);
    } catch (error) {
      this.outcome.set(this.outcomeFrom(error));
    }
  }

  logout(): void {
    this.outcome.set('idle');
    this.storage.clearToken();
  }

  private outcomeFrom(error: unknown): LoginOutcome {
    if (error instanceof HttpErrorResponse) {
      if (error.status === 401) return 'invalid';
      if (error.status === 429) return 'throttled';
      return 'network';
    }
    return 'network';
  }
}
