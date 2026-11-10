import { Component, DestroyRef, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { Subscription } from 'rxjs';
import { ApiService } from '../core/api.service';
import { Choice, TeamDetail } from './team.models';
import { TeamSummaryComponent } from './team-summary.component';

@Component({
  selector: 'app-team',
  standalone: true,
  imports: [FormsModule, TranslatePipe, TeamSummaryComponent],
  styleUrl: './team.component.scss',
  template: `
    <section class="team-page">
      <h1>{{ 'TEAM.TITLE' | translate }}</h1><p>{{ 'TEAM.SUBTITLE' | translate }}</p>
      <div class="selectors">
        <label>{{ 'TEAM.OFFICE' | translate }}
          <select [ngModel]="officeId" (ngModelChange)="selectOffice($event)">
            <option [ngValue]="0">{{ 'TEAM.SELECT' | translate }}</option>
            @for (office of offices(); track office.id) { <option [ngValue]="office.id">{{ office.name }}</option> }
          </select>
        </label>
        <label>{{ 'TEAM.LEAGUE' | translate }}
          <select [ngModel]="leagueId" (ngModelChange)="selectLeague($event)" [disabled]="!officeId || loading()">
            <option [ngValue]="0">{{ 'TEAM.SELECT' | translate }}</option>
            @for (league of leagues(); track league.id) { <option [ngValue]="league.id">{{ league.name }}</option> }
          </select>
        </label>
        <label>{{ 'TEAM.TEAM' | translate }}
          <select [ngModel]="teamId" (ngModelChange)="selectTeam($event)" [disabled]="!leagueId || loading()">
            <option [ngValue]="0">{{ 'TEAM.SELECT' | translate }}</option>
            @for (team of teams(); track team.id) { <option [ngValue]="team.id">{{ team.name }}</option> }
          </select>
        </label>
      </div>
      @if (loading()) { <p role="status">{{ 'TEAM.LOADING' | translate }}</p> }
      @if (error()) { <p class="error" role="alert">{{ 'TEAM.ERROR' | translate }} <button type="button" (click)="retry()">{{ 'TEAM.RETRY' | translate }}</button></p> }
      <app-team-summary [team]="detail()" />
    </section>
  `,
})
export class TeamComponent {
  private readonly api = inject(ApiService);
  private subscription?: Subscription;
  readonly offices = signal<Choice[]>([]);
  readonly leagues = signal<Choice[]>([]);
  readonly teams = signal<Choice[]>([]);
  readonly detail = signal<TeamDetail | null>(null);
  readonly loading = signal(false);
  readonly error = signal(false);
  officeId = 0;
  leagueId = 0;
  teamId = 0;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.subscription?.unsubscribe());
    this.loadOffices();
  }

  selectOffice(id: number): void {
    this.officeId = id; this.leagueId = 0; this.teamId = 0;
    this.leagues.set([]); this.teams.set([]); this.detail.set(null);
    if (id) this.loadLeagues(); else this.cancel();
  }
  selectLeague(id: number): void {
    this.leagueId = id; this.teamId = 0; this.teams.set([]); this.detail.set(null);
    if (id) this.loadTeams(); else this.cancel();
  }
  selectTeam(id: number): void {
    this.teamId = id; this.detail.set(null);
    if (id) this.loadDetail(); else this.cancel();
  }
  retry(): void {
    if (this.teamId) this.loadDetail();
    else if (this.leagueId) this.loadTeams();
    else if (this.officeId) this.loadLeagues();
    else this.loadOffices();
  }
  private loadOffices(): void { this.load(this.api.getOffices(), value => this.offices.set(value)); }
  private loadLeagues(): void { this.load(this.api.getLeagues(this.officeId), value => this.leagues.set(value)); }
  private loadTeams(): void { this.load(this.api.getTeams(this.leagueId), value => this.teams.set(value)); }
  private loadDetail(): void { this.load(this.api.getTeamDetail(this.leagueId, this.teamId), value => this.detail.set(value)); }
  private cancel(): void { this.subscription?.unsubscribe(); this.loading.set(false); this.error.set(false); }
  private load<T>(source: import('rxjs').Observable<T>, set: (value: T) => void): void {
    this.cancel(); this.loading.set(true);
    this.subscription = source.subscribe({
      next: value => { set(value); this.loading.set(false); },
      error: error => { console.error('Unable to load team data', error); this.error.set(true); this.loading.set(false); },
    });
  }
}
