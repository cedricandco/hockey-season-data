import assert from 'node:assert/strict';
import test from 'node:test';
import { gameDetailResponseSchema, gameMoments, periodGoalsForTeam } from './game-detail-data.ts';

const player = (name, number) => ({ name, number });
const goal = (teamId, period, time, name, assists = [], flags = {}) => ({
  teamId, period, time, scorer: player(name, 10), assists,
  isPowerPlay: false, isShorthanded: false, isEmptyNet: false, isPenaltyShot: false, ...flags,
});
const penalty = (teamId, period, time) => ({
  teamId, period, time, player: player('Player', 7),
  infraction: 'Tripping', duration: 'Minor', isBenchPenalty: false,
});
const detail = {
  id: 123, number: '3555', date: '2026-11-08', time: '11:00', division: 'U13',
  isLive: false, isApproved: true,
  homeTeam: { id: 1, name: 'Grizzly', logoUrl: null },
  awayTeam: { id: 2, name: 'Seigneurs', logoUrl: null },
  teamStats: [
    { teamId: 1, goalsFor: 3, penaltyMinutes: 0, powerPlay: '1/2', penaltyKill: '0/0', winLossType: 'regulation' },
    { teamId: 2, goalsFor: 1, penaltyMinutes: 2, powerPlay: '0/0', penaltyKill: '1/2', winLossType: 'regulation' },
  ],
  periodGoals: [
    { teamId: 1, period: 1, goals: 1 }, { teamId: 1, period: 2, goals: 1 },
    { teamId: 1, period: 3, goals: 1 }, { teamId: 2, period: 3, goals: 1 },
  ],
  goals: [
    goal(1, 3, '1:50', 'Nathan', [], { isShorthanded: true }),
    goal(1, 1, '1:19', 'Rafael', [player('Nathan', 54)]),
    goal(2, 3, '0:27', 'Simon'),
    goal(1, 2, '4:29', 'Jacob', [], { isPowerPlay: true }),
  ],
  penalties: [penalty(2, 1, '5:18'), penalty(1, 3, '0:36')],
};

test('complete game response preserves all goals, assists and chronological ordering', () => {
  const parsed = gameDetailResponseSchema.parse(detail);
  const moments = gameMoments(parsed);
  assert.deepEqual(moments.map(({ kind, period, time }) => [kind, period, time]), [
    ['goal', 1, '1:19'], ['penalty', 1, '5:18'], ['goal', 2, '4:29'],
    ['goal', 3, '0:27'], ['penalty', 3, '0:36'], ['goal', 3, '1:50'],
  ]);
  assert.equal(moments[0].goal.assists[0].name, 'Nathan');
  assert.equal(moments[2].goal.isPowerPlay, true);
  assert.equal(moments[5].goal.isShorthanded, true);
  assert.deepEqual(periodGoalsForTeam(parsed, 1, 3), [1, 1, 1]);
  assert.deepEqual(periodGoalsForTeam(parsed, 2, 3), [0, 0, 1]);
});

test('missing or inconsistent goal data is not displayed as a fabricated period score', () => {
  assert.equal(gameDetailResponseSchema.safeParse({ ...detail, goals: undefined }).success, false);
  assert.equal(gameDetailResponseSchema.safeParse({ ...detail, periodGoals: undefined }).success, false);
  assert.deepEqual(periodGoalsForTeam({ ...detail, periodGoals: [] }, 1, 3), ['—', '—', '—']);
  assert.deepEqual(periodGoalsForTeam({ ...detail, teamStats: [] }, 1, 3), ['—', '—', '—']);
});

test('overtime goals are counted separately and timestamps sort numerically', () => {
  const overtime = {
    ...detail,
    teamStats: [{ ...detail.teamStats[0], goalsFor: 2 }, detail.teamStats[1]],
    periodGoals: [{ teamId: 1, period: 1, goals: 1 }, { teamId: 1, period: 4, goals: 1 }],
    goals: [goal(1, 4, '10:00', 'Late'), goal(1, 4, '2:00', 'Early')],
    penalties: [],
  };
  assert.deepEqual(periodGoalsForTeam(overtime, 1, 4), [1, 0, 0, 1]);
  assert.deepEqual(gameMoments(overtime).map((moment) => moment.time), ['2:00', '10:00']);
});
