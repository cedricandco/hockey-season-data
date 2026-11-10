import { Component, computed, input } from '@angular/core';
import type { AngularToolCall } from '@copilotkit/angular';
import { TranslatePipe } from '@ngx-translate/core';

/** One row of the standings returned by GetLeagueStanding. */
export type LeagueStandingRow = {
  ranking: string | number;
  teamName: string;
  gamesPlayed: string | number;
  wins: string | number;
  losses: string | number;
  ties: string | number;
  points: string | number;
  goalsFor: string | number;
  goalsAgainst: string | number;
  goalDifferential: string | number;
  penaltyMinutes: string | number;
  powerPlayPercentage: string | number;
  penaltyKillPercentage: string | number;
};

/** Arguments the agent passes to the show_league_standing component. */
export type LeagueStandingArgs = {
  leagueName: string;
  standings: LeagueStandingRow[] | string;
};

@Component({
  selector: 'app-standing-table',
  standalone: true,
  imports: [TranslatePipe],
  template: `
    <article class="standing-card" [attr.aria-label]="'STANDING.CARD_LABEL' | translate">
      <header class="standing-card-header">
        <div class="standing-heading">
          <span class="standing-eyebrow">{{ 'STANDING.TITLE' | translate }}</span>
          <h3>{{ leagueName() }}</h3>
        </div>
        <span class="standing-team-count"
          [attr.aria-label]="'STANDING.TEAM_COUNT' | translate: { count: standings().length }">
          {{ standings().length }}
        </span>
      </header>

      @if (standings().length) {
        <div class="standing-table-wrap" role="region" tabindex="0"
          [attr.aria-label]="'STANDING.TABLE_LABEL' | translate">
          <table>
            <caption class="sr-only">{{ 'STANDING.TABLE_LABEL' | translate }}</caption>
            <thead>
              <tr>
                <th scope="col">{{ 'STANDING.RANK' | translate }}</th>
                <th scope="col" class="standing-team-heading">{{ 'STANDING.TEAM' | translate }}</th>
                <th scope="col">{{ 'STANDING.PLAYED' | translate }}</th>
                <th scope="col">{{ 'STANDING.RECORD' | translate }}</th>
                <th scope="col">{{ 'STANDING.POINTS' | translate }}</th>
                <th scope="col">{{ 'STANDING.GOALS_FOR' | translate }}</th>
                <th scope="col">{{ 'STANDING.GOALS_AGAINST' | translate }}</th>
                <th scope="col">{{ 'STANDING.DIFFERENTIAL' | translate }}</th>
                <th scope="col">{{ 'STANDING.PENALTY_MINUTES' | translate }}</th>
                <th scope="col">{{ 'STANDING.POWER_PLAY' | translate }}</th>
                <th scope="col">{{ 'STANDING.PENALTY_KILL' | translate }}</th>
              </tr>
            </thead>
            <tbody>
              @for (row of standings(); track row.teamName + row.ranking) {
                <tr>
                  <td class="standing-rank">
                    <span class="standing-rank-badge"
                      [class.rank-first]="row.ranking === 1"
                      [class.rank-second]="row.ranking === 2"
                      [class.rank-third]="row.ranking === 3">{{ display(row.ranking) }}</span>
                  </td>
                  <th scope="row" class="standing-team">{{ row.teamName }}</th>
                  <td>{{ display(row.gamesPlayed) }}</td>
                  <td class="standing-record">{{ display(row.wins) }}–{{ display(row.losses) }}–{{ display(row.ties) }}</td>
                  <td class="standing-points">{{ display(row.points) }}</td>
                  <td>{{ display(row.goalsFor) }}</td>
                  <td>{{ display(row.goalsAgainst) }}</td>
                  <td>{{ display(row.goalDifferential) }}</td>
                  <td>{{ display(row.penaltyMinutes) }}</td>
                  <td>{{ percentage(row.powerPlayPercentage) }}</td>
                  <td>{{ percentage(row.penaltyKillPercentage) }}</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
        <details class="standing-mobile-details">
          <summary>{{ 'STANDING.MORE_STATS' | translate }}</summary>
          @for (row of standings(); track row.teamName + row.ranking) {
            <section class="standing-extra">
              <h4>{{ row.teamName }}</h4>
              <dl>
                <div><dt>{{ 'STANDING.GOALS_FOR' | translate }}</dt><dd>{{ display(row.goalsFor) }}</dd></div>
                <div><dt>{{ 'STANDING.GOALS_AGAINST' | translate }}</dt><dd>{{ display(row.goalsAgainst) }}</dd></div>
                <div><dt>{{ 'STANDING.DIFFERENTIAL' | translate }}</dt><dd>{{ display(row.goalDifferential) }}</dd></div>
                <div><dt>{{ 'STANDING.PENALTY_MINUTES' | translate }}</dt><dd>{{ display(row.penaltyMinutes) }}</dd></div>
                <div><dt>{{ 'STANDING.POWER_PLAY' | translate }}</dt><dd>{{ percentage(row.powerPlayPercentage) }}</dd></div>
                <div><dt>{{ 'STANDING.PENALTY_KILL' | translate }}</dt><dd>{{ percentage(row.penaltyKillPercentage) }}</dd></div>
              </dl>
            </section>
          }
        </details>
      } @else if (toolCall().status !== 'complete') {
        <p class="standing-empty">{{ 'STANDING.LOADING' | translate }}</p>
      } @else if (hasInvalidStandings()) {
        <p class="standing-empty" role="alert">{{ 'STANDING.INVALID_DATA' | translate }}</p>
      } @else {
        <p class="standing-empty">{{ 'STANDING.EMPTY' | translate }}</p>
      }
    </article>
  `,
})
export class StandingTableComponent {
  readonly toolCall = input.required<AngularToolCall<LeagueStandingArgs>>();

  private readonly detail = computed<Partial<LeagueStandingArgs>>(() => this.toolCall().args);
  readonly leagueName = computed(() => this.detail().leagueName ?? '');
  readonly standings = computed(() => normalizeStandings(this.detail().standings));
  readonly hasInvalidStandings = computed(() => {
    const raw = this.detail().standings;
    return raw !== undefined && raw !== null && this.standings().length === 0;
  });

  display(value: string | number | undefined): string {
    return value === undefined || value === null || value === '' ? '—' : String(value);
  }

  percentage(value: string | number | undefined): string {
    const text = this.display(value);
    return text === '—' || text.endsWith('%') ? text : `${text}%`;
  }
}

function normalizeStandings(value: unknown): LeagueStandingRow[] {
  const rows = findStandingRows(value);
  return rows.flatMap((value): LeagueStandingRow[] => {
    const row = parseRecord(value);
    if (!row) return [];

    const teamValue = findField(row, 'teamName', 'team', 'name');
    const teamName = typeof teamValue === 'string'
      ? teamValue
      : typeof teamValue === 'object' && teamValue !== null
        ? findField(teamValue as Record<string, unknown>, 'name', 'teamName')
        : undefined;
    if (typeof teamName !== 'string' || !teamName.trim()) return [];

    return [{
      ranking: cellValue(findField(row, 'ranking', 'rank', 'position')),
      teamName,
      gamesPlayed: cellValue(findField(row, 'gamesPlayed', 'played', 'games')),
      wins: cellValue(findField(row, 'wins', 'victories')),
      losses: cellValue(findField(row, 'losses')),
      ties: cellValue(findField(row, 'ties', 'draws')),
      points: cellValue(findField(row, 'points', 'pts')),
      goalsFor: cellValue(findField(row, 'goalsFor', 'goalsScored', 'gf', 'bp')),
      goalsAgainst: cellValue(findField(row, 'goalsAgainst', 'ga', 'bc')),
      goalDifferential: cellValue(findField(row, 'goalDifferential', 'goalDifference', 'differential', 'diff')),
      penaltyMinutes: cellValue(findField(row, 'penaltyMinutes', 'penaltyMin', 'pim')),
      powerPlayPercentage: cellValue(findField(row, 'powerPlayPercentage', 'powerPlay', 'ppPercentage', 'anPercentage')),
      penaltyKillPercentage: cellValue(findField(row, 'penaltyKillPercentage', 'penaltyKill', 'pkPercentage', 'dnPercentage')),
    }];
  });
}

function findStandingRows(value: unknown, depth = 0): unknown[] {
  if (depth > 3) return [];
  if (typeof value === 'string') {
    const parsed = parseJson(value);
    return parsed === undefined ? [] : findStandingRows(parsed, depth + 1);
  }
  if (Array.isArray(value)) return value;
  if (typeof value !== 'object' || value === null) return [];

  const record = value as Record<string, unknown>;
  const rows = findField(record, 'standings', 'rows', 'data', 'result', 'value');
  return rows === undefined ? [] : findStandingRows(rows, depth + 1);
}

function parseRecord(value: unknown): Record<string, unknown> | undefined {
  if (typeof value === 'string') {
    const parsed = parseJson(value);
    return typeof parsed === 'object' && parsed !== null && !Array.isArray(parsed)
      ? parsed as Record<string, unknown>
      : undefined;
  }
  return typeof value === 'object' && value !== null && !Array.isArray(value)
    ? value as Record<string, unknown>
    : undefined;
}

function parseJson(value: string): unknown {
  const trimmed = value.trim().replace(/^```(?:json)?\s*|\s*```$/gi, '');
  if (!trimmed.startsWith('[') && !trimmed.startsWith('{')) return undefined;
  try {
    return JSON.parse(trimmed) as unknown;
  } catch {
    return undefined;
  }
}

function findField(record: Record<string, unknown>, ...names: string[]): unknown {
  const keys = new Map(Object.keys(record).map((key) => [normalizeKey(key), key]));
  for (const name of names) {
    const key = keys.get(normalizeKey(name));
    if (key !== undefined) return record[key];
  }
  return undefined;
}

function normalizeKey(value: string): string {
  return value.replace(/[^a-zA-Z0-9]/g, '').toLowerCase();
}

function cellValue(value: unknown): string | number {
  return typeof value === 'string' || typeof value === 'number' ? value : '';
}
