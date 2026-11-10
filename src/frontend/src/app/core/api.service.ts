import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { HealthReport } from './models';
import { WeeklyReport } from '../report/weekly-report.models';
import { HttpParams } from '@angular/common/http';
import { Choice, TeamDetail } from '../team/team.models';

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
}
