import { Component, DestroyRef, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { Observable, Subscription } from 'rxjs';
import { ApiService } from '../core/api.service';
import { FollowedTeam, NavChoice, NavDivision, NavLeague, NavTeam, TeamDetail } from './team.models';
import { TeamSummaryComponent } from './team-summary.component';

function positiveInt(value: string | null): number {
  const parsed = Number(value);
  return Number.isSafeInteger(parsed) && parsed > 0 ? parsed : 0;
}

/**
 * Team page, in two blocks over the teams the caller follows
 * (`GET /api/nav/teams`):
 *
 * 1. the follow list — every team I follow, with its league, division and
 *    association names; picking one loads its detail;
 * 2. the selector — association → division → league → team. The first three
 *    levels list only the values with at least one followed team; picking a
 *    league opens it: its team list then holds every team of the league, not
 *    only the followed one, so the opponents of my team are browsable too
 *    (following a team opens its whole league).
 *
 * Every identifier is a database id: the detail call is keyed on `teams.id`
 * and the backend resolves the published Spordle schedule itself.
 */
@Component({
  selector: 'app-team',
  standalone: true,
  imports: [FormsModule, RouterLink, TranslatePipe, TeamSummaryComponent],
  styleUrl: './team.component.scss',
  template: `
    <section class="team-page">
      <h1>{{ 'TEAM.TITLE' | translate }}</h1><p>{{ 'TEAM.SUBTITLE' | translate }}</p>
      @if (loadingFollows()) {
        <p role="status">{{ 'TEAM.LOADING' | translate }}</p>
      } @else if (noFollows()) {
        <p class="empty" role="status">
          {{ 'TEAM.NO_FOLLOWS' | translate }}
          <a class="config-link" routerLink="/configuration">{{ 'TEAM.NO_FOLLOWS_LINK' | translate }}</a>
        </p>
      } @else {
        <div class="team-blocks">
          <section class="follow-list-block" [attr.aria-label]="'TEAM.MY_TEAMS' | translate">
            <h2>{{ 'TEAM.MY_TEAMS' | translate }}</h2>
            <ul class="follow-list">
              @for (team of followed(); track team.teamId) {
                <li class="follow-item" [class.selected]="team.teamId === teamId">
                  <button type="button" class="follow-button" (click)="selectTeam(team.teamId)">
                    <span class="follow-name">{{ team.name }}</span>
                    <span class="follow-context">{{ team.leagueName }} · {{ team.divisionName }} · {{ team.associationName }}</span>
                  </button>
                </li>
              }
            </ul>
          </section>

          <section class="selector-block" [attr.aria-label]="'TEAM.SELECTOR' | translate">
            <h2>{{ 'TEAM.SELECTOR' | translate }}</h2>
            <div class="selectors">
              <label>{{ 'TEAM.OFFICE' | translate }}
                <select [ngModel]="associationId" (ngModelChange)="selectAssociation($event)">
                  <option [ngValue]="0">{{ 'TEAM.SELECT' | translate }}</option>
                  @for (association of associations(); track association.associationId) {
                    <option [ngValue]="association.associationId">{{ association.name }}</option>
                  }
                </select>
              </label>
              <label>{{ 'TEAM.DIVISION' | translate }}
                <select [ngModel]="divisionId" (ngModelChange)="selectDivision($event)" [disabled]="!associationId || loading()">
                  <option [ngValue]="0">{{ 'TEAM.SELECT' | translate }}</option>
                  @for (division of divisions(); track division.divisionId) {
                    <option [ngValue]="division.divisionId">{{ division.name }}</option>
                  }
                </select>
              </label>
              <label>{{ 'TEAM.LEAGUE' | translate }}
                <select [ngModel]="leagueId" (ngModelChange)="selectLeague($event)" [disabled]="!divisionId || loading()">
                  <option [ngValue]="0">{{ 'TEAM.SELECT' | translate }}</option>
                  @for (league of leagues(); track league.leagueId) {
                    <option [ngValue]="league.leagueId">{{ league.name }}</option>
                  }
                </select>
              </label>
              <label>{{ 'TEAM.TEAM' | translate }}
                <select [ngModel]="teamId" (ngModelChange)="selectTeam($event)" [disabled]="!leagueId || loading()">
                  <option [ngValue]="0">{{ 'TEAM.SELECT' | translate }}</option>
                  @for (team of teams(); track team.teamId) { <option [ngValue]="team.teamId">{{ team.name }}</option> }
                </select>
              </label>
            </div>
          </section>
        </div>

        @if (loading()) { <p role="status">{{ 'TEAM.LOADING' | translate }}</p> }
        @if (error()) { <p class="error" role="alert">{{ 'TEAM.ERROR' | translate }} <button type="button" (click)="retry()">{{ 'TEAM.RETRY' | translate }}</button></p> }
      }
      <app-team-summary [team]="detail()" />
    </section>
  `,
})
export class TeamComponent {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private subscription?: Subscription;
  private followsSubscription?: Subscription;
  readonly followed = signal<FollowedTeam[]>([]);
  readonly associations = signal<NavChoice[]>([]);
  readonly divisions = signal<NavDivision[]>([]);
  readonly leagues = signal<NavLeague[]>([]);
  readonly teams = signal<NavTeam[]>([]);
  readonly detail = signal<TeamDetail | null>(null);
  readonly loading = signal(false);
  readonly loadingFollows = signal(true);
  readonly error = signal(false);
  readonly noFollows = signal(false);

  associationId = 0;
  divisionId = 0;
  leagueId = 0;
  teamId = 0;

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      this.subscription?.unsubscribe();
      this.followsSubscription?.unsubscribe();
    });
    const params = inject(ActivatedRoute).snapshot.queryParamMap;
    // The follow list fills the two blocks of the page; the associations are
    // read separately because the URL levels are restored through them.
    this.loadFollows();
    this.loadAssociations(() => this.restore(positiveInt(params.get('association')),
      positiveInt(params.get('division')), positiveInt(params.get('league')), positiveInt(params.get('team'))));
  }

  selectAssociation(id: number): void {
    this.associationId = id; this.divisionId = 0; this.leagueId = 0; this.teamId = 0;
    this.divisions.set([]); this.leagues.set([]); this.teams.set([]); this.detail.set(null);
    this.syncUrl();
    if (id) this.loadDivisions(); else this.cancel();
  }
  selectDivision(id: number): void {
    this.divisionId = id; this.leagueId = 0; this.teamId = 0;
    this.leagues.set([]); this.teams.set([]); this.detail.set(null);
    this.syncUrl();
    if (id) this.loadLeagues(); else this.cancel();
  }
  selectLeague(id: number): void {
    this.leagueId = id; this.teamId = 0; this.teams.set([]); this.detail.set(null);
    this.syncUrl();
    if (id) this.loadTeams(); else this.cancel();
  }
  selectTeam(id: number): void {
    this.detail.set(null);
    const followed = this.followed().find(item => item.teamId === id);
    if (followed && followed.leagueId !== this.leagueId) {
      // A team picked in the follow list: the selector is aligned on its path
      // and each level is loaded, so the drop-downs show where the team lives.
      this.associationId = followed.associationId;
      this.divisionId = followed.divisionId;
      this.leagueId = followed.leagueId;
      this.teamId = id;
      this.syncUrl();
      this.loadDivisions(() => this.loadLeagues(() => this.loadTeams(() => this.loadDetail())));
      return;
    }
    this.teamId = id;
    this.syncUrl();
    if (id) this.loadDetail(); else this.cancel();
  }

  // Applies the association/division/league/team query parameters one level at a time.
  private restore(association: number, division: number, league: number, team: number): void {
    if (!association || !this.associations().some(item => item.associationId === association)) return;
    this.associationId = association;
    this.loadDivisions(() => {
      if (!division || !this.divisions().some(item => item.divisionId === division)) return;
      this.divisionId = division;
      this.loadLeagues(() => {
        if (!league || !this.leagues().some(item => item.leagueId === league)) return;
        this.leagueId = league;
        this.loadTeams(() => {
          if (!team || !this.teams().some(item => item.teamId === team)) return;
          this.teamId = team;
          this.loadDetail();
        });
      });
    });
  }
  private syncUrl(): void {
    void this.router.navigate([], {
      queryParams: {
        association: this.associationId || null,
        division: this.divisionId || null,
        league: this.leagueId || null,
        team: this.teamId || null,
      },
      replaceUrl: true,
    });
  }
  retry(): void {
    if (this.teamId) this.loadDetail();
    else if (this.leagueId) this.loadTeams();
    else if (this.divisionId) this.loadLeagues();
    else if (this.associationId) this.loadDivisions();
    else { this.loadFollows(); this.loadAssociations(); }
  }

  /**
   * The follow list drives both blocks: it fills the team list and the
   * association list, while the lower levels are read from the navigation
   * endpoints. Those only list the values that own at least one followed
   * team, so an empty level can never be picked.
   */
  private loadFollows(): void {
    this.followsSubscription?.unsubscribe();
    this.loadingFollows.set(true);
    this.followsSubscription = this.api.getFollowedNavTeams().subscribe({
      next: value => { this.followed.set(value); this.noFollows.set(value.length === 0); this.loadingFollows.set(false); },
      error: error => { console.error('Unable to load followed teams', error); this.error.set(true); this.loadingFollows.set(false); },
    });
  }
  private loadAssociations(done?: () => void): void {
    this.load(this.api.getNavAssociations(), value => this.associations.set(value), done);
  }
  private loadDivisions(done?: () => void): void {
    this.load(this.api.getNavDivisions(this.associationId), value => this.divisions.set(value), done);
  }
  private loadLeagues(done?: () => void): void {
    this.load(this.api.getNavLeagues(this.divisionId), value => this.leagues.set(value), done);
  }
  private loadTeams(done?: () => void): void {
    this.load(this.api.getNavTeams(this.leagueId), value => this.teams.set(value), done);
  }
  private loadDetail(): void {
    // The backend validates the follow and resolves the schedule, so the page
    // only sends the database team id.
    this.load(this.api.getFollowedTeamDetail(this.teamId), value => this.detail.set(value));
  }
  private cancel(): void { this.subscription?.unsubscribe(); this.loading.set(false); this.error.set(false); }
  private load<T>(source: Observable<T>, set: (value: T) => void, done?: () => void): void {
    this.cancel(); this.loading.set(true);
    this.subscription = source.subscribe({
      next: value => { set(value); this.loading.set(false); done?.(); },
      error: error => { console.error('Unable to load team data', error); this.error.set(true); this.loading.set(false); },
    });
  }
}