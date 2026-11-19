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
