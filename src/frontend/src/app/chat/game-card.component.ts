import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import type { AngularToolCall } from '@copilotkit/angular';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { ApiService } from '../core/api.service';
import { gameDetailResponseSchema, gameMoments, periodGoalsForTeam } from './game-detail-data';
import type { GameDetailResponse, GameMoment } from './game-detail-data';

/** A team on the game scoreboard. */
export interface GameTeam {
  id?: number;
  teamId?: number;
  name: string;
  logoUrl?: string | null;
}

interface PeriodSection {
  period: number;
  moments: GameMoment[];
}

/** A player mentioned by a game event (scorer, assist or penalized player). */
export interface GameParticipant {
  name: string;
  number?: number;
}

/** One goal, as returned by GetGameDetail. */
export interface GameGoal {
  teamId: number;
  period: number;
  time: string;
  scorer: GameParticipant;
  assists?: GameParticipant[];
  isPowerPlay?: boolean;
  isShorthanded?: boolean;
  isEmptyNet?: boolean;
  isPenaltyShot?: boolean;
}

/** One penalty, as returned by GetGameDetail. */
export interface GamePenalty {
  teamId: number;
  period: number;
  time: string;
  player?: GameParticipant;
  infraction: string;
  duration?: string;
  isBenchPenalty?: boolean;
}

/** Arguments the agent passes to the show_game_detail tool. */
export type GameDetailArgs = {
  gameId: number;
};

/** Server-computed goal count for one team in one period. */
export interface GamePeriodGoals {
  teamId: number;
  period: number;
  goals: number;
}

/** How a close game was decided: in regulation, overtime or a shootout. */
export interface GameTeamStats {
  teamId: number;
  goalsFor: number;
  penaltyMinutes?: number;
  powerPlay?: string;
  penaltyKill?: string;
  /** "regulation", "overtime" or "shootout" when the box score includes it. */
  winLossType?: string;
}

/** Regulation game ends after period 3. */
const REGULATION_PERIODS = 3;

@Component({
  selector: 'app-game-card',
  standalone: true,
  imports: [TranslatePipe],
  template: `
    @if (loading()) {
      <p class="game-card-message" role="status">{{ 'GAMECARD.LOADING' | translate }}</p>
    } @else if (error()) {
      <div class="game-card-message" role="alert">
        {{ error()! | translate }}
        <button type="button" (click)="retry()">{{ 'GAMECARD.RETRY' | translate }}</button>
      </div>
    } @else if (gameDetail()) {
    <article class="game-card" [class.live]="detail().isLive">
      <header class="game-card-header">
        <div class="game-card-meta">
          @if (detail().division) {
            <span class="meta-chip">{{ detail().division }}</span>
          }
          @if (detail().date) {
            <span class="meta-text">{{ dateLabel() }}</span>
          }
          @if (detail().time) {
            <span class="meta-text">{{ detail().time }}</span>
          }
          @if (detail().isLive) {
            <span class="live-chip"><span class="live-dot"></span>{{ 'GAMECARD.LIVE' | translate }}</span>
          }
        </div>
      </header>

      <div class="game-scoreboard" role="table" [attr.aria-label]="'GAMECARD.SCORE_LABEL' | translate">
        <div class="score-row score-teams" role="row">
          <div class="score-team home" role="cell">
            @if (homeLogo()) {
              <img class="team-logo" [src]="homeLogo()" alt="" loading="lazy" />
            } @else {
              <span class="team-logo logo-fallback" aria-hidden="true">🏒</span>
            }
            <span class="team-name">{{ homeName() }}</span>
          </div>
          <div class="score-versus" role="cell">
            <span class="score-main">{{ homeScore() }} - {{ awayScore() }}</span>
            @if (statusLabel()) {
              <span class="score-status">{{ statusLabel() }}</span>
            }
          </div>
          <div class="score-team away" role="cell">
            @if (awayLogo()) {
              <img class="team-logo" [src]="awayLogo()" alt="" loading="lazy" />
            } @else {
              <span class="team-logo logo-fallback" aria-hidden="true">🏒</span>
            }
            <span class="team-name">{{ awayName() }}</span>
          </div>
        </div>

        <div class="periods-table" role="table" [attr.aria-label]="'GAMECARD.PERIOD_TABLE_LABEL' | translate"
          [style.--gc-periods]="periodLabels().length">
          <div class="periods-row periods-head" role="row">
            <span class="periods-lead" role="columnheader"></span>
            @for (label of periodLabels(); track $index) {
              <span class="periods-cell head" role="columnheader">{{ label }}</span>
            }
            <span class="periods-cell total" role="columnheader">{{ 'GAMECARD.FINAL' | translate }}</span>
          </div>
          <div class="periods-row" role="row">
            <span class="periods-lead" role="rowheader">{{ homeName() }}</span>
            @for (value of homePeriods(); track $index) {
              <span class="periods-cell" role="cell">{{ value }}</span>
            }
            <span class="periods-cell total" role="cell">{{ homeScore() }}</span>
          </div>
          <div class="periods-row" role="row">
            <span class="periods-lead" role="rowheader">{{ awayName() }}</span>
            @for (value of awayPeriods(); track $index) {
              <span class="periods-cell" role="cell">{{ value }}</span>
            }
            <span class="periods-cell total" role="cell">{{ awayScore() }}</span>
          </div>
        </div>
        @if (periodsUnavailable()) {
          <p class="periods-unavailable">{{ 'GAMECARD.PERIODS_UNAVAILABLE' | translate }}</p>
        }
      </div>

      @if (periodSections().length) {
        <section class="game-timeline" [attr.aria-label]="'GAMECARD.CHRONOLOGICAL_SUMMARY' | translate">
          <h3>{{ 'GAMECARD.CHRONOLOGICAL_SUMMARY' | translate }}</h3>
          @for (section of periodSections(); track section.period) {
            <section class="period-timeline" [attr.aria-label]="periodTitle(section.period)">
              <h4>{{ periodTitle(section.period) }}</h4>
              <ol class="timeline-list">
                @for (moment of section.moments; track $index) {
                  <li class="timeline-item" [class.goal-item]="moment.kind === 'goal'" [class.penalty-item]="moment.kind === 'penalty'">
                    <span class="moment-icon" [attr.data-kind]="moment.kind" aria-hidden="true">
                      @if (moment.kind === 'goal') {
                        <svg viewBox="0 0 24 24" fill="none">
                          <ellipse cx="12" cy="7.5" rx="8" ry="3.5" fill="currentColor"/>
                          <path d="M4 7.5v8c0 1.95 3.58 3.5 8 3.5s8-1.55 8-3.5v-8" fill="currentColor"/>
                          <ellipse cx="12" cy="15.5" rx="8" ry="3.5" fill="currentColor"/>
                          <path d="M6.5 7.5c1.25 1 3.25 1.5 5.5 1.5" stroke="white" stroke-opacity=".7" stroke-width="1.2" stroke-linecap="round"/>
                        </svg>
                      } @else {
                        <svg viewBox="0 0 24 24" fill="none"><rect x="5" y="4" width="14" height="16" rx="3" stroke="currentColor" stroke-width="1.6"/><text x="12" y="16.4" text-anchor="middle" font-size="8" font-weight="700" fill="currentColor">{{ penaltyShortLabel(moment) }}</text></svg>
                      }
                    </span>
                    <span class="moment-time">{{ moment.time }}</span>
                    <span class="moment-body">
                      <span class="moment-kind">{{ (moment.kind === 'goal' ? 'GAMECARD.GOAL' : 'GAMECARD.PENALTY') | translate }}</span>
                      <span class="moment-team">{{ teamNameOf(moment.teamId) }}:</span>
                      @if (moment.kind === 'goal' && moment.goal) {
                        <span class="moment-desc">
                          {{ participantLabel(moment.goal.scorer) }}
                          @if (moment.goal.assists.length) {
                            , {{ 'GAMECARD.ASSISTED_BY' | translate }} {{ assistLabel(moment.goal.assists) }}
                          }
                        </span>
                        @if (momentTags(moment).length) {
                          <span class="moment-tags">
                            @for (tag of momentTags(moment); track tag.label) {
                              <em class="tag" [attr.title]="tag.detail">{{ tag.label }}</em>
                            }
                          </span>
                        }
                      } @else if (moment.kind === 'penalty' && moment.penalty) {
                        <span class="moment-desc">
                          @if (penaltySubject(moment.penalty)) {
                            {{ participantLabel(moment.penalty.player) }} {{ 'GAMECARD.INFRACTION_FOR' | translate }}
                          } @else {
                            {{ 'GAMECARD.BENCH_PENALTY_FOR' | translate }}
                          }
                          {{ moment.penalty.infraction }}
                        </span>
                        @if (moment.penalty.duration) {
                          <span class="moment-sub">{{ moment.penalty.duration }}</span>
                        }
                      }
                    </span>
                  </li>
                }
              </ol>
            </section>
          }
        </section>
      }
    </article>
    }
  `,
})
export class GameCardComponent {
  private readonly translate = inject(TranslateService);
  private readonly api = inject(ApiService);
  private readonly retryCount = signal(0);

  /** Tool-call arguments can be partial while the agent is streaming them. */
  readonly toolCall = input.required<AngularToolCall<GameDetailArgs>>();

  private readonly gameId = computed(() => {
    const call = this.toolCall();
    return call.status === 'in-progress' ? undefined : call.args.gameId;
  });
  readonly gameDetail = signal<GameDetailResponse | null>(null);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly detail = computed<Partial<GameDetailResponse>>(() => this.gameDetail() ?? {});
  readonly participantLabel = participantLabel;
  readonly assistLabel = assistLabel;
  readonly penaltySubject = penaltySubject;
  readonly penaltyShortLabel = penaltyShortLabel;

  readonly homeName = computed(() => teamNameOf(this.detail().homeTeam, this.translate.instant('GAMECARD.HOME')));
  readonly awayName = computed(() => teamNameOf(this.detail().awayTeam, this.translate.instant('GAMECARD.AWAY')));
  readonly homeLogo = computed(() => logoOf(this.detail().homeTeam));
  readonly awayLogo = computed(() => logoOf(this.detail().awayTeam));

  readonly homeScore = computed(() => this.statsOf(true)?.goalsFor ?? '—');
  readonly awayScore = computed(() => this.statsOf(false)?.goalsFor ?? '—');

  constructor() {
    effect((onCleanup) => {
      const gameId = this.gameId();
      this.retryCount();
      this.gameDetail.set(null);
      this.error.set(null);
      this.loading.set(true);
      if (gameId === undefined && this.toolCall().status === 'in-progress') return;
      if (!Number.isSafeInteger(gameId) || !gameId || gameId <= 0) {
        console.error('Game detail renderer received an invalid game ID', gameId);
        this.loading.set(false);
        this.error.set('GAMECARD.INVALID_GAME_ID');
        return;
      }

      const subscription = this.api.getGameDetail(gameId).subscribe({
        next: (response) => {
          const parsed = gameDetailResponseSchema.safeParse(response);
          if (!parsed.success || parsed.data.id !== gameId) {
            console.error('Invalid game detail response', parsed.success ? 'Game ID mismatch' : parsed.error);
            this.error.set('GAMECARD.INVALID_RESPONSE');
          } else {
            this.gameDetail.set(parsed.data);
          }
          this.loading.set(false);
        },
        error: (error: unknown) => {
          console.error('Could not load game detail', error);
          this.loading.set(false);
          this.error.set(error instanceof HttpErrorResponse && error.status === 404
            ? 'GAMECARD.NOT_FOUND'
            : 'GAMECARD.ERROR');
        },
      });
      onCleanup(() => subscription.unsubscribe());
    });
  }

  retry(): void {
    this.retryCount.update((count) => count + 1);
  }

  readonly statusLabel = computed(() => {
    const args = this.detail();
    if (args.isLive) return 'LIVE';
    if (args.isApproved === false) return this.translate.instant('GAMECARD.UNOFFICIAL');
    return this.translate.instant('GAMECARD.FINAL');
  });

  readonly dateLabel = computed(() => {
    const raw = this.detail().date;
    if (!raw) return '';
    const parsed = new Date(`${raw}T00:00:00`);
    if (Number.isNaN(parsed.getTime())) return raw;
    return parsed.toLocaleDateString('fr-CA', { day: 'numeric', month: 'long', year: 'numeric' });
  });

  /** Highest period that actually has gameplay, at least the three regulation periods. */
  private readonly periodCount = computed(() => {
    const args = this.detail();
    let max = REGULATION_PERIODS;
    for (const goal of args.goals ?? []) max = Math.max(max, goal.period);
    for (const penalty of args.penalties ?? []) max = Math.max(max, penalty.period);
    for (const periodGoals of args.periodGoals ?? []) max = Math.max(max, periodGoals.period);
    for (const stats of args.teamStats ?? []) {
      if (stats.winLossType === 'overtime' || stats.winLossType === 'shootout') max = Math.max(max, REGULATION_PERIODS + 1);
    }
    return max;
  });

  readonly periodLabels = computed(() => {
    const labels: string[] = [];
    for (let period = 1; period <= this.periodCount(); period++) {
      labels.push(period <= REGULATION_PERIODS ? `P${period}` : 'PROL');
    }
    return labels;
  });

  readonly homePeriods = computed(() => this.periodsFor(true));
  readonly awayPeriods = computed(() => this.periodsFor(false));
  readonly periodsUnavailable = computed(() =>
    this.homePeriods().includes('—') || this.awayPeriods().includes('—'),
  );

  readonly periodSections = computed<PeriodSection[]>(() => {
    const sections = new Map<number, GameMoment[]>();
    for (const moment of this.moments()) {
      const periodMoments = sections.get(moment.period) ?? [];
      periodMoments.push(moment);
      sections.set(moment.period, periodMoments);
    }
    return Array.from(sections, ([period, periodMoments]) => ({ period, moments: periodMoments }));
  });

  /** Goals and penalties merged, ordered by period then clock. */
  readonly moments = computed<GameMoment[]>(() => {
    const detail = this.gameDetail();
    return detail ? gameMoments(detail) : [];
  });

  private statsOf(home: boolean): GameTeamStats | undefined {
    const args = this.detail();
    const stats = args.teamStats ?? [];
    if (!stats.length) return undefined;
    const wanted = home ? resolveTeamId(args.homeTeam) : resolveTeamId(args.awayTeam);
    return stats.find((entry) => entry.teamId === wanted);
  }

  teamNameOf(teamId: number): string {
    const args = this.detail();
    return resolveTeamName(teamId === resolveTeamId(args.homeTeam) ? args.homeTeam : args.awayTeam);
  }

  periodTitle(period: number): string {
    if (period === 1) return this.translate.instant('GAMECARD.PERIOD_1');
    if (period === 2) return this.translate.instant('GAMECARD.PERIOD_2');
    if (period === 3) return this.translate.instant('GAMECARD.PERIOD_3');
    if (period === REGULATION_PERIODS + 1) return this.translate.instant('GAMECARD.OVERTIME');
    return this.translate.instant('GAMECARD.EXTRA_PERIOD', { period: period - REGULATION_PERIODS });
  }

  private periodsFor(home: boolean): (number | string)[] {
    const detail = this.gameDetail();
    if (!detail) return [];
    return periodGoalsForTeam(detail, home ? detail.homeTeam.id : detail.awayTeam.id, this.periodCount());
  }

  momentTags(moment: GameMoment): MomentTag[] {
    const goal = moment.goal;
    if (!goal) return [];
    const tags: MomentTag[] = [];
    if (goal.isPowerPlay) tags.push({ label: this.translate.instant('GAMECARD.POWER_PLAY_SHORT'), detail: this.translate.instant('GAMECARD.POWER_PLAY') });
    if (goal.isShorthanded) tags.push({ label: this.translate.instant('GAMECARD.SHORTHANDED_SHORT'), detail: this.translate.instant('GAMECARD.SHORTHANDED') });
    if (goal.isEmptyNet) tags.push({ label: this.translate.instant('GAMECARD.EMPTY_NET_SHORT'), detail: this.translate.instant('GAMECARD.EMPTY_NET') });
    if (goal.isPenaltyShot) tags.push({ label: this.translate.instant('GAMECARD.PENALTY_SHOT_SHORT'), detail: this.translate.instant('GAMECARD.PENALTY_SHOT') });
    return tags;
  }

}

/** Small badge attached to a goal describing special situations. */
interface MomentTag {
  label: string;
  detail: string;
}

function resolveTeamId(team: GameTeam | string | undefined): number | undefined {
  if (!team) return undefined;
  return typeof team === 'string' ? undefined : team.id ?? team.teamId;
}

function teamNameOf(team: GameTeam | string | undefined, fallback: string): string {
  if (!team) return fallback;
  return typeof team === 'string' ? team : team.name;
}

function logoOf(team: GameTeam | string | undefined): string | null {
  if (!team || typeof team === 'string') return null;
  return team.logoUrl ?? null;
}

function resolveTeamName(team: GameTeam | string | undefined): string {
  return teamNameOf(team, '');
}

/** Participant as "Nathan (11)" or "Georges". */
function participantLabel(participant: GameParticipant | undefined): string {
  if (!participant?.name) return '';
  return participant.number ? `${participant.name} (#${participant.number})` : participant.name;
}

function assistLabel(assists: GameParticipant[]): string {
  return assists.map(participantLabel).filter(Boolean).join(' et ');
}

function penaltySubject(penalty: GamePenalty): string {
  return penalty.isBenchPenalty ? '' : participantLabel(penalty.player);
}

/** Penalty durations rendered inside the icon, minutes when a number is available. */
function penaltyShortLabel(moment: GameMoment): string {
  const raw = moment.penalty?.duration ?? '';
  const minutes = raw.match(/(\d+)/);
  return minutes ? minutes[1] : 'P';
}
