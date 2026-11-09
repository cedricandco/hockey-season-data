import { Injectable, signal } from '@angular/core';

/**
 * Persistence of the OIDC-style access token. Kept in localStorage so the
 * token survives refreshes; requests attach it explicitly (no cookie is
 * involved, which avoids CSRF on the API). Storage failures (private mode,
 * disabled storage) degrade gracefully to a session-only token.
 */
export const TOKEN_STORAGE_KEY = 'hockey-season-auth-token';

/** Shape returned by POST /api/auth/login. */
export interface LoginResponse {
  success: boolean;
  accessToken: string;
  expiresInSeconds: number;
  tokenType: string;
}

@Injectable({ providedIn: 'root' })
export class AuthStorage {
  /** Reactive login state; true when a token is in memory. */
  readonly isLoggedIn = signal(false);

  private token: string | null = null;

  constructor() {
    this.restore();
  }

  get accessToken(): string | null {
    return this.token;
  }

  setToken(token: string): void {
    this.token = token;
    this.isLoggedIn.set(true);
    try {
      localStorage.setItem(TOKEN_STORAGE_KEY, token);
    } catch {
      // Storage unavailable (Safari private mode): keep the token in memory only.
    }
  }

  clearToken(): void {
    this.token = null;
    this.isLoggedIn.set(false);
    try {
      localStorage.removeItem(TOKEN_STORAGE_KEY);
    } catch {
      // Ignore storage failures; in-memory state was already reset.
    }
  }

  private restore(): void {
    try {
      const stored = localStorage.getItem(TOKEN_STORAGE_KEY);
      if (!stored) return;
      this.token = stored;
      this.isLoggedIn.set(true);
    } catch {
      // Ignore: sessionStorage fallbacks must not break app startup.
    }
  }
}
