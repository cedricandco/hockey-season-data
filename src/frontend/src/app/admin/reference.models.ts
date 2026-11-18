/**
 * Models of the admin reference-data page (`/admin/reference`): one Spordle
 * association imported for one season creates the division (office), the
 * leagues (categories, named in French) and the teams.
 */

/**
 * Association of the import page: one row of the `organizations` table
 * (`GET /api/reference/organizations`). The division is not listed — the
 * administrator types its number, which is the `divisions.external_id`.
 */
export interface ReferenceOrganization {
  id: number;
  name: string;
  /** Spordle identifier mirroring the association. */
  externalId: string;
}

export interface ReferenceOrganizationsResponse {
  organizations: ReferenceOrganization[];
}

/** Season of the import page, `externalId` is the Spordle season id. */
export interface ReferenceSeason {
  externalId: string;
  name: string;
  isCurrent: boolean;
}

export interface ReferenceSeasonsResponse {
  seasons: ReferenceSeason[];
  defaultSeasonId: string;
}

/** Body of `POST /api/reference/import`. */
export interface ReferenceImportRequest {
  /** Local id of the association (`organizations.id`). */
  organizationId: number;
  /** Division number typed in the page (`divisions.external_id`). */
  divisionId: string;
  seasonId: string;
}

/** Outcome of one import: what changed, per level. */
export interface ReferenceImportResult {
  organizationName: string;
  officeName: string;
  seasonId: string;
  leagues: {
    created: string[];
    updated: string[];
    deleted: string[];
    unchanged: string[];
  };
  teams: {
    created: number;
    updated: number;
    deleted: number;
  };
  warnings: string[];
}
