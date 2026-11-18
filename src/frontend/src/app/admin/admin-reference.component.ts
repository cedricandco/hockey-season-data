import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { ApiService } from '../core/api.service';
import { ReferenceImportResult, ReferenceOrganization, ReferenceSeason } from './reference.models';
import { canImport, defaultOrganization, defaultSeasonId, parseOfficeNumber } from './reference-helpers';

/**
 * Administrators only: define the divisions, leagues and teams of an
 * association. The association is picked in the `organizations` table, the
 * division number is typed (it is `divisions.external_id`) and the season comes
 * from a list; one call then imports everything from the public league API —
 * the division, the leagues (Spordle categories, stored under their French
 * name) and their teams. Re-running is safe: existing rows are updated, new
 * teams are added and the ones that disappeared are deleted (with their user
 * subscriptions).
 *
 * The backend re-checks the administrator flag on every call; this page is only
 * the UI, and `/admin/*` is guarded by `adminGuard`.
 */
@Component({
  selector: 'app-admin-reference',
  standalone: true,
  imports: [FormsModule, TranslatePipe],
  styleUrl: './admin-reference.component.scss',
  template: `
    <section class="page-container reference" aria-labelledby="reference-title">
      <h1 id="reference-title" class="page-title">{{ 'REFERENCE.TITLE' | translate }}</h1>
      <p class="page-subtitle">{{ 'REFERENCE.SUBTITLE' | translate }}</p>

      @if (loading()) {
        <div class="reference-state" role="status">
          <span class="spinner" aria-hidden="true"></span> {{ 'REFERENCE.LOADING' | translate }}
        </div>
      } @else {
        @if (loadError()) {
          <div class="notice" role="alert">
            {{ 'REFERENCE.LOAD_ERROR' | translate }}
            <button type="button" class="link" (click)="load()">{{ 'REFERENCE.RETRY' | translate }}</button>
          </div>
        }
        <form class="reference-card" (ngSubmit)="submit()">
          <div class="field">
            <label for="reference-association">{{ 'REFERENCE.ASSOCIATION' | translate }}</label>
            <select id="reference-association" name="association"
              [ngModel]="organization()?.id ?? null"
              (ngModelChange)="selectOrganization($event)">
              @if (organizations().length === 0) {
                <option [ngValue]="null">{{ 'REFERENCE.NO_ASSOCIATION' | translate }}</option>
              }
              @for (item of organizations(); track item.id) {
                <option [ngValue]="item.id">{{ item.name }}</option>
              }
            </select>
            <small>{{ 'REFERENCE.ASSOCIATION_HINT' | translate }}</small>
          </div>

          <div class="field">
            <label for="reference-division">{{ 'REFERENCE.DIVISION' | translate }}</label>
            <input id="reference-division" name="division" type="text" inputmode="numeric"
              autocomplete="off" pattern="[0-9]*" maxlength="9"
              [ngModel]="divisionInput()" (ngModelChange)="onDivisionInput($event)"
              [placeholder]="'REFERENCE.DIVISION_PLACEHOLDER' | translate"
              [attr.aria-invalid]="divisionInput() !== '' && divisionNumber() === 0"
              aria-describedby="reference-division-hint" />
            <small id="reference-division-hint">{{ 'REFERENCE.DIVISION_HINT' | translate }}</small>
          </div>

          <div class="field">
            <label for="reference-season">{{ 'REFERENCE.SEASON' | translate }}</label>
            <select id="reference-season" name="season"
              [ngModel]="seasonId()" (ngModelChange)="seasonId.set($event)" [disabled]="seasons().length === 0">
              @if (seasons().length === 0) {
                <option value="">{{ 'REFERENCE.NO_SEASON' | translate }}</option>
              }
              @for (season of seasons(); track season.externalId) {
                <option [value]="season.externalId">{{ season.name }}</option>
              }
            </select>
            <small>{{ 'REFERENCE.SEASON_HINT' | translate }}</small>
          </div>

          <button type="submit" class="submit" [disabled]="!canSubmit() || importing()">
            {{ (importing() ? 'REFERENCE.IMPORTING' : 'REFERENCE.IMPORT') | translate }}
          </button>
        </form>

        @if (importError()) {
          <div class="notice" role="alert">{{ importError()! | translate }}</div>
        }

        @if (result(); as outcome) {
          <article class="result-card" aria-labelledby="reference-result-title">
            <header>
              <h2 id="reference-result-title">{{ 'REFERENCE.RESULT' | translate }}</h2>
              <p>{{ outcome.organizationName }} · {{ outcome.officeName }} · {{ outcome.seasonId }}</p>
            </header>

            @if (outcome.warnings.length > 0) {
              <ul class="warnings" role="status">
                @for (warning of outcome.warnings; track warning) { <li>{{ warning }}</li> }
              </ul>
            }

            <div class="result-grid">
              <section>
                <h3>{{ 'REFERENCE.LEAGUES' | translate }}</h3>
                <dl>
                  <dt>{{ 'REFERENCE.CREATED' | translate }}</dt>
                  <dd>{{ outcome.leagues.created.length }}</dd>
                  <dt>{{ 'REFERENCE.UPDATED' | translate }}</dt>
                  <dd>{{ outcome.leagues.updated.length }}</dd>
                  <dt>{{ 'REFERENCE.DELETED' | translate }}</dt>
                  <dd>{{ outcome.leagues.deleted.length }}</dd>
                </dl>
                @if (outcome.leagues.created.length + outcome.leagues.updated.length + outcome.leagues.deleted.length > 0) {
                  <ul class="names">
                    @for (name of outcome.leagues.created; track name) {
                      <li><span class="tag tag-created">+</span> {{ name }}</li>
                    }
                    @for (name of outcome.leagues.updated; track name) {
                      <li><span class="tag tag-updated">~</span> {{ name }}</li>
                    }
                    @for (name of outcome.leagues.deleted; track name) {
                      <li><span class="tag tag-deleted">−</span> {{ name }}</li>
                    }
                  </ul>
                } @else {
                  <p class="empty">{{ 'REFERENCE.NO_LEAGUE_CHANGE' | translate }}</p>
                }
              </section>

              <section>
                <h3>{{ 'REFERENCE.TEAMS' | translate }}</h3>
                <dl>
                  <dt>{{ 'REFERENCE.CREATED' | translate }}</dt>
                  <dd>{{ outcome.teams.created }}</dd>
                  <dt>{{ 'REFERENCE.UPDATED' | translate }}</dt>
                  <dd>{{ outcome.teams.updated }}</dd>
                  <dt>{{ 'REFERENCE.DELETED' | translate }}</dt>
                  <dd>{{ outcome.teams.deleted }}</dd>
                </dl>
              </section>
            </div>
          </article>
        }
      }
    </section>
  `,
})
export class AdminReferenceComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly destroyRef = inject(DestroyRef);

  readonly organizations = signal<ReferenceOrganization[]>([]);
  readonly seasons = signal<ReferenceSeason[]>([]);
  readonly organization = signal<ReferenceOrganization | null>(null);
  readonly divisionInput = signal('');
  readonly divisionNumber = signal(0);
  readonly seasonId = signal('');
  readonly loading = signal(true);
  readonly loadError = signal(false);
  readonly importing = signal(false);
  readonly importError = signal<string | null>(null);
  readonly result = signal<ReferenceImportResult | null>(null);

  /** Division whose seasons are currently listed, to avoid redundant calls. */
  private loadedDivision = '';

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.loadError.set(false);
    this.api.getReferenceOrganizations().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: ({ organizations }) => {
        this.organizations.set(organizations);
        this.loading.set(false);
        // The association list is the organizations table itself: nothing is
        // preselected when it is empty.
        const organization = defaultOrganization(organizations);
        this.organization.set(organization);
        const division = this.divisionInput().trim();
        if (division) this.loadSeasons(division);
        else if (organization) this.seasonId.set('');
      },
      error: () => { this.loading.set(false); this.loadError.set(true); },
    });
  }

  selectOrganization(id: number | null): void {
    this.organization.set(this.organizations().find((item) => item.id === id) ?? null);
    this.result.set(null);
    const division = this.divisionInput().trim();
    if (division) this.loadSeasons(division);
  }

  onDivisionInput(value: string): void {
    this.divisionInput.set(value);
    const number = parseOfficeNumber(value);
    this.divisionNumber.set(number);
    if (number > 0 && value.trim() !== this.loadedDivision) this.loadSeasons(value.trim());
    else if (number === 0) { this.seasons.set([]); this.seasonId.set(''); this.loadedDivision = ''; }
  }

  canSubmit(): boolean {
    return canImport(this.organization(), this.divisionNumber(), this.seasonId());
  }

  submit(): void {
    const organization = this.organization();
    const divisionId = this.divisionInput().trim();
    if (!organization || !this.canSubmit() || this.importing()) return;

    this.importing.set(true);
    this.importError.set(null);
    this.result.set(null);
    this.api.importReferenceData({ organizationId: organization.id, divisionId, seasonId: this.seasonId() })
      .pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
        next: (outcome) => { this.result.set(outcome); this.importing.set(false); },
        error: (error: { status?: number }) => {
          this.importing.set(false);
          this.importError.set(error.status === 403 ? 'REFERENCE.ERROR_FORBIDDEN'
            : error.status === 400 ? 'REFERENCE.ERROR_INVALID'
            : error.status === 502 ? 'REFERENCE.ERROR_SPORDLE'
            : 'REFERENCE.ERROR');
        },
      });
  }

  /** Lists the seasons of a division; the import itself reports Spordle failures. */
  private loadSeasons(divisionId: string): void {
    this.loadedDivision = divisionId;
    this.seasons.set([]);
    this.seasonId.set('');
    this.api.getReferenceSeasons(divisionId).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: ({ seasons, defaultSeasonId: fallback }) => {
        // A later keystroke may have moved to another division: ignore a stale answer.
        if (this.loadedDivision !== divisionId) return;
        this.seasons.set(seasons);
        this.seasonId.set(defaultSeasonId(seasons, fallback));
      },
      error: () => {
        if (this.loadedDivision !== divisionId) return;
        // The page stays usable: submitting still surfaces the error precisely.
        this.seasons.set([]);
        this.seasonId.set('');
      },
    });
  }
}
