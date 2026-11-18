import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { HealthReport } from './models';
import { WeeklyReport } from '../report/weekly-report.models';
import { HttpParams } from '@angular/common/http';
import { Choice, TeamDetail } from '../team/team.models';
import { UserProfile } from './user.models';
import { AccountProfile, AdminRole, AdminUser } from './account.models';
import {
  ReferenceImportRequest,
  ReferenceImportResult,
  ReferenceOrganizationsResponse,
  ReferenceSeasonsResponse,
} from '../admin/reference.models';

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

  getOffices(): Observable<Choice[]> {
    return this.http.get<Choice[]>(`${this.baseUrl}/offices`);
  }

  getLeagues(officeId: number): Observable<Choice[]> {
    return this.http.get<Choice[]>(`${this.baseUrl}/offices/${officeId}/leagues`);
  }

  getTeams(leagueId: number): Observable<Choice[]> {
    return this.http.get<Choice[]>(`${this.baseUrl}/leagues/${leagueId}/teams`);
  }

  getTeamDetail(leagueId: number, teamId: number): Observable<TeamDetail> {
    return this.http.get<TeamDetail>(`${this.baseUrl}/leagues/${leagueId}/teams/${teamId}`);
  }

  getWeeklyReport(startDate: string, endDate: string, divisionIds: readonly number[], teamIds: readonly number[]): Observable<WeeklyReport> {
    let params = new HttpParams().set('startDate', startDate).set('endDate', endDate);
    for (const id of divisionIds) params = params.append('divisionId', id);
    for (const id of teamIds) params = params.append('teamId', id);
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
}
