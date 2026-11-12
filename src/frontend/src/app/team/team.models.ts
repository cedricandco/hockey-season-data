export interface Choice { id: number; name: string }

export interface TeamStanding {
  teamId: number; teamName: string; division: string; ranking: number;
  gamesPlayed: number; wins: number; losses: number; ties: number; points: number;
}

export interface TeamGame {
  id: number; date: string; time: string; homeTeamId: number; awayTeamId: number;
  homeTeamName: string; awayTeamName: string; homeScore: number | null; awayScore: number | null;
}

export interface TeamPlayer {
  participantId: number; name: string; number: number | null; goals: number; assists: number; points: number;
}

export interface TeamDetail {
  leagueId: number; team: TeamStanding; standings: TeamStanding[];
  games: { game: TeamGame }[]; upcomingGames: TeamGame[]; players: TeamPlayer[];
  totalGoals: number; totalAssists: number; totalPoints: number;
  powerPlayGoals: number; shorthandedGoals: number; penalties: number;
  completeRosterAvailable: boolean;
}
