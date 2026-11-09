import { registerComponent } from '@copilotkit/angular';
import { z } from 'zod';
import { GameCardComponent } from './game-card.component';
import { HOCKEY_SEASON_DATA_AGENT_ID } from '../app.config';

const participant = z.object({
  name: z.string(),
  number: z.number().optional(),
});

const team = z.union([
  z.string(),
  z.object({
    id: z.number().optional(),
    teamId: z.number().optional(),
    name: z.string(),
    logoUrl: z.string().nullable().optional(),
  }),
]);

const gameDetailSchema = z.object({
  gameId: z.number().optional(),
  gameNumber: z.string().optional(),
  date: z.string().optional(),
  time: z.string().optional(),
  division: z.string().optional(),
  isLive: z.boolean().optional(),
  isApproved: z.boolean().optional(),
  homeTeam: team,
  awayTeam: team,
  homeScore: z.number().optional(),
  awayScore: z.number().optional(),
  teamStats: z.array(z.object({
    teamId: z.number(),
    goalsFor: z.number(),
    penaltyMinutes: z.number().optional(),
    powerPlay: z.string().optional(),
    penaltyKill: z.string().optional(),
    winLossType: z.string().optional(),
  })).optional(),
  goals: z.array(z.object({
    teamId: z.number(),
    period: z.number(),
    time: z.string(),
    scorer: participant,
    assists: z.array(participant).optional(),
    isPowerPlay: z.boolean().optional(),
    isShorthanded: z.boolean().optional(),
    isEmptyNet: z.boolean().optional(),
    isPenaltyShot: z.boolean().optional(),
  })).optional(),
  penalties: z.array(z.object({
    teamId: z.number(),
    period: z.number(),
    time: z.string(),
    player: participant.optional(),
    infraction: z.string(),
    duration: z.string().optional(),
    isBenchPenalty: z.boolean().optional(),
  })).optional(),
});

/** Call from an injection context when a chat surface supports tool rendering. */
export function registerHockeySeasonComponents(): void {
  registerComponent({
    name: 'show_game_detail',
    description:
      'Displays the box score of one game as a hockey scoreboard card: final score,' +
      ' per-period goals for both teams, then every goal and penalty in game order.' +
      ' Call it with the full GetGameDetail payload (both teams, team stats, goals and penalties).' +
      ' Always answer the user in their language after calling this tool.',
    parameters: gameDetailSchema,
    component: GameCardComponent,
    agentId: HOCKEY_SEASON_DATA_AGENT_ID,
  });
}
