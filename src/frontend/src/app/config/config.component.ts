import { Component, computed, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { ApiService } from '../core/api.service';
import { AccountService } from '../core/account.service';
import { ThemeService } from '../core/theme.service';
import {
  FollowedTeam,
  PresidentTeam,
  SubscriptionAssociation,
  SubscriptionDivision,
  SubscriptionLeague,
  SubscriptionsResponse,
  SubscriptionTeam,
} from './subscription.models';
import {
  alreadyFollowed,
  baseNameOf,
  canAddTeam,
  canFollowBaseName,
  canFollowMoreTeams,
  canLoadDivisions,
  canLoadLeagues,
  canLoadTeams,
  formatUsage,
  isAssociationPresident,
  isRemovable,
  limitOf,
  matchesBaseName,
} from './subscription-helpers';

/**
 * Configuration page: per-device primary/secondary color choices (persisted in
 * local storage by the ThemeService) and, for every signed-in user, the teams
 * they follow.
 *
 * A team is followed by walking the reference data — association → division →
 * league → team — and adding is bounded by the licence limit of the caller's
 * role (`roles.max_followed_entities` of the team kind): the page shows
 * "3 / 5" and disables the button when full. The followed teams can be pruned,
 * except during their 15-day removal lock.
 *
 * A caller holding the PRESIDENT licence also gets an "association president"
 * block: it picks an association and types a team base name ("Grizzly"), then
 * every team of the association whose name starts with it — in all divisions,
 * leagues and categories — is followed in one call. That scope lifts the team
 * cap and the removal lock, so the president prunes unwanted teams right away
 * with the regular remove button.
 */
@Component({
  selector: 'app-config',
  standalone: true,
  imports: [FormsModule, TranslatePipe],
  styleUrl: './config.component.scss',
  template: `
    <section class="page-container config" aria-labelledby="config-title">
      <h1 id="config-title" class="page-title">{{ 'THEME.TITLE' | translate }}</h1>
      <p class="page-subtitle">{{ 'THEME.DESCRIPTION' | translate }}</p>
      <div class="config-card">
        <label class="setting-row">
          <span>{{ 'THEME.PRIMARY' | translate }}</span>
          <input type="color" [value]="theme.primaryColor()" (input)="theme.setColor('primary', $event)">
        </label>
        <label class="setting-row">
          <span>{{ 'THEME.SECONDARY' | translate }}</span>
          <input type="color" [value]="theme.secondaryColor()" (input)="theme.setColor('secondary', $event)">
        </label>
        <button class="reset-button" type="button" (click)="theme.resetColors()">{{ 'THEME.RESET' | translate }}</button>
        @if (theme.storageError()) {
          <p class="storage-error" role="alert">{{ 'THEME.STORAGE_ERROR' | translate }}</p>
        }
      </div>

      <section class="follow-section" aria-labelledby="follow-title">        <div class="section-heading">
          <div>
            <h2 id="follow-title">{{ 'FOLLOW.TITLE' | translate }}</h2>
            <p class="section-subtitle">{{ 'FOLLOW.SUBTITLE' | translate }}</p>
          </div>
          @if (teamLimit()) {
            <span class="limit-badge" [class.limit-full]="!canFollowMore()">
              {{ usage() }}
            </span>
          }
        </div>

        @if (loading()) {
          <p class="follow-state" role="status">{{ 'FOLLOW.LOADING' | translate }}</p>
        } @else if (loadError()) {
          <p class="notice" role="alert">
            {{ 'FOLLOW.LOAD_ERROR' | translate }}
            <button type="button" class="link" (click)="loadSubscriptions()">{{ 'FOLLOW.RETRY' | translate }}</button>
          </p>
        } @else {
          @if (isPresident()) {
            <div class="selector-card president-card">
              <h3 class="selector-title">{{ 'FOLLOW.PRESIDENT_TITLE' | translate }}</h3>
              <p class="section-subtitle">{{ 'FOLLOW.PRESIDENT_SUBTITLE' | translate }}</p>
              <div class="selector-grid president-grid">
                <label class="field">
                  <span>{{ 'FOLLOW.ASSOCIATION' | translate }}</span>
                  <select [ngModel]="presidentAssociationId()" (ngModelChange)="selectPresidentAssociation($event)"
                    [disabled]="catalogLoading() || presidentAdding()">
                    <option [ngValue]="null">{{ 'FOLLOW.SELECT' | translate }}</option>
                    @for (association of associations(); track association.id) {
                      <option [ngValue]="association.id">{{ association.name }}</option>
                    }
                  </select>
                </label>
                <label class="field">
                  <span>{{ 'FOLLOW.BASE_NAME' | translate }}</span>
                  <input type="text" list="association-team-bases"
                    [placeholder]="'FOLLOW.BASE_NAME_PLACEHOLDER' | translate"
                    [ngModel]="presidentBaseName()" (ngModelChange)="setPresidentBaseName($event)"
                    [disabled]="presidentMatchesLoading() || presidentAdding()"
                    (keydown.enter)="previewPresidentTeams()" />
                  <datalist id="association-team-bases">
                    @for (suggestion of presidentBaseSuggestions(); track suggestion) {
                      <option [value]="suggestion"></option>
                    }
                  </datalist>
                </label>
              </div>

              <div class="selector-actions">
                <button type="button" class="add-button"
                  [disabled]="!canPreviewPresidentTeams() || presidentMatchesLoading()"
                  (click)="previewPresidentTeams()">
                  {{ (presidentMatchesLoading() ? 'FOLLOW.SEARCHING' : 'FOLLOW.PREVIEW') | translate }}
                </button>
                <button type="button" class="add-button"
                  [disabled]="!presidentCanFollow()"
                  (click)="followPresidentTeams()">
                  {{ (presidentAdding() ? 'FOLLOW.ADDING' : 'FOLLOW.FOLLOW_ALL') | translate }}
                </button>
                @if (presidentAddedCount() > 0) {
                  <span class="hint" role="status">
                    {{ 'FOLLOW.FOLLOW_ALL_DONE' | translate: { count: presidentAddedCount() } }}
                  </span>
                }
              </div>

              @if (presidentMatches().length > 0) {
                <ul class="match-list" [attr.aria-label]="'FOLLOW.MATCHES' | translate">
                  @for (team of presidentMatches(); track team.id) {
                    <li class="match-item" [class.match-followed]="presidentTeamFollowed(team.id)">
                      <span class="match-name">{{ team.name }}</span>
                      <span class="match-meta">{{ team.leagueName }} · {{ team.divisionName }}</span>
                      @if (presidentTeamFollowed(team.id)) {
                        <span class="match-badge">✓</span>
                      }
                    </li>
                  }
                </ul>
              } @else if (presidentSearched()) {
                <p class="hint">{{ 'FOLLOW.NO_MATCH' | translate }}</p>
              }

              @if (presidentError()) {
                <p class="notice" role="alert">{{ presidentError()! | translate }}</p>
              }
            </div>
          }

          @if (hasTeamLicence()) {
            <div class="selector-card">
              <h3 class="selector-title">{{ 'FOLLOW.ADD_TITLE' | translate }}</h3>
              <div class="selector-grid">
                <label class="field">
                  <span>{{ 'FOLLOW.ASSOCIATION' | translate }}</span>
                  <select [ngModel]="associationId()" (ngModelChange)="selectAssociation($event)"
                    [disabled]="catalogLoading()">
                    <option [ngValue]="null">{{ 'FOLLOW.SELECT' | translate }}</option>
                    @for (association of associations(); track association.id) {
                      <option [ngValue]="association.id">{{ association.name }}</option>
                    }
                  </select>
                </label>

                <label class="field">
                  <span>{{ 'FOLLOW.DIVISION' | translate }}</span>
                  <select [ngModel]="divisionId()" (ngModelChange)="selectDivision($event)"
                    [disabled]="!divisionsEnabled() || catalogLoading()">
                    <option [ngValue]="null">{{ 'FOLLOW.SELECT' | translate }}</option>
                    @for (division of divisions(); track division.id) {
                      <option [ngValue]="division.id">{{ division.name }}</option>
                    }
                  </select>
                </label>

                <label class="field">
                  <span>{{ 'FOLLOW.LEAGUE' | translate }}</span>
                  <select [ngModel]="leagueId()" (ngModelChange)="selectLeague($event)"
                    [disabled]="!leaguesEnabled() || catalogLoading()">
                    <option [ngValue]="null">{{ 'FOLLOW.SELECT' | translate }}</option>
                    @for (league of leagues(); track league.id) {
                      <option [ngValue]="league.id">{{ league.name }}</option>
                    }
                  </select>
                </label>

                <label class="field">
                  <span>{{ 'FOLLOW.TEAM' | translate }}</span>
                  <select [ngModel]="teamId()" (ngModelChange)="teamId.set($event)"
                    [disabled]="!teamsEnabled() || catalogLoading()">
                    <option [ngValue]="null">{{ 'FOLLOW.SELECT' | translate }}</option>
                    @for (team of teams(); track team.id) {
                      <option [ngValue]="team.id">{{ team.name }}</option>
                    }
                  </select>
                </label>
              </div>

              <div class="selector-actions">
                <button type="button" class="add-button"
                  [disabled]="!addEnabled() || adding()"
                  (click)="addTeam()">
                  {{ (adding() ? 'FOLLOW.ADDING' : 'FOLLOW.ADD') | translate }}
                </button>
                @if (selectedTeamFollowed()) {
                  <span class="hint" role="status">{{ 'FOLLOW.ALREADY_FOLLOWED' | translate }}</span>
                }
                @if (!canFollowMore()) {
                  <span class="hint hint-full" role="status">{{ 'FOLLOW.LIMIT_REACHED' | translate }}</span>
                }
              </div>

              @if (catalogError()) {
                <p class="notice" role="alert">{{ 'FOLLOW.CATALOG_ERROR' | translate }}</p>
              }
              @if (addError()) {
                <p class="notice" role="alert">{{ addError()! | translate }}</p>
              }
            </div>
          }

          <h3 class="list-title">{{ 'FOLLOW.MY_TEAMS' | translate }}</h3>
          @if (isPresident() && presidentSearched() && presidentOutsideBaseName() > 0) {
            <p class="hint prune-hint">
              {{ 'FOLLOW.PRUNE_HINT' | translate: { count: presidentOutsideBaseName(), base: presidentBaseName().trim() } }}
            </p>
          }
          @if (followedTeams().length === 0) {
            <p class="empty">{{ 'FOLLOW.EMPTY' | translate }}</p>
          } @else {
            <ul class="followed-list">
              @for (team of followedTeams(); track team.teamId) {
                <li class="followed-item" [class.followed-match]="followedMatchesBaseName(team)">
                  <div class="followed-text">
                    <span class="followed-name">{{ team.name ?? ('FOLLOW.TEAM_UNKNOWN' | translate: { id: team.teamId }) }}</span>
                    @if (!removable(team)) {
                      <span class="lock">
                        <span aria-hidden="true">🔒</span>
                        {{ 'FOLLOW.LOCKED_UNTIL' | translate: { date: formatDate(team.lockedUntil) } }}
                      </span>
                    }
                  </div>
                  <button type="button" class="remove-button"
                    [disabled]="!removable(team) || removingId() === team.teamId"
                    (click)="removeTeam(team.teamId)">
                    {{ (removingId() === team.teamId ? 'FOLLOW.REMOVING' : 'FOLLOW.REMOVE') | translate }}
                  </button>
                </li>
              }
            </ul>
          }
          @if (removeError()) {
            <p class="notice" role="alert">{{ removeError()! | translate }}</p>
          }
        }
      </section>
    </section>
  `,
})
export class ConfigurationComponent implements OnInit {
  readonly theme = inject(ThemeService);
  private readonly api = inject(ApiService);
  private readonly account = inject(AccountService);
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

  /** Role codes of the caller, which drive the association-president block. */
  readonly roleCodes = signal<string[]>([]);
  readonly presidentAssociationId = signal<number | null>(null);
  readonly presidentBaseName = signal('');
  readonly presidentMatches = signal<PresidentTeam[]>([]);
  readonly presidentMatchesLoading = signal(false);
  readonly presidentAdding = signal(false);
  readonly presidentAddedCount = signal(0);
  readonly presidentSearched = signal(false);
  readonly presidentError = signal<string | null>(null);

  readonly followedTeams = computed<FollowedTeam[]>(() => this.subscriptions()?.teams ?? []);
  readonly teamLimit = computed(() => limitOf(this.subscriptions(), 'team'));
  readonly usage = computed(() => formatUsage(this.teamLimit()));
  readonly canFollowMore = computed(() => canFollowMoreTeams(this.subscriptions()));
  /** A caller without the COACH licence (max 0) cannot follow a team at all. */
  readonly hasTeamLicence = computed(() => this.teamLimit()?.max !== 0);
  /** The president block is offered to the PRESIDENT licence only. */
  readonly isPresident = computed(() => isAssociationPresident(this.roleCodes()));
  /**
   * Followed teams the typed base name does not cover: the president sees how
   * many other teams remain and prunes them one by one with the remove button
   * of the followed list below.
   */  readonly presidentOutsideBaseName = computed(() => {
    const baseName = this.presidentBaseName().trim();
    if (baseName.length === 0) return 0;
    return this.followedTeams().filter((team) => !matchesBaseName(team.name, baseName)).length;
  });
  /** The followed team carries the base name being typed. */
  readonly followedMatchesBaseName = (team: { name: string | null }): boolean =>
    matchesBaseName(team.name, this.presidentBaseName().trim());

  /** Base names already followed, offered as suggestions in the input. */
  readonly presidentBaseSuggestions = computed(() =>
    [...new Set(
      this.followedTeams()
        .map((team) => baseNameOf(team.name))
        .filter((name): name is string => name.length > 0),
    )].sort((left, right) => left.localeCompare(right)));

  /** Read-only helpers exposed to the template. */
  readonly removable = (team: { lockedUntil: string | null }) => isRemovable(team);

  ngOnInit(): void {
    this.loadSubscriptions();
    this.api.getSubscriptionAssociations().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: ({ associations }) => {
        this.associations.set(associations);
        // A single association needs no picking: preselect it to save a step.
        if (associations.length === 1) {
          this.selectAssociation(associations[0].id);
          this.selectPresidentAssociation(associations[0].id);
        }
      },
      // The follow list stays usable: only the selector is unavailable.
      error: () => this.catalogError.set(true),
    });
    void this.account.load().then((profile) => {
      this.roleCodes.set(profile?.roles.map((role) => role.code) ?? []);
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

  /** Date of a lock window, in the browser locale. */
  formatDate(value: string | null): string {
    if (!value) return '—';
    const date = new Date(value);
    return Number.isNaN(date.getTime()) ? '—' : date.toLocaleDateString();
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

  // ─── Association president: follow a base name in one call ─────────────

  selectPresidentAssociation(id: number | null): void {
    this.presidentAssociationId.set(id);
    this.presidentMatches.set([]);
    this.presidentSearched.set(false);
    this.presidentAddedCount.set(0);
    this.presidentError.set(null);
  }

  setPresidentBaseName(value: string): void {
    this.presidentBaseName.set(value);
    // The previous preview no longer describes what will be followed.
    this.presidentMatches.set([]);
    this.presidentSearched.set(false);
    this.presidentAddedCount.set(0);
    this.presidentError.set(null);
  }

  canPreviewPresidentTeams(): boolean {
    return this.presidentAssociationId() !== null && this.presidentBaseName().trim().length > 0;
  }

  presidentCanFollow(): boolean {
    return canFollowBaseName(
      this.presidentBaseName(), this.presidentMatches().length, this.presidentAdding());
  }

  presidentTeamFollowed(teamId: number): boolean {
    return alreadyFollowed(this.followedTeams(), teamId);
  }

  /** Lists the teams the base name selects, so the president sees them first. */
  previewPresidentTeams(): void {
    const associationId = this.presidentAssociationId();
    const baseName = this.presidentBaseName().trim();
    if (associationId === null || baseName.length === 0 || this.presidentMatchesLoading()) return;

    this.presidentMatchesLoading.set(true);
    this.presidentError.set(null);
    this.presidentAddedCount.set(0);
    this.api.getPresidentTeams(associationId, baseName).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: ({ teams }) => {
        this.presidentMatches.set(teams);
        this.presidentSearched.set(true);
        this.presidentMatchesLoading.set(false);
      },
      error: () => {
        this.presidentMatchesLoading.set(false);
        this.presidentSearched.set(true);
        this.presidentError.set('FOLLOW.PRESIDENT_ERROR');
      },
    });
  }

  /** Follows every matched team, then refreshes the list. */
  followPresidentTeams(): void {
    const associationId = this.presidentAssociationId();
    const baseName = this.presidentBaseName().trim();
    if (associationId === null || !this.presidentCanFollow()) return;

    this.presidentAdding.set(true);
    this.presidentError.set(null);
    this.api.addPresidentTeamSubscriptions(associationId, baseName)
      .pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
        next: (result) => {
          this.presidentAdding.set(false);
          this.presidentAddedCount.set(result.added.length);
          if (result.added.length === 0) this.presidentError.set('FOLLOW.PRESIDENT_NOTHING_ADDED');
          this.loadSubscriptions();
        },
        error: (error: { status?: number }) => {
          this.presidentAdding.set(false);
          this.presidentError.set(
            error.status === 404 ? 'FOLLOW.PRESIDENT_NO_MATCH'
              : error.status === 403 ? 'FOLLOW.PRESIDENT_FORBIDDEN'
                : 'FOLLOW.PRESIDENT_ERROR');
        },
      });
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
        this.removeError.set(error.status === 423 ? 'FOLLOW.ERROR_LOCKED' : 'FOLLOW.ERROR_REMOVE');
        if (error.status === 423) this.loadSubscriptions();
      },
    });
  }

  private addErrorKey(status?: number): string {
    if (status === 409) return 'FOLLOW.ERROR_LIMIT';
    if (status === 404) return 'FOLLOW.ERROR_UNKNOWN_TEAM';
    return 'FOLLOW.ERROR_ADD';
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
