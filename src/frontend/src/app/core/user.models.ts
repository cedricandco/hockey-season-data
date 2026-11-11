/**
 * Identity-provider profile returned by `GET /api/me` (OIDC /userinfo claims
 * relayed by the backend). `sub` is the stable user identifier.
 */
export interface UserProfile {
  sub: string;
  nickname: string | null;
  name: string | null;
  picture: string | null;
  updated_at: string | null;
  email: string | null;
  email_verified: boolean | null;
}
