import { z } from 'zod';

const participantSchema = z.object({
  name: z.string(),
  // Absent (null) for non-player participants, e.g. a coach serving a penalty.
  number: z.number().nullable(),
});

export const gameDetailResponseSchema = z.object({
  id: z.number().int().positive(),
  number: z.string(),
  date: z.string(),
  time: z.string(),
  division: z.string(),
  isLive: z.boolean(),
  isApproved: z.boolean(),
  homeTeam: z.object({ id: z.number(), name: z.string(), logoUrl: z.string().nullable() }),
  awayTeam: z.object({ id: z.number(), name: z.string(), logoUrl: z.string().nullable() }),
  teamStats: z.array(z.object({
    teamId: z.number(),
    goalsFor: z.number(),
    penaltyMinutes: z.number(),
    powerPlay: z.string(),
    penaltyKill: z.string(),
    winLossType: z.string(),
  })),
  periodGoals: z.array(z.object({
    teamId: z.number(),
    period: z.number(),
    goals: z.number(),
  })),
  goals: z.array(z.object({
    teamId: z.number(),
    period: z.number(),
    time: z.string(),
    scorer: participantSchema,
    assists: z.array(participantSchema),
    isPowerPlay: z.boolean(),
    isShorthanded: z.boolean(),
    isEmptyNet: z.boolean(),
    isPenaltyShot: z.boolean(),
  })),
  penalties: z.array(z.object({
    teamId: z.number(),
    period: z.number(),
    time: z.string(),
    player: participantSchema,
    infraction: z.string(),
    duration: z.string(),
    isBenchPenalty: z.boolean(),
  })),
});

export type GameDetailResponse = z.infer<typeof gameDetailResponseSchema>;

export type GameMoment =
  | { kind: 'goal'; period: number; time: string; teamId: number; goal: GameDetailResponse['goals'][number]; penalty?: never }
  | { kind: 'penalty'; period: number; time: string; teamId: number; penalty: GameDetailResponse['penalties'][number]; goal?: never };

export function gameMoments(detail: GameDetailResponse): GameMoment[] {
  const moments: GameMoment[] = [
    ...detail.goals.map((goal): GameMoment => ({ kind: 'goal', period: goal.period, time: goal.time, teamId: goal.teamId, goal })),
    ...detail.penalties.map((penalty): GameMoment => ({ kind: 'penalty', period: penalty.period, time: penalty.time, teamId: penalty.teamId, penalty })),
  ];
  return moments.sort((a, b) => a.period - b.period || clockSeconds(a.time) - clockSeconds(b.time));
}

export function periodGoalsForTeam(detail: GameDetailResponse, teamId: number, periodCount: number): (number | string)[] {
  const counts = Array.from({ length: periodCount }, (_, index) =>
    detail.periodGoals.find((entry) => entry.teamId === teamId && entry.period === index + 1)?.goals ?? 0,
  );
  const score = detail.teamStats.find((stats) => stats.teamId === teamId)?.goalsFor;
  return score === undefined || counts.reduce((total, goals) => total + goals, 0) !== score
    ? counts.map(() => '—')
    : counts;
}

function clockSeconds(time: string): number {
  const match = /^(\d+):([0-5]\d)$/.exec(time);
  return match ? Number(match[1]) * 60 + Number(match[2]) : Number.MAX_SAFE_INTEGER;
}
