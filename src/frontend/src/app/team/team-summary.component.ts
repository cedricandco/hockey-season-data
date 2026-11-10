import { Component, computed, input } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { TeamDetail } from './team.models';
import { recentGames, teamResult } from './team-results';

@Component({
  selector: 'app-team-summary',
  standalone: true,
  imports: [TranslatePipe],
  styleUrl: './team-summary.component.scss',
  template: `
    @if (team(); as data) {
      <article class="team-summary" [attr.aria-label]="data.team.teamName">
        <header class="team-header">
          <div class="team-heading">
            <span class="team-eyebrow">{{ 'TEAM.TITLE' | translate }} · {{ data.team.division }}</span>
            <h2>{{ data.team.teamName }}</h2>
            <p>{{ data.team.wins }}–{{ data.team.losses }}–{{ data.team.ties }} <span aria-hidden="true">·</span> {{ data.team.points }} {{ 'TEAM.POINTS' | translate }}</p>
          </div>
          <div class="team-rank"><strong>{{ data.team.ranking }}</strong><span>{{ 'TEAM.RANK' | translate }}</span></div>
        </header>
        <div class="team-body">
          <div class="metrics">
            <div><strong>{{ data.totalGoals }}</strong><span>{{ 'TEAM.GOALS' | translate }}</span></div>
            <div><strong>{{ data.totalAssists }}</strong><span>{{ 'TEAM.ASSISTS' | translate }}</span></div>
            <div><strong>{{ data.totalPoints }}</strong><span>{{ 'TEAM.POINTS' | translate }}</span></div>
            <div><strong>{{ data.powerPlayGoals }}</strong><span>{{ 'TEAM.POWERPLAY' | translate }}</span></div>
            <div><strong>{{ data.shorthandedGoals }}</strong><span>{{ 'TEAM.SHORTHANDED' | translate }}</span></div>
            <div><strong>{{ data.penalties }}</strong><span>{{ 'TEAM.PENALTIES' | translate }}</span></div>
          </div>
          <section [attr.aria-label]="'TEAM.RECENT' | translate">
            <h3>{{ 'TEAM.RECENT' | translate }}</h3>
            @if (recent().length === 0) { <p class="empty">{{ 'TEAM.NO_GAMES' | translate }}</p> }
            <ul class="games">
              @for (game of recent(); track game.id) {
                <li><span class="game-date">{{ game.date }}</span>
                  <span class="game-score">{{ game.homeTeamName }} <strong>{{ game.homeScore }} – {{ game.awayScore }}</strong> {{ game.awayTeamName }}</span>
                  <span class="game-result" [class.win]="result(game, data.team.teamId) === 'TEAM.WIN'"
                    [class.loss]="result(game, data.team.teamId) === 'TEAM.LOSS'">{{ result(game, data.team.teamId) | translate }}</span></li>
              }
            </ul>
          </section>
          <section>
            <h3>{{ 'TEAM.PLAYERS' | translate }}</h3>
            @if (!data.completeRosterAvailable) { <p class="notice">{{ 'TEAM.ROSTER_LIMIT' | translate }}</p> }
            <div class="table-wrap" role="region" tabindex="0" [attr.aria-label]="'TEAM.PLAYERS' | translate"><table>
              <thead><tr><th scope="col">#</th><th scope="col">{{ 'TEAM.PLAYER' | translate }}</th><th scope="col">{{ 'TEAM.GOALS' | translate }}</th><th scope="col">{{ 'TEAM.ASSISTS' | translate }}</th><th scope="col">{{ 'TEAM.POINTS' | translate }}</th></tr></thead>
              <tbody>@for (player of data.players; track player.participantId) {
                <tr><td>{{ player.number }}</td><th scope="row">{{ player.name }}</th><td>{{ player.goals }}</td><td>{{ player.assists }}</td><td class="points">{{ player.points }}</td></tr>
              }</tbody>
            </table></div>
          </section>
          <section>
            <h3>{{ 'TEAM.STANDINGS' | translate }}</h3>
            <div class="table-wrap" role="region" tabindex="0" [attr.aria-label]="'TEAM.STANDINGS' | translate"><table>
              <thead><tr><th scope="col">{{ 'TEAM.RANK' | translate }}</th><th scope="col">{{ 'TEAM.TEAM' | translate }}</th><th scope="col">{{ 'STANDING.PLAYED' | translate }}</th><th scope="col">{{ 'STANDING.RECORD' | translate }}</th><th scope="col">{{ 'TEAM.POINTS' | translate }}</th></tr></thead>
              <tbody>@for (row of data.standings; track row.teamId) {
                <tr [class.selected]="row.teamId === data.team.teamId"><td>{{ row.ranking }}</td><th scope="row">{{ row.teamName }}</th><td>{{ row.gamesPlayed }}</td><td>{{ row.wins }}–{{ row.losses }}–{{ row.ties }}</td><td class="points">{{ row.points }}</td></tr>
              }</tbody>
            </table></div>
          </section>
        </div>
      </article>
    }
  `,
})
export class TeamSummaryComponent {
  readonly team = input.required<TeamDetail | null>();
  readonly recent = computed(() => recentGames(this.team()?.games ?? []));
  readonly result = teamResult;
}
