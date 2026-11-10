import { Component, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Subscription } from 'rxjs';
import { ApiService } from '../core/api.service';
import { Game, ReportDivision, Standing, Suspension, WeeklyReport } from './weekly-report.models';

const DIVISION_IDS = [
  194899, 194901, 194906, 194908, 194910, 194930, 194934, 194936, 194940, 194942, 194944, 194931, 194937, 194945,
  194890, 194891, 194892, 194893, 194894, 194895,
] as const;
const TEAM_IDS = [
  182497, 182498, 182222, 182220, 181829, 181826, 181303, 181300, 185754, 185755, 185751, 181088, 181090,
  180492, 180494, 180322, 222322, 221751, 221750, 214757, 222320, 222321, 214756, 180671, 221752, 221749, 186200,
  214187, 181669, 214186, 214188, 214189, 180762, 214191,
] as const;
// AA leagues belong to other Spordle offices, so the default-office league
// lookup cannot name them; fall back to the titles published by the league.
const DIVISION_NAMES: Record<number, string> = {
  194890: 'M11 AA Saison',
  194891: 'M11 BB Saison Section Rives',
  194892: 'M11 BB Saison Section Vallée',
  194893: 'M13 AA Saison',
  194894: 'M15 AA Saison',
  194895: 'M17 AA Saison',
} as const;

@Component({
  selector: 'app-report',
  standalone: true,
  imports: [FormsModule, TranslatePipe],
  styleUrl: './report.component.scss',
  template: `
    <section class="page-container report" aria-labelledby="report-title">
      <div class="report-heading">
        <div>
          <span class="eyebrow">{{ 'REPORT.EYEBROW' | translate }}</span>
          <h1 id="report-title" class="page-title">{{ 'REPORT.TITLE' | translate }}</h1>
          <p class="page-subtitle">{{ 'REPORT.SUBTITLE' | translate }}</p>
        </div>
        <span class="heading-icon" aria-hidden="true">✦</span>
      </div>

      <form class="period-card" (ngSubmit)="load()">
        <div class="period-intro">
          <span class="period-icon" aria-hidden="true">▦</span>
          <div><strong>{{ 'REPORT.PERIOD' | translate }}</strong><small>{{ 'REPORT.PERIOD_HINT' | translate }}</small></div>
        </div>
        <label>{{ 'REPORT.START' | translate }}
          <input type="date" name="startDate" [(ngModel)]="startDate" [max]="endDate" required>
        </label>
        <label>{{ 'REPORT.END' | translate }}
          <input type="date" name="endDate" [(ngModel)]="endDate" [min]="startDate" required>
        </label>
        <button type="submit" [disabled]="loading()">{{ (loading() ? 'REPORT.LOADING' : 'REPORT.SHOW') | translate }} <span aria-hidden="true">→</span></button>
      </form>

      @if (error()) {
        <div class="notice notice-error" role="alert">{{ 'REPORT.ERROR' | translate }}</div>
      }
      @if (loading()) {
        <div class="loading" role="status"><span class="spinner"></span>{{ 'REPORT.LOADING_DETAIL' | translate }}</div>
      } @else if (report(); as result) {
        <div class="results-heading">
          <div><span class="eyebrow">{{ 'REPORT.OVERVIEW' | translate }}</span><h2>{{ 'REPORT.DIVISIONS' | translate }}</h2></div>
          <span class="date-range">{{ formatDate(result.startDate) }} — {{ formatDate(result.endDate) }}</span>
        </div>
        @for (division of result.divisions; track division.divisionId; let index = $index) {
          <article class="division-card">
            <header class="division-header">
              <div class="division-number">{{ (index + 1).toString().padStart(2, '0') }}</div>
              <div><span class="division-label">{{ 'REPORT.DIVISION' | translate }}</span>
                <h3>{{ divisionName(division) }}</h3>
              </div>
              <span class="division-id">#{{ division.divisionId }}</span>
            </header>
            <div class="division-content">
              <section class="report-section">
                <div class="section-heading"><h4>{{ 'REPORT.STANDINGS' | translate }}</h4><span>{{ 'REPORT.SELECTED_TEAMS' | translate }}</span></div>
                @if (!division.standings.available) {
                  <p class="notice">{{ 'REPORT.STANDINGS_UNAVAILABLE' | translate }} @if (division.standings.error) { <span class="notice-detail">{{ division.standings.error }}</span> }</p>
                } @else if (selectedStandings(division, result.teamIds); as standings) {
                  @if (standings.length) {
                    <div class="table-wrap"><table>
                      <thead><tr><th>{{ 'REPORT.RANK' | translate }}</th><th>{{ 'REPORT.TEAM' | translate }}</th><th>{{ 'REPORT.PLAYED' | translate }}</th><th>{{ 'REPORT.RECORD' | translate }}</th><th>{{ 'REPORT.POINTS' | translate }}</th></tr></thead>
                      <tbody>@for (standing of standings; track standing.teamId) {
                        <tr><td class="rank">{{ standing.manualRanking ?? standing.ranking }}</td><td class="team-name">{{ standing.team.name }}</td><td>{{ standing.gamesPlayed }}</td><td>{{ standing.wins }}–{{ standing.losses }}–{{ standing.ties }}</td><td class="points">{{ standing.points }}</td></tr>
                      }</tbody>
                    </table></div>
                  } @else { <p class="empty">{{ 'REPORT.NO_STANDINGS' | translate }}</p> }
                }
              </section>
              <section class="report-section">
                <div class="section-heading"><h4>{{ 'REPORT.GAMES' | translate }}</h4><span>{{ 'REPORT.CHRONOLOGICAL' | translate }}</span></div>
                @if (!division.games.available) {
                  <p class="notice">{{ 'REPORT.GAMES_UNAVAILABLE' | translate }} @if (division.games.error) { <span class="notice-detail">{{ division.games.error }}</span> }</p>
                } @else if (selectedGames(division, result.teamIds); as games) {
                  @if (games.length) {
                    <div class="games-list">@for (game of games; track game.id) {
                      <div class="game-row">
                        <div class="game-date"><strong>{{ formatDate(game.date) }}</strong><span>{{ formatTime(game) }}</span></div>
                        <div class="matchup">
                          <span [class.tracked-win]="trackedTeamResult(game, game.awayTeamId, result.teamIds) === 'win'"
                            [class.tracked-tie]="trackedTeamResult(game, game.awayTeamId, result.teamIds) === 'tie'"
                            [class.tracked-loss]="trackedTeamResult(game, game.awayTeamId, result.teamIds) === 'loss'">{{ game.awayTeam.name }}</span>
                          <span class="score">
                            <span [class.tracked-win]="trackedTeamResult(game, game.awayTeamId, result.teamIds) === 'win'"
                              [class.tracked-tie]="trackedTeamResult(game, game.awayTeamId, result.teamIds) === 'tie'"
                              [class.tracked-loss]="trackedTeamResult(game, game.awayTeamId, result.teamIds) === 'loss'">{{ game.score?.[game.awayTeamId] ?? '–' }}</span>
                            <i>:</i>
                            <span [class.tracked-win]="trackedTeamResult(game, game.homeTeamId, result.teamIds) === 'win'"
                              [class.tracked-tie]="trackedTeamResult(game, game.homeTeamId, result.teamIds) === 'tie'"
                              [class.tracked-loss]="trackedTeamResult(game, game.homeTeamId, result.teamIds) === 'loss'">{{ game.score?.[game.homeTeamId] ?? '–' }}</span>
                          </span>
                          <span [class.tracked-win]="trackedTeamResult(game, game.homeTeamId, result.teamIds) === 'win'"
                            [class.tracked-tie]="trackedTeamResult(game, game.homeTeamId, result.teamIds) === 'tie'"
                            [class.tracked-loss]="trackedTeamResult(game, game.homeTeamId, result.teamIds) === 'loss'">{{ game.homeTeam.name }}</span>
                        </div>
                      </div>
                    }</div>
                  } @else { <p class="empty">{{ 'REPORT.NO_GAMES' | translate }}</p> }
                }
              </section>
              <section class="report-section suspensions">
                <div class="section-heading"><h4>{{ 'REPORT.SUSPENSIONS' | translate }}</h4><span>{{ 'REPORT.TRACKED_TEAMS' | translate }}</span></div>
                @if (!division.suspensions.available) {
                  <p class="notice">{{ 'REPORT.SUSPENSIONS_UNAVAILABLE' | translate }} @if (division.suspensions.error) { <span class="notice-detail">{{ division.suspensions.error }}</span> }</p>
                } @else if (trackedSuspensions(division, result.teamIds); as suspensions) {
                  @if (suspensions.length) {
                    <div class="suspension-list">@for (suspension of suspensions; track suspension.id) {
                      <div class="suspension-row">
                        <span class="suspension-icon" aria-hidden="true">!</span>
                        <div class="suspension-person"><strong>{{ suspension.participant?.fullName ?? ('REPORT.OFFICIAL' | translate) }} @if (suspension.participant) { <span>#{{ suspension.participant.number }}</span> }</strong><small>{{ suspension.team.name }}</small></div>
                        <div class="suspension-meta"><strong>{{ suspension.code }}</strong><small>{{ suspension.status }} · {{ suspension.purgedGames?.length ?? 0 }}/{{ suspension.requiredGames }}</small></div>
                      </div>
                    }</div>
                  } @else { <p class="empty">{{ 'REPORT.NO_SUSPENSIONS' | translate }}</p> }
                }
              </section>
            </div>
          </article>
        }
      }
    </section>
  `,
})
export class ReportComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly translate = inject(TranslateService);
  private request?: Subscription;
  readonly report = signal<WeeklyReport | null>(null);
  readonly loading = signal(false);
  readonly error = signal(false);
  endDate = this.localDate(new Date());
  startDate = this.localDate(new Date(new Date().getFullYear(), new Date().getMonth(), new Date().getDate() - 7));

  ngOnInit(): void { this.load(); }

  load(): void {
    if (!this.startDate || !this.endDate || this.startDate > this.endDate) {
      this.error.set(true);
      return;
    }
    this.request?.unsubscribe();
    this.loading.set(true);
    this.error.set(false);
    this.request = this.api.getWeeklyReport(this.startDate, this.endDate, DIVISION_IDS, TEAM_IDS)
      .pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
        next: report => { this.report.set(report); this.loading.set(false); },
        error: () => { this.error.set(true); this.loading.set(false); },
      });
  }

  selectedStandings(division: ReportDivision, teamIds: number[]): Standing[] {
    return (division.standings.data ?? []).filter(row => teamIds.includes(row.teamId))
      .sort((a, b) => (a.manualRanking ?? a.ranking) - (b.manualRanking ?? b.ranking));
  }

  selectedGames(division: ReportDivision, teamIds: number[]): Game[] {
    return (division.games.data ?? []).filter(game => teamIds.includes(game.homeTeamId) || teamIds.includes(game.awayTeamId))
      .sort((a, b) => Date.parse(a.startTime) - Date.parse(b.startTime));
  }

  trackedSuspensions(division: ReportDivision, teamIds: number[]): Suspension[] {
    return (division.suspensions.data ?? []).filter(suspension => teamIds.includes(suspension.team.id));
  }

  trackedTeamResult(game: Game, teamId: number, trackedTeamIds: number[]): 'win' | 'tie' | 'loss' | null {
    if (!trackedTeamIds.includes(teamId)) return null;
    const score = game.score?.[teamId];
    const opponentId = teamId === game.homeTeamId ? game.awayTeamId : game.homeTeamId;
    const opponentScore = game.score?.[opponentId];
    if (score === undefined || opponentScore === undefined) return null;
    if (score === opponentScore) return 'tie';
    return score > opponentScore ? 'win' : 'loss';
  }

  divisionName(division: ReportDivision): string {
    if (division.leagueName) return division.leagueName;
    if (DIVISION_NAMES[division.divisionId]) return DIVISION_NAMES[division.divisionId];
    const name = division.standings.data?.find(row => row.team?.division)?.team.division;
    return name?.replace(/\bU11\s*([AB])\b/gi, (_, letter: string) => `M11${letter.toUpperCase()}`)
      || `${this.translate.instant('REPORT.DIVISION')} ${division.divisionId}`;
  }

  formatDate(date: string): string {
    const [year, month, day] = date.substring(0, 10).split('-').map(Number);
    return new Intl.DateTimeFormat(this.translate.getCurrentLang() === 'en' ? 'en-CA' : 'fr-CA', { day: 'numeric', month: 'short', year: 'numeric' }).format(new Date(year, month - 1, day));
  }

  formatTime(game: Game): string {
    const date = new Date(game.startTime);
    const options: Intl.DateTimeFormatOptions = { hour: '2-digit', minute: '2-digit', timeZone: game.timezone || 'America/Toronto' };
    try {
      return new Intl.DateTimeFormat(this.translate.getCurrentLang() === 'en' ? 'en-CA' : 'fr-CA', options).format(date);
    } catch (error) {
      if (!(error instanceof RangeError)) throw error;
      return `${game.startTime} (UTC)`;
    }
  }

  private localDate(date: Date): string {
    return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
  }
}
