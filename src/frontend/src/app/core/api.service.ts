import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { HealthReport } from './models';
import { WeeklyReport } from '../report/weekly-report.models';
import { HttpParams } from '@angular/common/http';
import { TeamDetail } from '../team/team.models';
import { UserProfile } from './user.models';
import { AccountProfile, AdminRole, AdminUser } from './account.models';
import {
  ReferenceImportRequest,
  ReferenceImportResult,
  ReferenceOrganizationsResponse,
  ReferenceSeasonsResponse,
} from '../admin/reference.models';
import {
  DirectorCategoriesResponse,
  DirectorTeamsFollowResult,
  DirectorTeamsResponse,
  PresidentTeamsFollowResult,
  PresidentTeamsResponse,
  SubscriptionAssociationsResponse,
  SubscriptionDivisionsResponse,
  SubscriptionLeaguesResponse,
  SubscriptionTeamsResponse,
  SubscriptionsResponse,
} from '../config/subscription.models';
import { NavChoice, NavDivision, NavLeague, NavTeam, FollowedTeam, TeamPageDetail } from '../team/team.models';

/**
 * Anonymous preview of the landing page: the reference data a visitor may
 * browse before creating an account, keyed on database ids like the team page.
 */
export interface PreviewAssociation { id: number; name: string }
export interface PreviewDivision { id: number; name: string }
export interface PreviewLeague { id: string; name: string }
export interface PreviewTeam { id: number; name: string }
export interface PreviewLeaguesResponse { seasonId: string | null; leagues: PreviewLeague[] }

/**
 * Thin wrapper around HttpClient for the Hockey Season API.
 * Kept as a single injectable so components stay dumb (SRP).
 */
@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = environment.apiUrl;

  getHealth(): Observable<HealthReport> {
    return this.http.get<HealthReport>(`${this.baseUrl}/health`);
  }

  /** Identity-provider profile of the authenticated caller (avatar, name...). */
  getMe(): Observable<UserProfile> {
    return this.http.get<UserProfile>(`${this.baseUrl}/me`);
  }

  getGameDetail(gameId: number): Observable<unknown> {
    return this.http.get<unknown>(`${this.baseUrl}/games/${gameId}`);
  }

  /**
   * Team page navigation, limited to the caller's follows: associations with a
   * followed team, their divisions, leagues and teams. Every identifier is a
   * database id, so nothing outside the follows is reachable.
   */
  getNavAssociations(): Observable<NavChoice[]> {
    return this.http.get<NavChoice[]>(`${this.baseUrl}/nav/associations`);
  }

  getNavDivisions(associationId: number): Observable<NavDivision[]> {
    return this.http.get<NavDivision[]>(`${this.baseUrl}/nav/associations/${associationId}/divisions`);
  }

  getNavLeagues(divisionId: number): Observable<NavLeague[]> {
    return this.http.get<NavLeague[]>(`${this.baseUrl}/nav/divisions/${divisionId}/leagues`);
  }

  /**
   * Teams the team page can open in one league, limited to the caller's
   * follows: a followed team opens its whole league, so every team of the
   * league is listed once one of its teams is followed.
   */
  getNavTeams(leagueId: number): Observable<NavTeam[]> {
    return this.http.get<NavTeam[]>(`${this.baseUrl}/nav/leagues/${leagueId}/teams`);
  }

  /** Every team the caller follows, with its league, division and association names. */
  getFollowedNavTeams(): Observable<FollowedTeam[]> {
    return this.http.get<FollowedTeam[]>(`${this.baseUrl}/nav/teams`);
  }

  /**
   * Detail of a followed team, keyed on its `teams.id` alone: the backend
   * validates the follow and resolves the published Spordle schedule, so the
   * page never handles one. The response also maps the Spordle team ids of the
   * games to the database ids the page navigates with.
   */
  getFollowedTeamDetail(teamId: number): Observable<TeamPageDetail> {
    return this.http.get<TeamPageDetail>(`${this.baseUrl}/teams/${teamId}`);
  }

  /** Detail of a team, keyed on Spordle ids (chat card / agent tool output). */
  getTeamDetail(leagueId: number, teamId: number): Observable<TeamDetail> {
    return this.http.get<TeamDetail>(`${this.baseUrl}/leagues/${leagueId}/teams/${teamId}`);
  }

  /**
   * Weekly report of the caller's followed leagues/teams. The backend derives
   * the divisions and teams from the follows, so only the date range is sent.
   */
  getWeeklyReport(startDate: string, endDate: string): Observable<WeeklyReport> {
    const params = new HttpParams().set('startDate', startDate).set('endDate', endDate);
    return this.http.get<WeeklyReport>(`${this.baseUrl}/weekly-report`, { params });
  }

  /** Local account of the caller, provisioned on first call (roles + admin flag). */
  getAccount(): Observable<AccountProfile> {
    return this.http.get<AccountProfile>(`${this.baseUrl}/users/me`);
  }

  /** Admin directory: every local account with its granted roles. */
  getUsers(): Observable<{ users: AdminUser[] }> {
    return this.http.get<{ users: AdminUser[] }>(`${this.baseUrl}/users`);
  }

  /** Admin reference data: roles that can be granted. */
  getRoles(): Observable<{ roles: AdminRole[] }> {
    return this.http.get<{ roles: AdminRole[] }>(`${this.baseUrl}/roles`);
  }

  /** Replaces the whole role set of a user; returns the updated account. */
  setUserRoles(userId: number, roleIds: readonly number[]): Observable<AdminUser> {
    return this.http.put<AdminUser>(`${this.baseUrl}/users/${userId}/roles`, { roleIds });
  }

  /** Admin import: associations (organizations) that can be synchronized from Spordle. */
  getReferenceOrganizations(): Observable<ReferenceOrganizationsResponse> {
    return this.http.get<ReferenceOrganizationsResponse>(`${this.baseUrl}/reference/organizations`);
  }

  /** Admin import: seasons Spordle publishes for a division, plus the imported ones. */
  getReferenceSeasons(divisionId: string): Observable<ReferenceSeasonsResponse> {
    return this.http.get<ReferenceSeasonsResponse>(`${this.baseUrl}/reference/divisions/${divisionId}/seasons`);
  }

  /**
   * Admin import: creates/updates the leagues (Spordle categories, French name)
   * and teams of one office for one season, deleting what disappeared.
   */
  importReferenceData(request: ReferenceImportRequest): Observable<ReferenceImportResult> {
    return this.http.post<ReferenceImportResult>(`${this.baseUrl}/reference/import`, request);
  }

  /**
   * What the caller follows (teams, leagues, associations) plus the licence
   * headroom of each kind, used by the configuration page.
   */
  getSubscriptions(): Observable<SubscriptionsResponse> {
    return this.http.get<SubscriptionsResponse>(`${this.baseUrl}/users/me/subscriptions`);
  }

  /** First level of the "follow a team" selector: the associations. */
  getSubscriptionAssociations(): Observable<SubscriptionAssociationsResponse> {
    return this.http.get<SubscriptionAssociationsResponse>(`${this.baseUrl}/subscriptions/associations`);
  }

  /** Divisions (Spordle offices) of one association. */
  getSubscriptionDivisions(associationId: number): Observable<SubscriptionDivisionsResponse> {
    return this.http.get<SubscriptionDivisionsResponse>(
      `${this.baseUrl}/subscriptions/associations/${associationId}/divisions`);
  }

  /** Leagues of a division, for the latest imported season. */
  getSubscriptionLeagues(divisionId: number): Observable<SubscriptionLeaguesResponse> {
    return this.http.get<SubscriptionLeaguesResponse>(`${this.baseUrl}/subscriptions/divisions/${divisionId}/leagues`);
  }

  /** Teams of one league (Spordle category guid). */
  getSubscriptionTeams(leagueId: string): Observable<SubscriptionTeamsResponse> {
    return this.http.get<SubscriptionTeamsResponse>(
      `${this.baseUrl}/subscriptions/leagues/${encodeURIComponent(leagueId)}/teams`);
  }

  /** Follows a team (Spordle team id). */
  addTeamSubscription(teamId: number): Observable<unknown> {
    return this.http.post(`${this.baseUrl}/users/me/team-subscriptions`, { entityId: teamId });
  }

  /**
   * Association-president selector: teams of one association whose name starts
   * with the typed base name, across every division, league and category.
   */
  getPresidentTeams(associationId: number, baseName: string): Observable<PresidentTeamsResponse> {
    const params = new HttpParams().set('baseName', baseName);
    return this.http.get<PresidentTeamsResponse>(
      `${this.baseUrl}/subscriptions/associations/${associationId}/teams`, { params });
  }

  /**
   * Follows every team of an association whose name starts with the base name,
   * in one call (PRESIDENT licence: no team cap, no 15-day lock).
   */
  addPresidentTeamSubscriptions(
    associationId: number,
    baseName: string,
  ): Observable<PresidentTeamsFollowResult> {
    return this.http.post<PresidentTeamsFollowResult>(
      `${this.baseUrl}/users/me/president-team-subscriptions`, { associationId, baseName });
  }

  /** Unfollows a team; 423 when the 15-day lock is still active. */
  removeTeamSubscription(teamId: number): Observable<unknown> {
    return this.http.delete(`${this.baseUrl}/users/me/team-subscriptions/${teamId}`);
  }

  /**
   * Category-director selector: category levels ("M11", "M13", …, "Junior") of
   * one association, with the number of teams of each.
   */
  getDirectorCategories(associationId: number): Observable<DirectorCategoriesResponse> {
    return this.http.get<DirectorCategoriesResponse>(
      `${this.baseUrl}/subscriptions/associations/${associationId}/categories`);
  }

  /**
   * Category-director selector: every team of one category level whose name
   * starts with the base name, whatever the letter refining it ("M13" +
   * "Grizzly" selects M13 A, M13 B and M13 AA Grizzlies).
   */
  getDirectorTeams(associationId: number, category: string, baseName?: string): Observable<DirectorTeamsResponse> {
    let params = new HttpParams().set('category', category);
    if (baseName) params = params.set('baseName', baseName);
    return this.http.get<DirectorTeamsResponse>(
      `${this.baseUrl}/subscriptions/associations/${associationId}/categories/teams`, { params });
  }

  /**
   * Follows every team of one team name inside a category level in one call
   * (DIRECTOR licence: no team cap, no 15-day lock).
   */
  addDirectorTeamSubscriptions(
    associationId: number,
    category: string,
    baseName: string,
  ): Observable<DirectorTeamsFollowResult> {
    return this.http.post<DirectorTeamsFollowResult>(
      `${this.baseUrl}/users/me/director-team-subscriptions`, { associationId, category, baseName });
  }

  /**
   * Anonymous preview of the landing page (no account needed): association →
   * division → league → team, then the detail of a picked team, which has the
   * exact shape of the team page (`GET /api/teams/{teamId}`). Nothing here is
   * tied to a caller, so the browser is what limits a visitor to their free
   * previews.
   */
  getPreviewAssociations(): Observable<{ associations: PreviewAssociation[] }> {
    return this.http.get<{ associations: PreviewAssociation[] }>(`${this.baseUrl}/preview/associations`);
  }

  getPreviewDivisions(associationId: number): Observable<{ divisions: PreviewDivision[] }> {
    return this.http.get<{ divisions: PreviewDivision[] }>(
      `${this.baseUrl}/preview/associations/${associationId}/divisions`);
  }

  getPreviewLeagues(divisionId: number): Observable<PreviewLeaguesResponse> {
    return this.http.get<PreviewLeaguesResponse>(`${this.baseUrl}/preview/divisions/${divisionId}/leagues`);
  }

  getPreviewTeams(leagueId: string): Observable<{ teams: PreviewTeam[] }> {
    return this.http.get<{ teams: PreviewTeam[] }>(
      `${this.baseUrl}/preview/leagues/${encodeURIComponent(leagueId)}/teams`);
  }

  /** Detail of a previewed team: the same JSON as the team page, guest mode. */
  getPreviewTeamDetail(teamId: number): Observable<TeamPageDetail> {
    return this.http.get<TeamPageDetail>(`${this.baseUrl}/preview/teams/${teamId}`);
  }
}
