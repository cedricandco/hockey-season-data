import { Component, computed, effect, inject, input, signal } from '@angular/core';
import type { AngularToolCall } from '@copilotkit/angular';
import { TranslatePipe } from '@ngx-translate/core';
import { ApiService } from '../core/api.service';
import { TeamDetail } from '../team/team.models';
import { TeamSummaryComponent } from '../team/team-summary.component';

export type TeamCardArgs = { leagueId: number; teamId: number };

@Component({
  selector: 'app-team-card',
  standalone: true,
  imports: [TranslatePipe, TeamSummaryComponent],
  template: `
    @if (loading()) { <p role="status">{{ 'TEAM.LOADING' | translate }}</p> }
    @if (error()) { <p role="alert">{{ 'TEAM.ERROR' | translate }} <button type="button" (click)="retry()">{{ 'TEAM.RETRY' | translate }}</button></p> }
    <app-team-summary [team]="detail()" />
  `,
})
export class TeamCardComponent {
  private readonly api = inject(ApiService);
  private readonly retries = signal(0);
  readonly toolCall = input.required<AngularToolCall<TeamCardArgs>>();
  readonly args = computed(() => this.toolCall().status === 'in-progress' ? undefined : this.toolCall().args);
  readonly detail = signal<TeamDetail | null>(null);
  readonly loading = signal(true);
  readonly error = signal(false);

  constructor() {
    effect(onCleanup => {
      const args = this.args();
      this.retries();
      this.detail.set(null); this.error.set(false); this.loading.set(true);
      if (!args) return;
      if (typeof args.leagueId !== 'number' || !Number.isSafeInteger(args.leagueId) || args.leagueId <= 0 ||
          typeof args.teamId !== 'number' || !Number.isSafeInteger(args.teamId) || args.teamId <= 0) {
        console.error('Invalid team detail identifiers', args);
        this.loading.set(false); this.error.set(true); return;
      }
      const subscription = this.api.getTeamDetail(args.leagueId, args.teamId).subscribe({
        next: value => { this.detail.set(value); this.loading.set(false); },
        error: error => { console.error('Unable to load team detail', error); this.loading.set(false); this.error.set(true); },
      });
      onCleanup(() => subscription.unsubscribe());
    });
  }
  retry(): void { this.retries.update(value => value + 1); }
}
