import { Component, DestroyRef, computed, inject, input, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { ApiService } from '../core/api.service';
import { gameDetailResponseSchema } from '../chat/game-detail-data';
import type { GameDetailResponse } from '../chat/game-detail-data';
import { GameDetailComponent } from '../chat/game-detail.component';
import type { TeamGame, TeamPageDetail, TeamPagePath } from './team.models';
import {
  RECENT_PAGE_SIZE, UPCOMING_COUNT, gameSides, nextGames, playedGames, teamResult,
} from './team-results';

/**
 * Played and upcoming games of the team page.
 *
 * The last games are paged by five: the button adds five more each time and
 * disappears once the whole schedule is shown. Each played game expands its box
 * score in place — the same card the chat renders — and the next two games link
 * their opponent to that opponent's team page.
 *
 * The page navigates with database ids while the score lines carry Spordle ones,
 * so an opponent is linked with the `teams.id` that
 * {@link TeamPageDetail.teamLinks} maps: a team the mapping does not know stays
 * plain text rather than a broken link.
 */
@Component({
  selector: 'app-team-games',
  standalone: true,
  imports: [RouterLink, TranslatePipe, GameDetailComponent],
  styleUrl: './team-games.component.scss',
  template: `
    @if (team(); as data) {
      <div class="team-games">
        <section class="games-block" [attr.aria-label]="'TEAM.RECENT' | translate">
          <header class="block-header">
            <h3>{{ 'TEAM.RECENT' | translate }}</h3>
            @if (played().length) {
              <span class="block-count">{{ shown().length }} / {{ played().length }}</span>
            }
          </header>
          @if (played().length === 0) {
            <p class="empty">{{ 'TEAM.NO_GAMES' | translate }}</p>
          }
          <ul class="games">
            @for (game of shown(); track game.id) {
              <li class="game-row">
                <div class="game-line">
                  <span class="game-date">{{ game.date }}<span class="game-time">{{ game.time }}</span></span>
                  <span class="game-score">
                    @for (side of sides(game); track $index) {
                      <span class="game-team" [class.own]="side.own">{{ side.name }}</span>
                      @if (!$last) { <strong>{{ scoreLabel(game) }}</strong> }
                    }
                  </span>
                  <span class="game-result" [class.win]="result(game, data.team.teamId) === 'TEAM.WIN'"
                    [class.loss]="result(game, data.team.teamId) === 'TEAM.LOSS'">{{ result(game, data.team.teamId) | translate }}</span>
                  <button type="button" class="detail-button" [attr.aria-expanded]="openGameId() === game.id"
                    [attr.aria-controls]="'game-detail-' + game.id" (click)="toggleDetail(game.id)">
                    {{ (openGameId() === game.id ? 'TEAM.HIDE_DETAIL' : 'TEAM.SHOW_DETAIL') | translate }}
                  </button>
                </div>
                @if (openGameId() === game.id) {
                  <div class="game-detail" [id]="'game-detail-' + game.id">
                    @if (detailLoading()) {
                      <p class="game-detail-message" role="status">{{ 'GAMECARD.LOADING' | translate }}</p>
                    } @else if (detailError()) {
                      <p class="game-detail-message" role="alert">
                        {{ detailError()! | translate }}
                        <button type="button" (click)="loadDetail(game.id)">{{ 'GAMECARD.RETRY' | translate }}</button>
                      </p>
                    } @else if (detail(); as loaded) {
                      <app-game-detail [game]="loaded" />
                    }
                  </div>
                }
              </li>
            }
          </ul>
          @if (remaining() > 0) {
            <button type="button" class="more-button" (click)="showMore()">
              {{ 'TEAM.MORE_GAMES' | translate: { count: nextPageSize() } }}
            </button>
          }
        </section>

        <section class="games-block" [attr.aria-label]="'TEAM.NEXT' | translate">
          <h3>{{ 'TEAM.NEXT' | translate }}</h3>
          @if (upcoming().length === 0) {
            <p class="empty">{{ 'TEAM.NO_UPCOMING' | translate }}</p>
          }
          <ul class="games">
            @for (game of upcoming(); track game.id) {
              <li class="game-row">
                <div class="game-line">
                  <span class="game-date">{{ game.date }}<span class="game-time">{{ game.time }}</span></span>
                  <span class="game-score">
                    @for (side of sides(game); track $index) {
                      @if (side.own || !side.pageTeamId) {
                        <span class="game-team" [class.own]="side.own">{{ side.name }}</span>
                      } @else {
                        <a class="game-team opponent" [routerLink]="['/team']"
                          [queryParams]="opponentQuery(side.pageTeamId)"
                          [attr.title]="'TEAM.OPPONENT_PAGE' | translate">{{ side.name }}</a>
                      }
                      @if (!$last) { <span class="versus">{{ 'TEAM.VERSUS' | translate }}</span> }
                    }
                  </span>
                </div>
              </li>
            }
          </ul>
        </section>
      </div>
    }
  `,
})
export class TeamGamesComponent {
  private readonly api = inject(ApiService);
  private subscription?: { unsubscribe(): void };
  private readonly visibleCount = signal(RECENT_PAGE_SIZE);

  readonly team = input.required<TeamPageDetail | null>();
  /** Association/division/league of the selected team, restored by each opponent link. */
  readonly path = input<TeamPagePath | null>(null);

  readonly result = teamResult;
  readonly played = computed(() => playedGames(this.team()?.games ?? []));
  readonly shown = computed(() => this.played().slice(0, this.visibleCount()));
  readonly remaining = computed(() => Math.max(0, this.played().length - this.visibleCount()));
  readonly nextPageSize = computed(() => Math.min(RECENT_PAGE_SIZE, this.remaining()));
  /** The two next games only: the section answers "when do we play next?". */
  readonly upcoming = computed(() => nextGames(this.team()?.upcomingGames ?? [], UPCOMING_COUNT));

  readonly openGameId = signal(0);
  readonly detail = signal<GameDetailResponse | null>(null);
  readonly detailLoading = signal(false);
  readonly detailError = signal<string | null>(null);

  constructor() {
    inject(DestroyRef).onDestroy(() => this.subscription?.unsubscribe());
  }

  showMore(): void {
    this.visibleCount.update(count => count + RECENT_PAGE_SIZE);
  }

  /** Home then away, with our own team highlighted and the opponents linked. */
  sides(game: TeamGame) {
    const teamId = this.team()?.team.teamId ?? 0;
    return gameSides(game, teamId, this.team()?.teamLinks ?? []);
  }

  scoreLabel(game: TeamGame): string {
    return game.homeScore === null || game.awayScore === null ? '–' : `${game.homeScore} – ${game.awayScore}`;
  }

  /** Query parameters that restore the opponent's team page, selectors included. */
  opponentQuery(teamId: number): Record<string, number> {
    const path = this.path();
    return {
      ...(path ? { association: path.association, division: path.division, league: path.league } : {}),
      team: teamId,
    };
  }

  /** Opens the box score of one game in place, closing the one that was open. */
  toggleDetail(gameId: number): void {
    if (this.openGameId() === gameId) {
      this.openGameId.set(0);
      this.subscription?.unsubscribe();
      this.detail.set(null);
      this.detailError.set(null);
      return;
    }

    this.openGameId.set(gameId);
    this.loadDetail(gameId);
  }

  loadDetail(gameId: number): void {
    this.subscription?.unsubscribe();
    this.detail.set(null);
    this.detailError.set(null);
    this.detailLoading.set(true);
    this.subscription = this.api.getGameDetail(gameId).subscribe({
      next: (response) => {
        const parsed = gameDetailResponseSchema.safeParse(response);
        if (!parsed.success || parsed.data.id !== gameId) {
          console.error('Invalid game detail response', parsed.success ? 'Game ID mismatch' : parsed.error);
          this.detailError.set('GAMECARD.INVALID_RESPONSE');
        } else {
          this.detail.set(parsed.data);
        }
        this.detailLoading.set(false);
      },
      error: (error: unknown) => {
        console.error('Could not load game detail', error);
        this.detailLoading.set(false);
        this.detailError.set(error instanceof HttpErrorResponse && error.status === 404
          ? 'GAMECARD.NOT_FOUND'
          : 'GAMECARD.ERROR');
      },
    });
  }
}
