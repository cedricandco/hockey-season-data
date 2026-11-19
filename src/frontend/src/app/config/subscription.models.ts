/**
 * Models of the configuration page's "teams I follow" section.
 *
 * Selection path: association → division → league → team. Every identifier is
 * the one the next level and the subscription endpoints expect (`GET
 * /api/subscriptions/*` and `/api/users/me/*-subscriptions`).
 */

/** Association of the selector: a row of the `organizations` table. */
export interface SubscriptionAssociation {
  id: number;
  name: string;
}

export interface SubscriptionAssociationsResponse {
  associations: SubscriptionAssociation[];
}

/** Division of the selector: a Spordle office number (`divisions.external_id`). */
export interface SubscriptionDivision {
  id: number;
  name: string;
}

export interface SubscriptionDivisionsResponse {
  divisions: SubscriptionDivision[];
}

/** League of the selector: a Spordle category guid (`leagues.external_id`). */
export interface SubscriptionLeague {
  id: string;
  name: string;
}

export interface SubscriptionLeaguesResponse {
  /** Season the leagues belong to; null when the division has no imported league. */
  seasonId: string | null;
  leagues: SubscriptionLeague[];
}

/** Team of the selector: a Spordle team id (`teams.external_id`). */
export interface SubscriptionTeam {
  id: number;
  name: string;
}

export interface SubscriptionTeamsResponse {
  teams: SubscriptionTeam[];
}

/** One followed team, with its 15-day removal lock window. */
export interface FollowedTeam {
  teamId: number;
  name: string | null;
  createdAt: string;
  lockedUntil: string | null;
}

/** One followed league (Spordle category guid). */
export interface FollowedLeague {
  leagueId: string;
  name: string | null;
  createdAt: string;
}

/** One followed association (PRESIDENT scope). */
export interface FollowedOrganization {
  /** Spordle id of the association (`organizations.external_id`). */
  organizationId: number;
  name: string | null;
  createdAt: string;
}

/**
 * Licence headroom of one follow kind: `max` is null when the kind is
 * unlimited (open access or a whole-association licence) and 0 when the caller
 * holds no role for it.
 */
export interface SubscriptionLimit {
  kind: 'team' | 'league' | 'organization' | string;
  max: number | null;
  used: number;
  canAddMore: boolean;
}

/** `GET /api/users/me/subscriptions`: what the caller follows + licence headroom. */
export interface SubscriptionsResponse {
  teams: FollowedTeam[];
  leagues: FollowedLeague[];
  organizations: FollowedOrganization[];
  limits: SubscriptionLimit[];
}
