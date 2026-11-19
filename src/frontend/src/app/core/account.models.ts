/** Licence role granted to a user (COACH / DIRECTOR / PRESIDENT). */
export interface RoleAssignment {
  roleId: number;
  code: string;
  name: string;
  maxFollowedEntities: number;
}

/** Licence role as returned by `GET /api/users/me` (display name field). */
export interface AccountRole {
  roleId: number;
  code: string;
  displayName: string;
  maxFollowedEntities: number;
}

/** Local account of the caller returned by `GET /api/users/me`. */
export interface AccountProfile {
  id: number;
  auth0UserId: string;
  email: string | null;
  firstName: string | null;
  lastName: string | null;
  isAdmin: boolean;
  roles: AccountRole[];
  settings: {
    primaryColor: string | null;
    secondaryColor: string | null;
    extra: Record<string, string | null>;
  };
  /**
   * Number of teams the account follows. A brand-new account is provisioned
   * as a coach with zero teams, which is what sends it to the team-selection
   * page on first visit.
   */
  teamCount?: number;
}

/** Account row of the admin directory (`GET /api/users`). */
export interface AdminUser {
  id: number;
  auth0UserId: string;
  email: string | null;
  firstName: string | null;
  lastName: string | null;
  isAdmin: boolean;
  createdAt: string;
  roles: RoleAssignment[];
}

/** Assignable role of the reference table (`GET /api/roles`). */
export interface AdminRole {
  roleId: number;
  code: string;
  name: string;
  maxFollowedEntities: number;
}
