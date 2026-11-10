import { registerComponent } from '@copilotkit/angular';
import { z } from 'zod';
import { GameCardComponent } from './game-card.component';
import { StandingTableComponent } from './standing-table.component';
import type { LeagueStandingArgs } from './standing-table.component';
import { HOCKEY_SEASON_DATA_AGENT_ID } from '../app.config';
import { TeamCardComponent } from './team-card.component';
import type { TeamCardArgs } from './team-card.component';

const gameDetailSchema = z.object({
  gameId: z.number().int().positive(),
});

const leagueStandingRowSchema = z.object({
  ranking: z.number(),
  teamName: z.string(),
  gamesPlayed: z.number(),
  wins: z.number(),
  losses: z.number(),
  ties: z.number(),
  points: z.number(),
  goalsFor: z.number(),
  goalsAgainst: z.number(),
  goalDifferential: z.number(),
  penaltyMinutes: z.number(),
  powerPlayPercentage: z.number(),
  penaltyKillPercentage: z.number(),
});

/** Call from an injection context when a chat surface supports tool rendering. */
export function registerHockeySeasonComponents(): void {
  registerComponent<TeamCardArgs>({
    name: 'show_team_detail',
    description: 'Displays the team overview, recent games, division standings and player scoring. Pass the exact leagueId and teamId from GetTeamDetail; the component loads the data itself.',
    parameters: z.object({ leagueId: z.number().int().positive(), teamId: z.number().int().positive() }),
    component: TeamCardComponent,
    agentId: HOCKEY_SEASON_DATA_AGENT_ID,
  });
  registerComponent({
    name: 'show_game_detail',
    description:
      'Displays the box score of one game as a hockey scoreboard card: final score,' +
      ' per-period goals for both teams, then every goal and penalty grouped chronologically by period.' +
      ' Call it with the gameId returned by GetTeamGames or GetGameDetail.' +
      ' The component fetches the complete game details directly; do not copy the events into the tool call.' +
      ' Always answer the user in their language after calling this tool.',
    parameters: gameDetailSchema,
    component: GameCardComponent,
    agentId: HOCKEY_SEASON_DATA_AGENT_ID,
  });

  registerComponent<LeagueStandingArgs>({
    name: 'show_league_standing',
    description:
      'Displays the complete standings of a league as a formatted table. Call after retrieving ' +
      'GetLeagueStanding, passing the returned league name and every standings row with its original values.',
    parameters: z.object({
      leagueName: z.string(),
      standings: z.array(leagueStandingRowSchema),
    }),
    component: StandingTableComponent,
    agentId: HOCKEY_SEASON_DATA_AGENT_ID,
  });
}
