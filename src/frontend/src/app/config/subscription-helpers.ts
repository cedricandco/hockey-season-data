/**
 * Pure helpers of the configuration page's "teams I follow" section, kept free
 * of Angular so they are unit testable with `node --test` (see
 * subscription-helpers.test.mjs).
 */
import type {
  FollowedTeam,
  SubscriptionLimit,
  SubscriptionsResponse,
} from './subscription.models';

/** Licence headroom of a follow kind, or null when the API did not report it. */
export function limitOf(
  subscriptions: Pick<SubscriptionsResponse, 'limits'> | null,
  kind: string,
): SubscriptionLimit | null {
  return subscriptions?.limits.find((limit) => limit.kind === kind) ?? null;
}

/**
 * Whether another team can be followed: the kind's `canAddMore` flag, which is
 * true when unlimited and false when the licence cap is reached or the caller
 * holds no COACH licence (max 0).
 */
export function canFollowMoreTeams(subscriptions: Pick<SubscriptionsResponse, 'limits'> | null): boolean {
  return limitOf(subscriptions, 'team')?.canAddMore === true;
}

/** "3 / 5" usage of a kind, "3 / ∞" when unlimited, "—" when unknown. */
export function formatUsage(limit: SubscriptionLimit | null): string {
  if (!limit) return '—';
  return `${limit.used} / ${limit.max === null ? '∞' : limit.max}`;
}

/** Whether a followed team can be removed now (its 15-day lock has expired). */
export function isRemovable(team: Pick<FollowedTeam, 'lockedUntil'>, now: Date = new Date()): boolean {
  if (!team.lockedUntil) return true;
  const until = Date.parse(team.lockedUntil);
  return Number.isNaN(until) || until <= now.getTime();
}

/** Whether the team is the one currently picked in the selector. */
export function alreadyFollowed(teams: readonly Pick<FollowedTeam, 'teamId'>[], teamId: number | null): boolean {
  return teamId !== null && teams.some((team) => team.teamId === teamId);
}

/**
 * Next selection step enabled by the current one: each level is only usable
 * once its parent is chosen, so the form mirrors the association → division →
 * league → team path.
 */
export function canLoadDivisions(associationId: number | null): boolean {
  return associationId !== null;
}

export function canLoadLeagues(divisionId: number | null): boolean {
  return divisionId !== null;
}

export function canLoadTeams(leagueId: string | null): boolean {
  return leagueId !== null;
}

/** Whether the "add" button can be pressed for the current selection. */
export function canAddTeam(
  teamId: number | null,
  subscriptions: Pick<SubscriptionsResponse, 'limits'> | null,
): boolean {
  return teamId !== null && canFollowMoreTeams(subscriptions);
}

/**
 * Whether the caller is an association president: a PRESIDENT licence shows up
 * in the account roles (`GET /api/users/me`). The president follows a whole
 * base name at once, so the configuration page offers that flow in addition to
 * the team-by-team selector. Role codes are compared case-insensitively.
 */
export function isAssociationPresident(roleCodes: readonly string[] = []): boolean {
  return roleCodes.some((code) => code.toLowerCase() === 'president');
}

/**
 * Whether the caller is a category director: the DIRECTOR licence shows up in
 * the account roles. A director follows a whole category level ("M13") at once
 * — every letter of it — so the configuration page offers that flow.
 */
export function isCategoryDirector(roleCodes: readonly string[] = []): boolean {
  return roleCodes.some((code) => code.toLowerCase() === 'director');
}

/**
 * Whether the category flow can run: a level and a team name must be typed, the
 * preview must have matched at least one team, and no batch add may be in
 * flight. A category is followed one name at a time, so a name other than the
 * one the level already follows (`lockedName`) is blocked — the comparison uses
 * the base name (first word), so another suffix of the same family ("grizzly 5"
 * after "grizzly 3") stays allowed.
 */
export function canFollowCategory(
  category: string | null,
  baseName: string,
  matches: number,
  adding = false,
  lockedName: string | null = null,
): boolean {
  if (adding || (category ?? '').trim().length === 0 || baseName.trim().length === 0 || matches <= 0) {
    return false;
  }
  return lockedName === null || baseNameOf(baseName) === lockedName;
}

/** Whether the "show the teams" button can be pressed: level and name typed. */
export function canPreviewCategoryTeams(category: string | null, baseName: string): boolean {
  return (category ?? '').trim().length > 0 && baseName.trim().length > 0;
}

/**
 * Team name a category director already follows in a category level, or null.
 * A category is followed one name at a time: while the teams of that name are
 * followed, another name cannot be picked — the director removes them first.
 * The level of a followed team comes from the API (`category`), the level of
 * the selector from its own id.
 */
export function lockedCategoryName(
  teams: readonly Pick<FollowedTeam, 'name' | 'category'>[],
  category: string | null,
): string | null {
  const level = categoryLevelOf(category);
  if (level === null) return null;
  const followed = teams.find((team) =>
    categoryLevelOf(team.category) === level && baseNameOf(team.name).length > 0);
  return followed ? baseNameOf(followed.name) : null;
}

/**
 * Level of a published league name — "M13 A", "M13 B", "M13 AA" all belong to
 * M13, which is what a category director follows. Mirrors the backend
 * `LeagueCategoryLevel`: the first "M<digits>" word, or Junior/Juvénil.
 */
export function categoryLevelOf(leagueName: string | null | undefined): string | null {
  const words = (leagueName ?? '')
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .toUpperCase()
    .split(/[^A-Z0-9]+/)
    .filter((word) => word.length > 0);
  const first = words[0];
  if (!first) return null;
  if (first.startsWith('JUNIOR') || first.startsWith('JUVENIL')) return 'JUNIOR';
  const match = /^M\d+/.exec(first);
  return match ? match[0] : null;
}

/**
 * Whether a league belongs to the picked category level, ignoring the letters
 * that refine it: "M13" covers "M13 A", "M13 B" and "M13 AA".
 */
export function matchesCategory(leagueName: string | null | undefined, category: string): boolean {
  const level = categoryLevelOf(category);
  return level !== null && categoryLevelOf(leagueName) === level;
}

/**
 * Whether the typed base name can be turned into follows: something other than
 * blank must be typed, and the base name must match at least one team.
 */
export function canFollowBaseName(baseName: string, matches: number, adding = false): boolean {
  return !adding && baseName.trim().length > 0 && matches > 0;
}

/**
 * Whether a published team name belongs to the typed base name — the same
 * match the backend uses (prefix on a word boundary, case- and
 * accent-insensitive). The followed list marks the teams of the current base
 * name so a president sees at a glance which ones the batch covered.
 */
export function matchesBaseName(name: string | null | undefined, baseName: string): boolean {
  const basis = normalizeTeamName(baseName);
  if (basis.length === 0) return false;
  return startsWithWord(name ?? '', basis);
}

/** Lower case, no diacritics, single spaces — the backend's normalization. */
export function normalizeTeamName(value: string | null | undefined): string {
  if (!value) return '';
  return value
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, ' ')
    .trim();
}

/** Whether the normalized name starts with the base name on a word boundary. */
function startsWithWord(name: string, basis: string): boolean {
  const normalized = normalizeTeamName(name);
  return normalized === basis || normalized.startsWith(`${basis} `);
}

/**
 * Suggested base name of a followed team, used as an input suggestion: the
 * first word of the published name ("GRIZZLY 1 STE-JULIE" → "GRIZZLY"), which
 * is how Spordle names a team (base name, rank, city).
 */
export function baseNameOf(name: string | null | undefined): string {
  return normalizeTeamName(name).split(' ')[0] ?? '';
}
