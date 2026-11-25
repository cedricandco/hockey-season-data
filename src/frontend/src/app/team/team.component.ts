import { Component, DestroyRef, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, ParamMap, Router, RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { Observable, Subscription } from 'rxjs';
import { ApiService } from '../core/api.service';
import {
  FollowedTeam, NavChoice, NavDivision, NavLeague, NavTeam, RouteSelection, TeamPageDetail, TeamPagePath,
} from './team.models';
import { TeamStatsComponent } from './team-stats.component';
import { nextSelectionStep, sameSelection } from './team-route';

function positiveInt(value: string | null): number {
  const parsed = Number(value);
  return Number.isSafeInteger(parsed) && parsed > 0 ? parsed : 0;
}

function selectionOf(params: ParamMap): RouteSelection {
  return {
    association: positiveInt(params.get('association')),
    division: positiveInt(params.get('division')),
    league: positiveInt(params.get('league')),
    team: positiveInt(params.get('team')),
  };
}

const NO_SELECTION: RouteSelection = { association: 0, division: 0, league: 0, team: 0 };

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
 *
 * The selection lives in the query string, so a view of the page is linkable —
 * the next games link their opponent that way. The page watches the parameters
 * instead of reading them once, because the router reuses this component
 * between two links on the same page; {@link nextSelectionStep} decides what to
 * load next, which keeps the restore exact across the asynchronous reads.
 */
@Component({
  selector: 'app-team',
  standalone: true,
  imports: [FormsModule, RouterLink, TranslatePipe, TeamStatsComponent],
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
                <li class="follow-item" [class.selected]="team.teamId === selection().team">
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
                <select [ngModel]="selection().association" (ngModelChange)="selectAssociation($event)">
                  <option [ngValue]="0">{{ 'TEAM.SELECT' | translate }}</option>
                  @for (association of associations(); track association.associationId) {
                    <option [ngValue]="association.associationId">{{ association.name }}</option>
                  }
                </select>
              </label>
              <label>{{ 'TEAM.DIVISION' | translate }}
                <select [ngModel]="selection().division" (ngModelChange)="selectDivision($event)"
                  [disabled]="!selection().association || loading()">
                  <option [ngValue]="0">{{ 'TEAM.SELECT' | translate }}</option>
                  @for (division of divisions(); track division.divisionId) {
                    <option [ngValue]="division.divisionId">{{ division.name }}</option>
                  }
                </select>
              </label>
              <label>{{ 'TEAM.LEAGUE' | translate }}
                <select [ngModel]="selection().league" (ngModelChange)="selectLeague($event)"
                  [disabled]="!selection().division || loading()">
                  <option [ngValue]="0">{{ 'TEAM.SELECT' | translate }}</option>
                  @for (league of leagues(); track league.leagueId) {
                    <option [ngValue]="league.leagueId">{{ league.name }}</option>
                  }
                </select>
              </label>
              <label>{{ 'TEAM.TEAM' | translate }}
                <select [ngModel]="selection().team" (ngModelChange)="selectTeam($event)"
                  [disabled]="!selection().league || loading()">
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
      <app-team-stats [team]="detail()" [path]="path()" />
    </section>
  `,
})
export class TeamComponent {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private subscription?: Subscription;
  private followsSubscription?: Subscription;
  private routeSubscription?: Subscription;
  /** Query string currently wanted; re-read after each level finishes loading. */
  private wanted = NO_SELECTION;

  readonly followed = signal<FollowedTeam[]>([]);
  readonly associations = signal<NavChoice[]>([]);
  readonly divisions = signal<NavDivision[]>([]);
  readonly leagues = signal<NavLeague[]>([]);
  readonly teams = signal<NavTeam[]>([]);
  readonly selection = signal<RouteSelection>(NO_SELECTION);
  readonly detail = signal<TeamPageDetail | null>(null);
  readonly loading = signal(false);
  readonly loadingFollows = signal(true);
  readonly error = signal(false);
  readonly noFollows = signal(false);
  /** Association/division/league of the selected team, restored by each opponent link. */
  readonly path = signal<TeamPagePath | null>(null);
  /** Null means "not read yet": the restore must load the level before trusting an absence. */
  private loaded = { associations: false, divisions: false, leagues: false, teams: false };

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      this.subscription?.unsubscribe();
      this.followsSubscription?.unsubscribe();
      this.routeSubscription?.unsubscribe();
    });
    this.loadFollows();
    // The associations are the entry point of every restore, so they are read
    // before the parameters are watched.
    this.routeSubscription = this.route.queryParamMap.subscribe(params => {
      this.wanted = selectionOf(params);
      this.applyWanted();
    });
  }

  selectAssociation(id: number): void {
    this.setSelection({ ...NO_SELECTION, association: id });
    this.syncUrl();
    if (id) this.loadDivisions(); else this.cancel();
  }
  selectDivision(id: number): void {
    const current = this.selection();
    this.setSelection({ ...NO_SELECTION, association: current.association, division: id });
    this.syncUrl();
    if (id) this.loadLeagues(); else this.cancel();
  }
  selectLeague(id: number): void {
    const current = this.selection();
    this.setSelection({ ...NO_SELECTION, association: current.association, division: current.division, league: id });
    this.syncUrl();
    if (id) this.loadTeams(); else this.cancel();
  }
  selectTeam(id: number): void {
    const followed = this.followed().find(item => item.teamId === id);
    if (followed && followed.leagueId !== this.selection().league) {
      // A team picked in the follow list: the selector is aligned on its path
      // and each level is loaded, so the drop-downs show where the team lives.
      this.setSelection({ association: followed.associationId, division: followed.divisionId, league: followed.leagueId, team: id });
      this.syncUrl();
      this.loadDivisions(() => this.loadLeagues(() => this.loadTeams(() => this.loadDetail())));
      return;
    }
    this.setSelection({ ...this.selection(), team: id });
    this.syncUrl();
    if (id) this.loadDetail(); else this.cancel();
  }

  /**
   * Walks towards {@link wanted} one level at a time, loading the level it
   * cannot trust yet and asking again once it arrives. A URL level that does
   * not exist in the follows stops the walk at the deepest valid selection, so
   * the page can never offer a path outside them.
   */
  private applyWanted(): void {
    // The association list is the entry point of the page: without it the
    // restore cannot decide anything, even when the URL holds no selection.
    if (!this.loaded.associations) {
      this.loadAssociations();
      return;
    }

    const step = nextSelectionStep({
      associations: this.loaded.associations ? this.associations().map(item => item.associationId) : null,
      divisions: this.loaded.associations && this.loaded.divisions ? this.divisions().map(item => item.divisionId) : null,
      leagues: this.loaded.divisions && this.loaded.leagues ? this.leagues().map(item => item.leagueId) : null,
      teams: this.loaded.leagues && this.loaded.teams ? this.teams().map(item => item.teamId) : null,
      selection: this.selection(),
    }, this.wanted);

    if (!sameSelection(step.selection, this.selection())) this.setSelection(step.selection);

    switch (step.action) {
      case 'load-associations': this.loadAssociations(); break;
      case 'load-divisions': this.loadDivisions(() => this.applyWanted()); break;
      case 'load-leagues': this.loadLeagues(() => this.applyWanted()); break;
      case 'load-teams': this.loadTeams(() => this.applyWanted()); break;
      case 'load-team': this.loadDetail(); break;
      case 'settled':
        // A truncated selection leaves the URL ahead of the page: rewrite it so
        // the address bar matches what is shown (a stale team id must not stay).
        if (!sameSelection(step.selection, this.wanted)) this.syncUrl();
        break;
    }
  }

  /** Adopts a selection and drops what belonged to a level that is no longer valid. */
  private setSelection(selection: RouteSelection): void {
    const current = this.selection();
    if (selection.association !== current.association) {
      this.divisions.set([]); this.leagues.set([]); this.teams.set([]);
      this.loaded = { ...this.loaded, divisions: false, leagues: false, teams: false };
    } else if (selection.division !== current.division) {
      this.leagues.set([]); this.teams.set([]);
      this.loaded = { ...this.loaded, leagues: false, teams: false };
    } else if (selection.league !== current.league) {
      this.teams.set([]);
      this.loaded = { ...this.loaded, teams: false };
    }
    this.selection.set(selection);
    if (selection.team !== current.team) this.detail.set(null);
    this.cancel();
  }

  private syncUrl(): void {
    const selection = this.selection();
    void this.router.navigate([], {
      queryParams: {
        association: selection.association || null,
        division: selection.division || null,
        league: selection.league || null,
        team: selection.team || null,
      },
      replaceUrl: true,
    });
  }

  retry(): void {
    const selection = this.selection();
    if (selection.team) this.loadDetail();
    else if (selection.league) this.loadTeams();
    else if (selection.division) this.loadLeagues();
    else if (selection.association) this.loadDivisions();
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
    this.load(this.api.getNavAssociations(), value => {
      this.associations.set(value);
      this.loaded.associations = true;
    }, done ?? (() => this.applyWanted()));
  }
  private loadDivisions(done?: () => void): void {
    this.load(this.api.getNavDivisions(this.selection().association), value => {
      this.divisions.set(value);
      this.loaded.divisions = true;
    }, done);
  }
  private loadLeagues(done?: () => void): void {
    this.load(this.api.getNavLeagues(this.selection().division), value => {
      this.leagues.set(value);
      this.loaded.leagues = true;
    }, done);
  }
  private loadTeams(done?: () => void): void {
    this.load(this.api.getNavTeams(this.selection().league), value => {
      this.teams.set(value);
      this.loaded.teams = true;
    }, done);
  }
  private loadDetail(): void {
    // The backend validates the follow and resolves the schedule, so the page
    // only sends the database team id.
    const selection = this.selection();
    this.path.set({ association: selection.association, division: selection.division, league: selection.league });
    this.load(this.api.getFollowedTeamDetail(selection.team), value => this.detail.set(value));
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
