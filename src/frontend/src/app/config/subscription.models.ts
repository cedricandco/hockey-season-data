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

/**
 * A team matched by the association-president selector: typing a base name
 * ("grizzly") selects every team of the association whose name starts with it.
 * `id` is the Spordle team id the team-subscription endpoints expect.
 */
export interface PresidentTeam {
  id: number;
  name: string;
  leagueName: string;
  divisionName: string;
}

/** `GET /api/subscriptions/associations/{id}/teams?baseName=…` */
export interface PresidentTeamsResponse {
  associationId: number;
  associationName: string;
  baseName: string;
  teams: PresidentTeam[];
}

/** `POST /api/users/me/president-team-subscriptions` outcome. */
export interface PresidentTeamsFollowResult {
  associationId: number;
  associationName: string;
  baseName: string;
  /** Spordle team ids followed by this call. */
  added: number[];
  /** Spordle team ids already followed (reported, not refused). */
  alreadyFollowed: number[];
  /** Spordle team ids that match no imported team. */
  unknown: number[];
}

/**
 * A category level of an association, e.g. "M13": the category-director
 * selector picks the level and follows every team of it, whatever the letter
 * refining the category ("M13 A", "M13 B", "M13 AA").
 */
export interface DirectorCategory {
  /** Level itself ("M11", "M13", "M15", "M17", "M19", "JUNIOR"). */
  id: string;
  /** Display name of the level. */
  name: string;
  /** Number of teams of the level in the latest imported season. */
  teamCount: number;
}

/** `GET /api/subscriptions/associations/{id}/categories` */
export interface DirectorCategoriesResponse {
  categories: DirectorCategory[];
}

/**
 * A team matched by the category-director selector: picking a level ("M13") and
 * typing a team name ("Grizzly") selects every team of that name in the
 * category, whatever its letter. `id` is the Spordle team id the
 * team-subscription endpoints expect.
 */
export interface DirectorTeam {
  id: number;
  name: string;
  leagueName: string;
  divisionName: string;
}

/** `GET /api/subscriptions/associations/{id}/categories/teams?category=…&baseName=…` */
export interface DirectorTeamsResponse {
  associationId: number;
  associationName: string;
  category: string;
  /** Team name the list was narrowed to; null when the whole level is listed. */
  baseName: string | null;
  teams: DirectorTeam[];
}

/** `POST /api/users/me/director-team-subscriptions` outcome. */
export interface DirectorTeamsFollowResult {
  associationId: number;
  associationName: string;
  category: string;
  baseName: string;
  /** Spordle team ids followed by this call. */
  added: number[];
  /** Spordle team ids already followed (reported, not refused). */
  alreadyFollowed: number[];
  /** Spordle team ids that match no imported team. */
  unknown: number[];
}

/** One followed team, with its 15-day removal lock window. */
export interface FollowedTeam {
  teamId: number;
  name: string | null;
  createdAt: string;
  lockedUntil: string | null;
  /** Category level of the team's league ("M13", "JUNIOR"), null when it publishes none. */
  category?: string | null;
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
