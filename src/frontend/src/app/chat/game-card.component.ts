import { Component, computed, inject, input } from '@angular/core';
import type { AngularToolCall } from '@copilotkit/angular';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';

/** A team on the game scoreboard. */
export interface GameTeam {
  id?: number;
  teamId?: number;
  name: string;
  logoUrl?: string | null;
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
  gameId?: number;
  gameNumber?: string;
  date?: string;
  time?: string;
  division?: string;
  isLive?: boolean;
  isApproved?: boolean;
  homeTeam: GameTeam | string;
  awayTeam: GameTeam | string;
  homeScore?: number;
  awayScore?: number;
  teamStats?: GameTeamStats[];
  goals?: GameGoal[];
  penalties?: GamePenalty[];
};

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

/** A goal or penalty merged into one chronological key moment. */
interface KeyMoment {
  kind: 'goal' | 'penalty';
  period: number;
  /** Seconds elapsed in the period, for ordering. */
  seconds: number;
  time: string;
  teamId: number;
  goal?: GameGoal;
  penalty?: GamePenalty;
}

/** Regulation game ends after period 3. */
const REGULATION_PERIODS = 3;

@Component({
  selector: 'app-game-card',
  standalone: true,
  imports: [TranslatePipe],
  template: `
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

      <div class="game-scoreboard" role="table" aria-label="Score final">
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

        <div class="periods-table" role="table" aria-label="Points par période">
          <div class="periods-row periods-head" role="row">
            <span class="periods-lead" role="columnheader"></span>
            @for (label of periodLabels(); track $index) {
              <span class="periods-cell head" role="columnheader">{{ label }}</span>
            }
            <span class="periods-cell total" role="columnheader">{{ 'GAMECARD.TOTAL' | translate }}</span>
          </div>
          <div class="periods-row" role="row">
            <span class="periods-lead" role="rowheader">{{ awayName() }}</span>
            @for (value of awayPeriods(); track $index) {
              <span class="periods-cell" role="cell">{{ value }}</span>
            }
            <span class="periods-cell total" role="cell">{{ awayScore() }}</span>
          </div>
          <div class="periods-row" role="row">
            <span class="periods-lead" role="rowheader">{{ homeName() }}</span>
            @for (value of homePeriods(); track $index) {
              <span class="periods-cell" role="cell">{{ value }}</span>
            }
            <span class="periods-cell total" role="cell">{{ homeScore() }}</span>
          </div>
        </div>
      </div>

      @if (moments().length) {
        <section class="game-timeline" aria-label="Moments clés">
          <h3>{{ 'GAMECARD.MOMENTS' | translate }}</h3>
          <ol class="timeline-list">
            @for (moment of moments(); track $index) {
              <li class="timeline-item" [class.goal-item]="moment.kind === 'goal'" [class.penalty-item]="moment.kind === 'penalty'">
                <span class="moment-icon" [attr.data-kind]="moment.kind" aria-hidden="true">
                  @if (moment.kind === 'goal') {
                    <svg viewBox="0 0 24 24" fill="none"><circle cx="12" cy="12" r="9" stroke="currentColor" stroke-width="1.6"/><circle cx="12" cy="12" r="3.2" fill="currentColor"/></svg>
                  } @else {
                    <svg viewBox="0 0 24 24" fill="none"><rect x="5" y="4" width="14" height="16" rx="3" stroke="currentColor" stroke-width="1.6"/><text x="12" y="16.4" text-anchor="middle" font-size="8" font-weight="700" fill="currentColor">{{ penaltyShortLabel(moment) }}</text></svg>
                  }
                </span>
                <span class="moment-time">{{ momentPeriodLabel(moment) }} {{ moment.time }}</span>
                <span class="moment-body">
                  @if (moment.kind === 'goal' && moment.goal) {
                    <span class="moment-team">{{ teamNameOf(moment.teamId) }}</span>
                    <span class="moment-desc">{{ 'GAMECARD.GOAL_BY' | translate }} {{ participantLabel(moment.goal.scorer) }}</span>
                    @if (moment.goal.assists?.length) {
                      <span class="moment-sub">{{ 'GAMECARD.ASSISTED_BY' | translate }} {{ assistLabel(moment.goal.assists!) }}</span>
                    }
                    @if (momentTags(moment).length) {
                      <span class="moment-tags">
                        @for (tag of momentTags(moment); track tag.label) {
                          <em class="tag" [attr.title]="tag.detail">{{ tag.label }}</em>
                        }
                      </span>
                    }
                  } @else if (moment.kind === 'penalty' && moment.penalty) {
                    <span class="moment-team">{{ teamNameOf(moment.teamId) }}</span>
                    <span class="moment-desc">{{ penaltySubject(moment.penalty) }} {{ penaltySubject(moment.penalty) ? ('GAMECARD.PENALTY_FOR' | translate) : ('GAMECARD.BENCH_PENALTY_FOR' | translate) }} {{ moment.penalty.infraction }}</span>
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
    </article>
  `,
})
export class GameCardComponent {
  private readonly translate = inject(TranslateService);

  /** Tool-call arguments can be partial while the agent is streaming them. */
  readonly toolCall = input.required<AngularToolCall<GameDetailArgs>>();

  readonly detail = computed<Partial<GameDetailArgs>>(() => this.toolCall().args);
  readonly participantLabel = participantLabel;
  readonly assistLabel = assistLabel;
  readonly penaltySubject = penaltySubject;
  readonly penaltyShortLabel = penaltyShortLabel;
  readonly momentPeriodLabel = momentPeriodLabel;

  readonly homeName = computed(() => teamNameOf(this.detail().homeTeam, this.translate.instant('GAMECARD.HOME')));
  readonly awayName = computed(() => teamNameOf(this.detail().awayTeam, this.translate.instant('GAMECARD.AWAY')));
  readonly homeLogo = computed(() => logoOf(this.detail().homeTeam));
  readonly awayLogo = computed(() => logoOf(this.detail().awayTeam));

  readonly homeScore = computed(() => this.statsOf(true)?.goalsFor ?? this.detail().homeScore ?? 0);
  readonly awayScore = computed(() => this.statsOf(false)?.goalsFor ?? this.detail().awayScore ?? 0);

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

  /** Goals and penalties merged, ordered by period then clock. */
  readonly moments = computed<KeyMoment[]>(() => {
    const args = this.detail();
    const list: KeyMoment[] = [];

    for (const goal of args.goals ?? []) {
      list.push({
        kind: 'goal',
        period: goal.period,
        seconds: clockSeconds(goal.time),
        time: goal.time ?? '',
        teamId: goal.teamId,
        goal,
      });
    }
    for (const penalty of args.penalties ?? []) {
      list.push({
        kind: 'penalty',
        period: penalty.period,
        seconds: clockSeconds(penalty.time),
        time: penalty.time ?? '',
        teamId: penalty.teamId,
        penalty,
      });
    }

    return list.sort(byMomentOrder);
  });

  private statsOf(home: boolean): GameTeamStats | undefined {
    const args = this.detail();
    const stats = args.teamStats ?? [];
    if (!stats.length) return undefined;
    const wanted = home ? resolveTeamId(args.homeTeam) : resolveTeamId(args.awayTeam);
    const match = stats.find((entry) => entry.teamId === wanted);
    return match ?? (home ? stats[0] : stats[1]);
  }

  teamNameOf(teamId: number): string {
    const args = this.detail();
    return resolveTeamName(teamId === resolveTeamId(args.homeTeam) ? args.homeTeam : args.awayTeam);
  }

  private periodsFor(home: boolean): number[] {
    const stats = this.statsOf(home);
    const cells: number[] = [];
    for (let period = 1; period <= this.periodCount(); period++) {
      cells.push(this.goalsInPeriod(home, period));
    }
    const scored = cells.reduce((sum, value) => sum + value, 0);
    if (stats && scored === 0 && stats.goalsFor > 0) {
      // Box score without goal times: show the total in the last column only.
      return this.fillUnknown(stats.goalsFor, cells.length);
    }
    return cells;
  }

  private fillUnknown(total: number, length: number): number[] {
    const cells = Array.from({ length }, () => 0);
    cells[length - 1] = total;
    return cells;
  }

  momentTags(moment: KeyMoment): MomentTag[] {
    const goal = moment.goal;
    if (!goal) return [];
    const tags: MomentTag[] = [];
    if (goal.isPowerPlay) tags.push({ label: this.translate.instant('GAMECARD.POWER_PLAY_SHORT'), detail: this.translate.instant('GAMECARD.POWER_PLAY') });
    if (goal.isShorthanded) tags.push({ label: this.translate.instant('GAMECARD.SHORTHANDED_SHORT'), detail: this.translate.instant('GAMECARD.SHORTHANDED') });
    if (goal.isEmptyNet) tags.push({ label: this.translate.instant('GAMECARD.EMPTY_NET_SHORT'), detail: this.translate.instant('GAMECARD.EMPTY_NET') });
    if (goal.isPenaltyShot) tags.push({ label: this.translate.instant('GAMECARD.PENALTY_SHOT_SHORT'), detail: this.translate.instant('GAMECARD.PENALTY_SHOT') });
    return tags;
  }

  private goalsInPeriod(home: boolean, period: number): number {
    const args = this.detail();
    const teamId = home ? resolveTeamId(args.homeTeam) : resolveTeamId(args.awayTeam);
    return (args.goals ?? []).filter((goal) => goal.teamId === teamId && goal.period === period).length;
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

/** "12:45" → 765 seconds, used to order moments on the game clock. */
function clockSeconds(time?: string): number {
  if (!time) return Number.MAX_SAFE_INTEGER;
  const parts = time.split(':').map((part) => Number.parseInt(part, 10));
  if (parts.some((part) => Number.isNaN(part))) return Number.MAX_SAFE_INTEGER;
  return parts.length === 2 ? parts[0] * 60 + parts[1] : parts[parts.length - 1] ?? Number.MAX_SAFE_INTEGER;
}

function byMomentOrder(a: KeyMoment, b: KeyMoment): number {
  return a.period - b.period || a.seconds - b.seconds;
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
function penaltyShortLabel(moment: KeyMoment): string {
  const raw = moment.penalty?.duration ?? '';
  const minutes = raw.match(/(\d+)/);
  return minutes ? minutes[1] : 'P';
}

function momentPeriodLabel(moment: KeyMoment): string {
  return moment.period <= REGULATION_PERIODS ? `P${moment.period}` : 'PROL';
}
