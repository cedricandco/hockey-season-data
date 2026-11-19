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
  SubscriptionAssociationsResponse,
  SubscriptionDivisionsResponse,
  SubscriptionLeaguesResponse,
  SubscriptionTeamsResponse,
  SubscriptionsResponse,
} from '../config/subscription.models';
import { NavChoice, NavDivision, NavLeague, NavTeam, FollowedTeam } from '../team/team.models';

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
   * page never handles one.
   */
  getFollowedTeamDetail(teamId: number): Observable<TeamDetail> {
    return this.http.get<TeamDetail>(`${this.baseUrl}/teams/${teamId}`);
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

  /** Unfollows a team; 423 when the 15-day lock is still active. */
  removeTeamSubscription(teamId: number): Observable<unknown> {
    return this.http.delete(`${this.baseUrl}/users/me/team-subscriptions/${teamId}`);
  }
}
