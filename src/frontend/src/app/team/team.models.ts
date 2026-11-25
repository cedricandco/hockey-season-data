/**
 * Association of the team page, returned by `GET /api/nav/associations`: the
 * associations with a followed team. `associationId` is `organizations.id`.
 */
export interface NavChoice { associationId: number; name: string }

/**
 * Division of the team page, returned by `GET /api/nav/associations/{id}/divisions`.
 * `divisionId` is `divisions.id`.
 */
export interface NavDivision { divisionId: number; name: string }

/**
 * League of the team page, returned by `GET /api/nav/divisions/{id}/leagues`.
 * `leagueId` is `leagues.id`.
 */
export interface NavLeague { leagueId: number; name: string }

/**
 * Team of the team page's selectors, returned by `GET /api/nav/leagues/{id}/teams`.
 * `teamId` is `teams.id`.
 */
export interface NavTeam { teamId: number; name: string }

/**
 * One team the caller follows, returned by `GET /api/nav/teams`: the whole
 * follow list of the team page, with the names of its league, division and
 * association.
 */
export interface FollowedTeam {
  teamId: number;
  name: string;
  leagueId: number;
  leagueName: string;
  divisionId: number;
  divisionName: string;
  associationId: number;
  associationName: string;
}

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

/**
 * Bridge between the Spordle team id used by the games and the database id the
 * team page navigates with. The page only ever sends `pageTeamId`.
 */
export interface TeamPageLink { externalTeamId: number; pageTeamId: number }

/** Association → division → league → team, as carried by the query string. */
export interface RouteSelection {
  association: number; division: number; league: number; team: number;
}

/** Association/division/league of the selected team, restored by each opponent link. */
export type TeamPagePath = Omit<RouteSelection, 'team'>;

export interface TeamDetail {
  leagueId: number; team: TeamStanding; standings: TeamStanding[];
  games: { game: TeamGame }[]; upcomingGames: TeamGame[]; players: TeamPlayer[];
  totalGoals: number; totalAssists: number; totalPoints: number;
  powerPlayGoals: number; shorthandedGoals: number; penalties: number;
  completeRosterAvailable: boolean;
}

/**
 * Team detail as read by the team page (`GET /api/teams/{teamId}`): the same
 * figures as {@link TeamDetail}, plus the Spordle ↔ database team mapping used
 * to open an opponent's page from a score line.
 */
export interface TeamPageDetail extends TeamDetail { teamLinks: TeamPageLink[] }
