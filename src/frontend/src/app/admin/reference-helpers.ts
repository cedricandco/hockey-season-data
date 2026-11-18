/**
 * Pure helpers of the admin reference-data page, kept free of Angular so they
 * are unit testable with `node --test` (see reference-helpers.test.mjs).
 */
import type {
  ReferenceImportResult,
  ReferenceOrganization,
  ReferenceSeason,
} from './reference.models';

/** Digits-only Spordle division number, or 0 when the input is not one. */
export function parseOfficeNumber(value: string | number | null | undefined): number {
  const text = String(value ?? '').trim();
  if (!/^\d{1,9}$/.test(text)) return 0;
  const parsed = Number(text);
  return Number.isSafeInteger(parsed) && parsed > 0 ? parsed : 0;
}

/**
 * Season preselection: the current season of the list, its first entry, then
 * the backend default — whichever exists in the list first.
 */
export function defaultSeasonId(seasons: readonly ReferenceSeason[], fallback: string): string {
  return seasons.find((season) => season.isCurrent)?.externalId
    ?? seasons[0]?.externalId
    ?? fallback;
}

/**
 * Association preselect: the first row of the organizations table, as returned
 * by the backend. The list is the table itself, so nothing is inferred from the
 * division number.
 */
export function defaultOrganization(
  organizations: readonly ReferenceOrganization[],
): ReferenceOrganization | null {
  return organizations[0] ?? null;
}

/** Whether an import can be submitted. */
export function canImport(organization: ReferenceOrganization | null, officeNumber: number, seasonId: string): boolean {
  return organization !== null && officeNumber > 0 && seasonId.trim().length > 0;
}

/** One-line summary of an import, used by the result panel and its tests. */
export function summarizeImport(result: ReferenceImportResult): string {
  const leagues = result.leagues.created.length + result.leagues.updated.length;
  const teams = result.teams.created + result.teams.updated;
  const removed = result.leagues.deleted.length + result.teams.deleted;
  return `${leagues} ligue(s), ${teams} équipe(s), ${removed} suppression(s)`;
}

/** Teams changed by an import (created + updated), the number the admin looks at. */
export function changedTeams(result: ReferenceImportResult): number {
  return result.teams.created + result.teams.updated;
}
