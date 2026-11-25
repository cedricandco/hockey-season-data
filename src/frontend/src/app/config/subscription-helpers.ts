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
