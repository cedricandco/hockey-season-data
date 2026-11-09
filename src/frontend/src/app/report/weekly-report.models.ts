export interface ReportSource<T> {
  available: boolean;
  data: T[] | null;
  error?: string | null;
}

export interface Standing {
  teamId: number;
  ranking: number;
  manualRanking?: number | null;
  gamesPlayed: number;
  wins: number;
  losses: number;
  ties: number;
  points: number;
  team: { name: string; division: string };
}

export interface Game {
  id: number;
  date: string;
  startTime: string;
  timezone?: string | null;
  homeTeamId: number;
  awayTeamId: number;
  homeTeam: { name: string };
  awayTeam: { name: string };
  score?: Record<string, number>;
}

export interface Suspension {
  id: string;
  status: string;
  participant?: { fullName: string; number: number } | null;
  team: { id: number; name: string };
  code: string;
  requiredGames: number;
  purgedGames?: number[];
}

export interface ReportDivision {
  divisionId: number;
  leagueName?: string | null;
  standings: ReportSource<Standing>;
  games: ReportSource<Game>;
  suspensions: ReportSource<Suspension>;
}

export interface WeeklyReport {
  startDate: string;
  endDate: string;
  teamIds: number[];
  divisions: ReportDivision[];
}
