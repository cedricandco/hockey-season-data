/**
 * Free previews a visitor gets on the landing page before having to create an
 * account. Counted in the browser (`localStorage`) — not on the server — so the
 * gate is a product rule rather than a security boundary: the API routes stay
 * anonymous, and creating the free account is what keeps the data available.
 *
 * Kept free of Angular so it is unit testable with `node --test`
 * (see preview-trials.test.mjs).
 */

/** Number of team statistics a visitor may open without an account. */
export const FREE_PREVIEW_LIMIT = 3;

/** Storage key holding the number of previews already used on this device. */
export const PREVIEW_TRIALS_KEY = 'hockey-season-preview-trials';

/** Minimal storage surface used here, so tests can pass an in-memory stub. */
export type TrialsStorage = Pick<Storage, 'getItem' | 'setItem'>;

/** Previews already used on this device; 0 for an unreadable or legacy value. */
export function previewsUsed(storage: TrialsStorage | null): number {
  const raw = storage?.getItem(PREVIEW_TRIALS_KEY);
  const used = Number(raw);
  return Number.isSafeInteger(used) && used > 0 ? used : 0;
}

/** Previews left before the account is required, never negative. */
export function previewsLeft(storage: TrialsStorage | null): number {
  return Math.max(0, FREE_PREVIEW_LIMIT - previewsUsed(storage));
}

/** Whether another preview can be opened without creating an account. */
export function canPreview(storage: TrialsStorage | null): boolean {
  return previewsLeft(storage) > 0;
}

/**
 * Records one preview and returns the new count, capped at the limit so a
 * tampered or stale value can never unlock more attempts. A storage that
 * refuses to persist (private browsing, quota) leaves the counter unchanged.
 */
export function recordPreview(storage: TrialsStorage | null): number {
  const next = Math.min(FREE_PREVIEW_LIMIT, previewsUsed(storage) + 1);
  try {
    storage?.setItem(PREVIEW_TRIALS_KEY, String(next));
  } catch {
    // Storage unavailable: the browser session keeps its in-memory state only.
  }
  return next;
}
