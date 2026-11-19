import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { ApiService } from '../core/api.service';
import { AccountService } from '../core/account.service';
import { skipOnboarding } from '../core/team-onboarding';
import {
  FollowedTeam,
  SubscriptionAssociation,
  SubscriptionDivision,
  SubscriptionLeague,
  SubscriptionsResponse,
  SubscriptionTeam,
} from '../config/subscription.models';
import {
  alreadyFollowed,
  canAddTeam,
  canFollowMoreTeams,
  canLoadDivisions,
  canLoadLeagues,
  canLoadTeams,
  formatUsage,
  limitOf,
} from '../config/subscription-helpers';

/**
 * Team-selection page shown right after a first connection (the account was
 * just provisioned as a COACH by the backend).
 *
 * It walks the reference data — association → division → league → team — and
 * follows each picked team, bounded by the COACH licence of the caller. The
 * teams followed here are listed below so the page doubles as the first
 * "my teams" view; "continue" enters the app, "later" is remembered for the
 * browser session only.
 */
@Component({
  selector: 'app-team-selection',
  standalone: true,
  imports: [FormsModule, TranslatePipe],
  styleUrl: './team-selection.component.scss',
  template: `
    <section class="page-container selection" aria-labelledby="selection-title">
      <header class="selection-header">
        <h1 id="selection-title" class="page-title">{{ 'SELECTION.TITLE' | translate }}</h1>
        <p class="page-subtitle">{{ 'SELECTION.SUBTITLE' | translate }}</p>
        @if (teamLimit()) {
          <span class="limit-badge" [class.limit-full]="!canFollowMore()">{{ usage() }}</span>
        }
      </header>

      @if (loading()) {
        <p class="selection-state" role="status">{{ 'SELECTION.LOADING' | translate }}</p>
      } @else if (loadError()) {
        <p class="notice" role="alert">
          {{ 'SELECTION.LOAD_ERROR' | translate }}
          <button type="button" class="link" (click)="loadSubscriptions()">{{ 'SELECTION.RETRY' | translate }}</button>
        </p>
      } @else {
        @if (hasTeamLicence()) {
          <div class="selector-card">
            <h2 class="selector-title">{{ 'SELECTION.ADD_TITLE' | translate }}</h2>
            <div class="selector-grid">
              <label class="field">
                <span>{{ 'SELECTION.ASSOCIATION' | translate }}</span>
                <select [ngModel]="associationId()" (ngModelChange)="selectAssociation($event)"
                  [disabled]="catalogLoading()">
                  <option [ngValue]="null">{{ 'SELECTION.SELECT' | translate }}</option>
                  @for (association of associations(); track association.id) {
                    <option [ngValue]="association.id">{{ association.name }}</option>
                  }
                </select>
              </label>

              <label class="field">
                <span>{{ 'SELECTION.DIVISION' | translate }}</span>
                <select [ngModel]="divisionId()" (ngModelChange)="selectDivision($event)"
                  [disabled]="!divisionsEnabled() || catalogLoading()">
                  <option [ngValue]="null">{{ 'SELECTION.SELECT' | translate }}</option>
                  @for (division of divisions(); track division.id) {
                    <option [ngValue]="division.id">{{ division.name }}</option>
                  }
                </select>
              </label>

              <label class="field">
                <span>{{ 'SELECTION.LEAGUE' | translate }}</span>
                <select [ngModel]="leagueId()" (ngModelChange)="selectLeague($event)"
                  [disabled]="!leaguesEnabled() || catalogLoading()">
                  <option [ngValue]="null">{{ 'SELECTION.SELECT' | translate }}</option>
                  @for (league of leagues(); track league.id) {
                    <option [ngValue]="league.id">{{ league.name }}</option>
                  }
                </select>
              </label>

              <label class="field">
                <span>{{ 'SELECTION.TEAM' | translate }}</span>
                <select [ngModel]="teamId()" (ngModelChange)="teamId.set($event)"
                  [disabled]="!teamsEnabled() || catalogLoading()">
                  <option [ngValue]="null">{{ 'SELECTION.SELECT' | translate }}</option>
                  @for (team of teams(); track team.id) {
                    <option [ngValue]="team.id">{{ team.name }}</option>
                  }
                </select>
              </label>
            </div>

            <div class="selector-actions">
              <button type="button" class="add-button" [disabled]="!addEnabled() || adding()" (click)="addTeam()">
                {{ (adding() ? 'SELECTION.ADDING' : 'SELECTION.ADD') | translate }}
              </button>
              @if (selectedTeamFollowed()) {
                <span class="hint" role="status">{{ 'SELECTION.ALREADY_FOLLOWED' | translate }}</span>
              }
              @if (!canFollowMore()) {
                <span class="hint hint-full" role="status">{{ 'SELECTION.LIMIT_REACHED' | translate }}</span>
              }
            </div>

            @if (catalogError()) {
              <p class="notice" role="alert">{{ 'SELECTION.CATALOG_ERROR' | translate }}</p>
            }
            @if (addError()) {
              <p class="notice" role="alert">{{ addError()! | translate }}</p>
            }
          </div>
        }

        <h2 class="list-title">{{ 'SELECTION.MY_TEAMS' | translate }}</h2>
        @if (followedTeams().length === 0) {
          <p class="empty">{{ 'SELECTION.EMPTY' | translate }}</p>
        } @else {
          <ul class="followed-list">
            @for (team of followedTeams(); track team.teamId) {
              <li class="followed-item">
                <span class="followed-name">{{ team.name ?? ('SELECTION.TEAM_UNKNOWN' | translate: { id: team.teamId }) }}</span>
                <button type="button" class="remove-button" [disabled]="removingId() === team.teamId"
                  (click)="removeTeam(team.teamId)">
                  {{ (removingId() === team.teamId ? 'SELECTION.REMOVING' : 'SELECTION.REMOVE') | translate }}
                </button>
              </li>
            }
          </ul>
        }
        @if (removeError()) {
          <p class="notice" role="alert">{{ removeError()! | translate }}</p>
        }
      }

      <div class="selection-actions">
        <button type="button" class="continue-button" [disabled]="followedTeams().length === 0" (click)="continueToApp()">
          {{ 'SELECTION.CONTINUE' | translate }}
        </button>
        <button type="button" class="later-button" (click)="later()">{{ 'SELECTION.LATER' | translate }}</button>
      </div>
    </section>
  `,
})
export class TeamSelectionComponent {
  private readonly api = inject(ApiService);
  private readonly account = inject(AccountService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly subscriptions = signal<SubscriptionsResponse | null>(null);
  readonly loading = signal(true);
  readonly loadError = signal(false);

  readonly associations = signal<SubscriptionAssociation[]>([]);
  readonly divisions = signal<SubscriptionDivision[]>([]);
  readonly leagues = signal<SubscriptionLeague[]>([]);
  readonly teams = signal<SubscriptionTeam[]>([]);
  readonly associationId = signal<number | null>(null);
  readonly divisionId = signal<number | null>(null);
  readonly leagueId = signal<string | null>(null);
  readonly teamId = signal<number | null>(null);
  readonly catalogLoading = signal(false);
  readonly catalogError = signal(false);

  readonly adding = signal(false);
  readonly addError = signal<string | null>(null);
  readonly removingId = signal<number | null>(null);
  readonly removeError = signal<string | null>(null);

  readonly followedTeams = computed<FollowedTeam[]>(() => this.subscriptions()?.teams ?? []);
  readonly teamLimit = computed(() => limitOf(this.subscriptions(), 'team'));
  readonly usage = computed(() => formatUsage(this.teamLimit()));
  readonly canFollowMore = computed(() => canFollowMoreTeams(this.subscriptions()));
  /** A caller without the COACH licence (max 0) cannot follow a team at all. */
  readonly hasTeamLicence = computed(() => this.teamLimit()?.max !== 0);

  constructor() {
    this.loadSubscriptions();
    this.api.getSubscriptionAssociations().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: ({ associations }) => {
        this.associations.set(associations);
        // A single association needs no picking: preselect it to save a step.
        if (associations.length === 1) this.selectAssociation(associations[0].id);
      },
      error: () => this.catalogError.set(true),
    });
  }

  divisionsEnabled(): boolean {
    return canLoadDivisions(this.associationId());
  }

  leaguesEnabled(): boolean {
    return canLoadLeagues(this.divisionId());
  }

  teamsEnabled(): boolean {
    return canLoadTeams(this.leagueId());
  }

  addEnabled(): boolean {
    return canAddTeam(this.teamId(), this.subscriptions()) && !this.selectedTeamFollowed();
  }

  selectedTeamFollowed(): boolean {
    return alreadyFollowed(this.followedTeams(), this.teamId());
  }

  loadSubscriptions(): void {
    this.loading.set(true);
    this.loadError.set(false);
    this.api.getSubscriptions().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (subscriptions) => {
        this.subscriptions.set(subscriptions);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.loadError.set(true);
      },
    });
  }

  selectAssociation(id: number | null): void {
    this.associationId.set(id);
    this.divisionId.set(null);
    this.leagueId.set(null);
    this.teamId.set(null);
    this.divisions.set([]);
    this.leagues.set([]);
    this.teams.set([]);
    if (id !== null) this.loadDivisions(id);
  }

  selectDivision(id: number | null): void {
    this.divisionId.set(id);
    this.leagueId.set(null);
    this.teamId.set(null);
    this.leagues.set([]);
    this.teams.set([]);
    if (id !== null) this.loadLeagues(id);
  }

  selectLeague(id: string | null): void {
    this.leagueId.set(id);
    this.teamId.set(null);
    this.teams.set([]);
    if (id !== null) this.loadTeams(id);
  }

  addTeam(): void {
    const teamId = this.teamId();
    if (teamId === null || !this.addEnabled() || this.adding()) return;

    this.adding.set(true);
    this.addError.set(null);
    this.api.addTeamSubscription(teamId).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.adding.set(false);
        this.teamId.set(null);
        this.loadSubscriptions();
        // Refresh the cached account so the first-run guard sees the new team
        // count on later navigations (best effort: the follow is persisted).
        void this.account.load(true);
      },
      error: (error: { status?: number }) => {
        this.adding.set(false);
        this.addError.set(this.addErrorKey(error.status));
        // The cap may have been reached concurrently: refresh the headroom.
        if (error.status === 409) this.loadSubscriptions();
      },
    });
  }

  removeTeam(teamId: number): void {
    if (this.removingId() !== null) return;

    this.removingId.set(teamId);
    this.removeError.set(null);
    this.api.removeTeamSubscription(teamId).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.removingId.set(null);
        this.loadSubscriptions();
      },
      error: (error: { status?: number }) => {
        this.removingId.set(null);
        this.removeError.set(error.status === 423 ? 'SELECTION.ERROR_LOCKED' : 'SELECTION.ERROR_REMOVE');
      },
    });
  }

  /** Enters the app with the team(s) picked above. */
  continueToApp(): void {
    void this.router.navigateByUrl('/chat');
  }

  /** "Later": remembered for this browser session so the guard does not loop. */
  later(): void {
    skipOnboarding(this.sessionStorage());
    void this.router.navigateByUrl('/chat');
  }

  private sessionStorage(): Storage | null {
    return typeof sessionStorage === 'undefined' ? null : sessionStorage;
  }

  private addErrorKey(status?: number): string {
    if (status === 409) return 'SELECTION.ERROR_LIMIT';
    if (status === 404) return 'SELECTION.ERROR_UNKNOWN_TEAM';
    return 'SELECTION.ERROR_ADD';
  }

  private loadDivisions(associationId: number): void {
    this.catalogLoading.set(true);
    this.catalogError.set(false);
    this.api.getSubscriptionDivisions(associationId).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: ({ divisions }) => {
        this.divisions.set(divisions);
        this.catalogLoading.set(false);
      },
      error: () => {
        this.catalogLoading.set(false);
        this.catalogError.set(true);
      },
    });
  }

  private loadLeagues(divisionId: number): void {
    this.catalogLoading.set(true);
    this.catalogError.set(false);
    this.api.getSubscriptionLeagues(divisionId).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: ({ leagues }) => {
        this.leagues.set(leagues);
        this.catalogLoading.set(false);
      },
      error: () => {
        this.catalogLoading.set(false);
        this.catalogError.set(true);
      },
    });
  }

  private loadTeams(leagueId: string): void {
    this.catalogLoading.set(true);
    this.catalogError.set(false);
    this.api.getSubscriptionTeams(leagueId).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: ({ teams }) => {
        this.teams.set(teams);
        this.catalogLoading.set(false);
      },
      error: () => {
        this.catalogLoading.set(false);
        this.catalogError.set(true);
      },
    });
  }
}
