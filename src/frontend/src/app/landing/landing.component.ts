import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { AuthService as Auth0AuthService } from '@auth0/auth0-angular';
import { ApiService } from '../core/api.service';
import { TeamPageDetail } from '../team/team.models';
import { TeamStatsComponent } from '../team/team-stats.component';
import {
  PreviewAssociation,
  PreviewDivision,
  PreviewLeague,
  PreviewTeam,
} from '../core/api.service';
import {
  FREE_PREVIEW_LIMIT,
  canPreview,
  previewsLeft,
  recordPreview,
} from './preview-trials';

/**
 * Landing page shown to visitors who are not signed in: what the site does, a
 * free preview of one team's statistics, and the reasons to create a free
 * account.
 *
 * The preview walks the reference data — association → division → league →
 * team — through the anonymous `/api/preview/*` routes and renders the result
 * with `app-team-stats`, the very component of the signed-in team page, so the
 * figures are identical. A visitor gets {@link FREE_PREVIEW_LIMIT} previews,
 * counted in `localStorage` (see `preview-trials.ts`); opening a fourth
 * requires the free account.
 *
 * Sign up and sign in both hand over to Auth0 Universal Login and land back on
 * the chat page. The page also advertises the two paid scopes: an association
 * president and a category director each get a weekly recap of every team of
 * their league / category.
 */
@Component({
  selector: 'app-landing',
  standalone: true,
  imports: [FormsModule, TranslatePipe, TeamStatsComponent],
  styleUrl: './landing.component.scss',
  template: `
    <div class="landing">
      <header class="landing-header">
        <a class="landing-brand" href="/" [attr.aria-label]="'APP.TITLE' | translate">
          <img class="brand-mark" src="assets/logo/game-tracker-badge.png" alt="" />
        </a>
        <div class="landing-header-actions">
          <button class="header-button language-button" type="button" (click)="toggleLanguage()"
            [attr.aria-label]="'LANGUAGE.BUTTON' | translate">
            <span aria-hidden="true">◎</span> {{ language() === 'fr' ? 'EN' : 'FR' }}
          </button>
          <button class="signin-button" type="button" [disabled]="redirecting()" (click)="signIn()">
            {{ 'LANDING.SIGN_IN' | translate }}
          </button>
        </div>
      </header>

      <main class="landing-main">
        <section class="hero" aria-labelledby="landing-title">
          <p class="hero-eyebrow">{{ 'LANDING.EYEBROW' | translate }}</p>
          <h1 id="landing-title">{{ 'LANDING.TITLE' | translate }}</h1>
          <p class="hero-subtitle">{{ 'LANDING.SUBTITLE' | translate }}</p>

          <ul class="hero-steps" [attr.aria-label]="'LANDING.STEPS_LABEL' | translate">
            <li><strong>1</strong><span>{{ 'LANDING.STEP_1' | translate }}</span></li>
            <li><strong>2</strong><span>{{ 'LANDING.STEP_2' | translate }}</span></li>
            <li><strong>3</strong><span>{{ 'LANDING.STEP_3' | translate }}</span></li>
          </ul>

          <div class="hero-actions">
            <button class="primary-button signup-button" type="button" [disabled]="redirecting()" (click)="signUp()">
              {{ 'LANDING.SIGN_UP' | translate }}
            </button>
            <button class="secondary-button login-submit" type="button" [disabled]="redirecting()" (click)="signIn()">
              {{ 'LANDING.SIGN_IN' | translate }}
            </button>
          </div>
          @if (error()) {
            <p class="hero-error" role="alert">{{ 'LOGIN.NETWORK' | translate }}</p>
          }
        </section>

        <section class="preview" aria-labelledby="preview-title">
          <header class="section-heading">
            <div>
              <h2 id="preview-title">{{ 'LANDING.PREVIEW_TITLE' | translate }}</h2>
              <p>{{ 'LANDING.PREVIEW_SUBTITLE' | translate: { count: freeLimit } }}</p>
            </div>
            <span class="trial-badge" [class.trial-badge-empty]="left() === 0" role="status">
              {{ (left() > 0 ? 'LANDING.TRIALS_LEFT' : 'LANDING.TRIALS_NONE') | translate: { count: left() } }}
            </span>
          </header>

          @if (loadingAssociations()) {
            <p class="state" role="status">{{ 'LANDING.LOADING' | translate }}</p>
          } @else if (associations().length === 0) {
            <p class="state">{{ 'LANDING.NO_DATA' | translate }}</p>
          } @else {
            <div class="selector" [attr.aria-label]="'LANDING.PREVIEW_TITLE' | translate">
              <label class="field">
                <span>{{ 'LANDING.ASSOCIATION' | translate }}</span>
                <select [ngModel]="associationId()" (ngModelChange)="selectAssociation($event)"
                  [disabled]="catalogLoading()">
                  <option [ngValue]="null">{{ 'LANDING.SELECT' | translate }}</option>
                  @for (association of associations(); track association.id) {
                    <option [ngValue]="association.id">{{ association.name }}</option>
                  }
                </select>
              </label>
              <label class="field">
                <span>{{ 'LANDING.DIVISION' | translate }}</span>
                <select [ngModel]="divisionId()" (ngModelChange)="selectDivision($event)"
                  [disabled]="!divisionsEnabled() || catalogLoading()">
                  <option [ngValue]="null">{{ 'LANDING.SELECT' | translate }}</option>
                  @for (division of divisions(); track division.id) {
                    <option [ngValue]="division.id">{{ division.name }}</option>
                  }
                </select>
              </label>
              <label class="field">
                <span>{{ 'LANDING.LEAGUE' | translate }}</span>
                <select [ngModel]="leagueId()" (ngModelChange)="selectLeague($event)"
                  [disabled]="!leaguesEnabled() || catalogLoading()">
                  <option [ngValue]="null">{{ 'LANDING.SELECT' | translate }}</option>
                  @for (league of leagues(); track league.id) {
                    <option [ngValue]="league.id">{{ league.name }}</option>
                  }
                </select>
              </label>
              <label class="field">
                <span>{{ 'LANDING.TEAM' | translate }}</span>
                <select [ngModel]="teamId()" (ngModelChange)="selectTeam($event)"
                  [disabled]="!teamsEnabled() || catalogLoading()">
                  <option [ngValue]="null">{{ 'LANDING.SELECT' | translate }}</option>
                  @for (team of teams(); track team.id) {
                    <option [ngValue]="team.id">{{ team.name }}</option>
                  }
                </select>
              </label>
            </div>

            <div class="preview-actions">
              @if (left() > 0) {
                <button class="primary-button preview-button" type="button"
                  [disabled]="teamId() === null || catalogLoading() || detailLoading()" (click)="showStatistics()">
                  {{ (detailLoading() ? 'LANDING.LOADING_TEAM' : 'LANDING.SHOW_STATS') | translate }}
                </button>
              }
              @if (catalogError()) { <span class="hint hint-error" role="alert">{{ 'LANDING.CATALOG_ERROR' | translate }}</span> }
            </div>

            @if (left() === 0) {
              <div class="limit-card" role="status">
                <strong>{{ 'LANDING.LIMIT_TITLE' | translate }}</strong>
                <p>{{ 'LANDING.LIMIT_BODY' | translate: { count: freeLimit } }}</p>
                <button class="primary-button signup-button" type="button" [disabled]="redirecting()" (click)="signUp()">
                  {{ 'LANDING.CREATE_FREE_ACCOUNT' | translate }}
                </button>
              </div>
            }

            @if (detailError()) {
              <p class="state state-error" role="alert">
                {{ 'TEAM.ERROR' | translate }}
                <button type="button" class="link-button" (click)="retry()">{{ 'TEAM.RETRY' | translate }}</button>
              </p>
            }

            @if (detail(); as data) {
              <app-team-stats [team]="data" [guest]="true" />
              <p class="preview-note">{{ 'LANDING.PREVIEW_NOTE' | translate }}</p>
            }
          }
        </section>

        <section class="benefits" aria-labelledby="benefits-title">
          <h2 id="benefits-title">{{ 'LANDING.BENEFITS_TITLE' | translate }}</h2>
          <div class="benefit-grid">
            <article class="benefit-card">
              <span class="benefit-icon" aria-hidden="true">★</span>
              <h3>{{ 'LANDING.BENEFIT_ACCOUNT_TITLE' | translate }}</h3>
              <p>{{ 'LANDING.BENEFIT_ACCOUNT_BODY' | translate }}</p>
            </article>
            <article class="benefit-card">
              <span class="benefit-icon" aria-hidden="true">▤</span>
              <h3>{{ 'LANDING.BENEFIT_PRESIDENT_TITLE' | translate }}</h3>
              <p>{{ 'LANDING.BENEFIT_PRESIDENT_BODY' | translate }}</p>
            </article>
            <article class="benefit-card">
              <span class="benefit-icon" aria-hidden="true">◆</span>
              <h3>{{ 'LANDING.BENEFIT_DIRECTOR_TITLE' | translate }}</h3>
              <p>{{ 'LANDING.BENEFIT_DIRECTOR_BODY' | translate }}</p>
            </article>
          </div>
          <div class="benefits-actions">
            <button class="primary-button signup-button" type="button" [disabled]="redirecting()" (click)="signUp()">
              {{ 'LANDING.CREATE_FREE_ACCOUNT' | translate }}
            </button>
            <span class="benefits-hint">{{ 'LANDING.FREE_HINT' | translate }}</span>
          </div>
        </section>
      </main>
    </div>
  `,
})
export class LandingComponent {
  private readonly api = inject(ApiService);
  private readonly translate = inject(TranslateService);
  private readonly auth = inject(Auth0AuthService);
  private readonly destroyRef = inject(DestroyRef);

  readonly freeLimit = FREE_PREVIEW_LIMIT;

  readonly associations = signal<PreviewAssociation[]>([]);
  readonly divisions = signal<PreviewDivision[]>([]);
  readonly leagues = signal<PreviewLeague[]>([]);
  readonly teams = signal<PreviewTeam[]>([]);
  readonly associationId = signal<number | null>(null);
  readonly divisionId = signal<number | null>(null);
  readonly leagueId = signal<string | null>(null);
  readonly teamId = signal<number | null>(null);

  readonly loadingAssociations = signal(true);
  readonly catalogLoading = signal(false);
  readonly catalogError = signal(false);

  readonly detail = signal<TeamPageDetail | null>(null);
  readonly detailLoading = signal(false);
  readonly detailError = signal(false);
  /** Database id of the team currently shown: re-viewing it costs no attempt. */
  private shownTeamId = 0;

  /** Free previews left on this device; refreshed after each preview. */
  readonly left = signal(previewsLeft(this.storage()));
  readonly redirecting = signal(false);
  readonly error = signal(false);
  readonly language = computed<'fr' | 'en'>(() => this.translate.currentLang() === 'en' ? 'en' : 'fr');

  constructor() {
    this.loadAssociations();
  }

  divisionsEnabled(): boolean { return this.associationId() !== null; }
  leaguesEnabled(): boolean { return this.divisionId() !== null; }
  teamsEnabled(): boolean { return this.leagueId() !== null; }

  selectAssociation(id: number | null): void {
    this.associationId.set(id);
    this.divisionId.set(null);
    this.leagueId.set(null);
    this.teamId.set(null);
    this.divisions.set([]);
    this.leagues.set([]);
    this.teams.set([]);
    this.clearPreview();
    if (id !== null) this.loadDivisions(id);
  }

  selectDivision(id: number | null): void {
    this.divisionId.set(id);
    this.leagueId.set(null);
    this.teamId.set(null);
    this.leagues.set([]);
    this.teams.set([]);
    this.clearPreview();
    if (id !== null) this.loadLeagues(id);
  }

  selectLeague(id: string | null): void {
    this.leagueId.set(id);
    this.teamId.set(null);
    this.teams.set([]);
    this.clearPreview();
    if (id !== null) this.loadTeams(id);
  }

  /**
   * Picks a team. Changing the selection drops the current preview so the
   * figures on screen always belong to the team picked above them (the attempt
   * already paid is not refunded; re-viewing the same team stays free).
   */
  selectTeam(id: number | null): void {
    this.teamId.set(id);
    this.clearPreview();
  }

  private clearPreview(): void {
    this.detail.set(null);
    this.detailError.set(false);
    this.shownTeamId = 0;
  }

  /**
   * Shows the selected team's statistics. A team already on screen costs no
   * attempt; otherwise one is recorded once the detail is actually read, so a
   * failed load never consumes one. Without attempts left the preview button is
   * gone: the page asks for the free account instead.
   */
  showStatistics(): void {
    const teamId = this.teamId();
    if (teamId === null || this.detailLoading()) return;

    if (teamId === this.shownTeamId && this.detail()) return;
    if (!canPreview(this.storage())) {
      this.detail.set(null);
      this.shownTeamId = 0;
      this.left.set(0);
      return;
    }

    this.loadDetail(teamId);
  }

  retry(): void {
    const teamId = this.teamId() ?? this.shownTeamId;
    if (teamId) this.loadDetail(teamId);
  }

  signIn(): void {
    this.redirect();
  }

  /** Auth0 signup screen; the free account is created on the provider side. */
  signUp(): void {
    this.redirect({ screen_hint: 'signup' });
  }

  toggleLanguage(): void {
    const next: 'fr' | 'en' = this.language() === 'fr' ? 'en' : 'fr';
    document.documentElement.lang = next;
    this.translate.use(next);
  }

  private redirect(authorizationParams?: { screen_hint: 'signup' }): void {
    if (this.redirecting()) return;
    this.redirecting.set(true);
    this.error.set(false);
    this.auth.loginWithRedirect({
      appState: { target: '/chat' },
      ...(authorizationParams ? { authorizationParams } : {}),
    }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      error: (reason: unknown) => {
        console.error('Auth0 redirect failed', reason);
        this.redirecting.set(false);
        this.error.set(true);
      },
    });
  }

  private loadAssociations(): void {
    this.loadingAssociations.set(true);
    this.api.getPreviewAssociations().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: ({ associations }) => {
        this.associations.set(associations);
        this.loadingAssociations.set(false);
        // A single association needs no picking: preselect it to save a step.
        if (associations.length === 1) this.selectAssociation(associations[0].id);
      },
      error: (reason: unknown) => {
        console.error('Unable to load the preview associations', reason);
        this.loadingAssociations.set(false);
        this.catalogError.set(true);
      },
    });
  }

  private loadDivisions(associationId: number): void {
    this.catalogLoading.set(true);
    this.catalogError.set(false);
    this.api.getPreviewDivisions(associationId).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: ({ divisions }) => {
        this.divisions.set(divisions);
        this.catalogLoading.set(false);
        if (divisions.length === 1) this.selectDivision(divisions[0].id);
      },
      error: () => { this.catalogLoading.set(false); this.catalogError.set(true); },
    });
  }

  private loadLeagues(divisionId: number): void {
    this.catalogLoading.set(true);
    this.catalogError.set(false);
    this.api.getPreviewLeagues(divisionId).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: ({ leagues }) => {
        this.leagues.set(leagues);
        this.catalogLoading.set(false);
        if (leagues.length === 1) this.selectLeague(leagues[0].id);
      },
      error: () => { this.catalogLoading.set(false); this.catalogError.set(true); },
    });
  }

  private loadTeams(leagueId: string): void {
    this.catalogLoading.set(true);
    this.catalogError.set(false);
    // A category guid is shared by every division that plays it: the division
    // the visitor walked through pins the roster.
    this.api.getPreviewTeams(leagueId, this.divisionId() ?? undefined)
      .pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
        next: ({ teams }) => { this.teams.set(teams); this.catalogLoading.set(false); },
        error: () => { this.catalogLoading.set(false); this.catalogError.set(true); },
      });
  }

  private loadDetail(teamId: number): void {
    this.detailLoading.set(true);
    this.detailError.set(false);
    this.api.getPreviewTeamDetail(teamId).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (detail) => {
        // One attempt per new team, recorded only once the figures are in.
        recordPreview(this.storage());
        this.left.set(previewsLeft(this.storage()));
        this.detail.set(detail);
        this.shownTeamId = teamId;
        this.detailLoading.set(false);
      },
      error: (reason: unknown) => {
        console.error('Unable to load the previewed team', reason);
        this.detail.set(null);
        this.shownTeamId = 0;
        this.detailLoading.set(false);
        this.detailError.set(true);
      },
    });
  }

  private storage(): Storage | null {
    try {
      return typeof localStorage === 'undefined' ? null : localStorage;
    } catch {
      return null;
    }
  }
}
