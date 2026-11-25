import { Component, input } from '@angular/core';
import { TeamPageDetail, TeamPagePath } from './team.models';
import { TeamSummaryComponent } from './team-summary.component';
import { TeamGamesComponent } from './team-games.component';

/**
 * The team statistics block, rendered identically by the signed-in team page
 * and by the anonymous landing-page preview: key figures, player rankings,
 * division standings, then the last games (paged, box score in place) and the
 * next two games.
 *
 * Keeping one component for both surfaces is deliberate — the preview must show
 * exactly what a member sees. Only the interactions that need an account are
 * disabled in {@link guest} mode (opponent links, box scores), never the
 * figures themselves.
 */
@Component({
  selector: 'app-team-stats',
  standalone: true,
  imports: [TeamSummaryComponent, TeamGamesComponent],
  styleUrl: './team-stats.component.scss',
  template: `
    <app-team-summary [team]="team()" [showGames]="false">
      <app-team-games [team]="team()" [path]="path()" [guest]="guest()" />
    </app-team-summary>
  `,
})
export class TeamStatsComponent {
  readonly team = input.required<TeamPageDetail | null>();
  /** Association/division/league of the selected team, restored by each opponent link. */
  readonly path = input<TeamPagePath | null>(null);
  /** Anonymous preview: the opponent pages and the box scores need an account. */
  readonly guest = input(false);
}
