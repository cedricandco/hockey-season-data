/**
 * Pure helpers of the admin page, kept free of Angular so they are unit
 * testable with `node --test` (see admin-roles.test.mjs).
 */

/** Whether two role-id sets describe the same assignment (order-insensitive). */
export function sameRoleSet(a: readonly number[], b: readonly number[]): boolean {
  const left = [...new Set(a)].sort((x, y) => x - y);
  const right = [...new Set(b)].sort((x, y) => x - y);
  return left.length === right.length && left.every((roleId, index) => roleId === right[index]);
}

/** Toggles a role id inside a copy of the selection, preserving the others. */
export function toggleRoleId(roleIds: readonly number[], roleId: number, selected: boolean): number[] {
  if (selected) return roleIds.includes(roleId) ? [...roleIds] : [...roleIds, roleId];
  return roleIds.filter((id) => id !== roleId);
}

/** Human-readable role label: the licence limit is only shown when it applies. */
export function roleLabel(role: { name: string; maxFollowedEntities: number }): string {
  return role.maxFollowedEntities > 0 ? `${role.name} (${role.maxFollowedEntities})` : role.name;
}

/** Display name fallback chain of a user row. */
export function displayName(user: {
  firstName: string | null;
  lastName: string | null;
  email: string | null;
  auth0UserId: string;
}): string {
  const fullName = [user.firstName, user.lastName].filter(Boolean).join(' ').trim();
  return fullName || user.email || user.auth0UserId;
}
