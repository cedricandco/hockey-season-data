import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import type { AngularToolCall } from '@copilotkit/angular';
import { TranslatePipe } from '@ngx-translate/core';
import { ApiService } from '../core/api.service';
import { gameDetailResponseSchema } from './game-detail-data';
import type { GameDetailResponse } from './game-detail-data';
import { GameDetailComponent } from './game-detail.component';

/** Arguments the agent passes to the show_game_detail tool. */
export type GameDetailArgs = {
  gameId: number;
};

/**
 * Chat renderer of the `show_game_detail` tool call: loads the box score the
 * agent selected and hands it to the shared card, which the team page also
 * uses for its "game detail" button.
 */
@Component({
  selector: 'app-game-card',
  standalone: true,
  imports: [TranslatePipe, GameDetailComponent],
  template: `
    @if (loading()) {
      <p class="game-card-message" role="status">{{ 'GAMECARD.LOADING' | translate }}</p>
    } @else if (error()) {
      <div class="game-card-message" role="alert">
        {{ error()! | translate }}
        <button type="button" (click)="retry()">{{ 'GAMECARD.RETRY' | translate }}</button>
      </div>
    } @else if (gameDetail(); as detail) {
      <app-game-detail [game]="detail" />
    }
  `,
})
export class GameCardComponent {
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
}
