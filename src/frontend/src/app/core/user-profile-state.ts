/**
 * Pure view-model of the header user menu, kept free of Angular so it is unit
 * testable with `node --test` (see user-profile-state.test.mjs).
 *
 * The identity-provider profile (`GET /api/me`) is decorative: it supplies the
 * name, email and avatar of the menu. The menu itself — and the only logout
 * entry of the app — must render whatever that lookup does, so an unavailable
 * profile falls back to the SDK's id-token claims and then to a neutral
 * placeholder.
 */

/** Profile returned by `GET /api/me` (only the fields the menu displays). */
export interface MenuProfile {
  readonly name?: string | null;
  readonly nickname?: string | null;
  readonly email?: string | null;
  readonly picture?: string | null;
}

/** Identity-provider claims of the Auth0 SDK, used when the profile is missing. */
export interface MenuClaims {
  readonly name?: string | null;
  readonly nickname?: string | null;
  readonly email?: string | null;
  readonly picture?: string | null;
}

/** Outcome of the profile lookup: still loading, found, or failed. */
export type MenuProfileState =
  | { readonly status: 'loading' }
  | { readonly status: 'ready'; readonly profile: MenuProfile }
  | { readonly status: 'unavailable' };

export interface MenuView {
  readonly loading: boolean;
  readonly displayName: string;
  readonly email: string;
  readonly picture: string;
  /** Two uppercase letters shown when there is no avatar image. */
  readonly initials: string;
}

/**
 * Resolves what the menu shows. A failed lookup is not an error here: the SDK
 * claims stand in, and a visitor whose profile and claims are both empty still
 * gets the anonymous `?` placeholder instead of an empty trigger.
 */
export function menuView(state: MenuProfileState, claims: MenuClaims | null | undefined): MenuView {
  const profile = state.status === 'ready' ? state.profile : null;

  const displayName = profile?.name ?? profile?.nickname ?? claims?.name ?? claims?.nickname
    ?? profile?.email ?? claims?.email ?? '';
  const email = profile?.email ?? claims?.email ?? '';
  const picture = profile?.picture ?? claims?.picture ?? '';

  return {
    loading: state.status === 'loading',
    displayName,
    email,
    picture,
    initials: initialsOf(displayName),
  };
}

/** First letter of the two first words of a display name, uppercased. */
function initialsOf(displayName: string): string {
  return (displayName || '?')
    .split(/[\s@.|-]/)
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0]!.toUpperCase())
    .join('');
}
