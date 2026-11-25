import { chromium } from 'playwright-core';
import { spawn } from 'node:child_process';
import { existsSync } from 'node:fs';
import { createServer } from 'node:net';
import { createSign, generateKeyPairSync } from 'node:crypto';
import { setTimeout as sleep } from 'node:timers/promises';
import { fileURLToPath } from 'node:url';
import { resolve } from 'node:path';

const root = fileURLToPath(new URL('..', import.meta.url));
const allViewports = [
  { width: 320, height: 568 }, { width: 375, height: 667 },
  { width: 390, height: 844 }, { width: 430, height: 932 },
  { width: 568, height: 320 }, { width: 768, height: 1024 }, { width: 1280, height: 800 },
];
/** `VIEWPORTS=320,1280 npm run test:responsive` runs only those widths. */
const onlyWidths = (process.env['VIEWPORTS'] ?? '').split(',').map(value => Number(value.trim())).filter(Boolean);
const viewports = onlyWidths.length ? allViewports.filter(item => onlyWidths.includes(item.width)) : allViewports;
const failures = [];
const teamName = 'Les Grizzlys de Sainte-Julie et de la Vallée du Richelieu';
const opponentName = 'Les Canadiens de Saint-Jean-sur-Richelieu et de la Montérégie';
const playerName = 'Alexandre Jean-Baptiste Tremblay-Lavoie';
const standing = {
  teamId: 1, teamName, division: 'M13 A', ranking: 1,
  gamesPlayed: 4, wins: 3, losses: 1, ties: 0, points: 6,
};
const chatStanding = {
  ranking: 1, teamName, gamesPlayed: 12, wins: 8, losses: 3, ties: 1, points: 17,
  goalsFor: 38, goalsAgainst: 24, goalDifferential: 14, penaltyMinutes: 26,
  powerPlayPercentage: 21.5, penaltyKillPercentage: 82.4,
};
const chatQuestion = 'Montre le classement';
const gameQuestion = 'Montre le match 123';

/**
 * The app authenticates with Auth0 Universal Login, so the suite serves a fake
 * tenant: `/authorize` bounces back with a code, `/oauth/token` mints tokens
 * signed by a key this process generates, and `/.well-known/jwks.json` publishes
 * the matching public key so the SDK's signature check passes.
 */
const auth0Host = 'dev-fwr0a0nxb1dt4f46.us.auth0.com';
const auth0ClientId = 'IDv6Vf6vsZnh5A0VzGevrfX8YuC1uL3M';
const auth0Scope = 'openid profile email read:reports read:teams use:agent';
const { publicKey: auth0PublicKey, privateKey: auth0PrivateKey } =
  generateKeyPairSync('rsa', { modulusLength: 2048 });
const auth0KeyId = 'responsive-test-key';
const auth0Jwk = { ...auth0PublicKey.export({ format: 'jwk' }), kid: auth0KeyId, use: 'sig', alg: 'RS256' };

function signedToken(payload) {
  const header = Buffer.from(JSON.stringify({ alg: 'RS256', typ: 'JWT', kid: auth0KeyId })).toString('base64url');
  const body = Buffer.from(JSON.stringify(payload)).toString('base64url');
  const signature = createSign('RSA-SHA256').update(`${header}.${body}`).sign(auth0PrivateKey).toString('base64url');
  return `${header}.${body}.${signature}`;
}

/** Routes the Auth0 calls of the SDK to the fake tenant above. */
async function mockAuth0(route, url, baseUrl, state) {
  const cors = {
    'access-control-allow-origin': baseUrl,
    'access-control-allow-credentials': 'true',
    'access-control-allow-headers': '*',
    'access-control-allow-methods': 'POST, GET, OPTIONS',
  };
  if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 204, headers: cors });
  if (url.pathname === '/authorize') {
    state.nonce = url.searchParams.get('nonce') ?? state.nonce;
    const redirect = url.searchParams.get('redirect_uri');
    const query = url.searchParams.get('state');
    return route.fulfill({ status: 302, headers: { location: `${redirect}?code=test-code&state=${query}` } });
  }
  const now = Math.floor(Date.now() / 1000);
  if (url.pathname === '/oauth/token') {
    return route.fulfill({
      status: 200, headers: cors, json: {
        access_token: signedToken({
          sub: 'auth0|test', aud: 'https://api.hockeyseason.dev', iss: `https://${auth0Host}/`,
          azp: auth0ClientId, scope: auth0Scope, iat: now, exp: now + 3600,
        }),
        id_token: signedToken({
          sub: 'auth0|test', aud: auth0ClientId, iss: `https://${auth0Host}/`, azp: auth0ClientId,
          nonce: state.nonce, iat: now, exp: now + 3600,
          email: 'test@example.com', name: 'Test Hockey',
        }),
        scope: auth0Scope, expires_in: 3600, token_type: 'Bearer',
      },
    });
  }
  if (url.pathname === '/.well-known/jwks.json') {
    return route.fulfill({ status: 200, headers: cors, json: { keys: [auth0Jwk] } });
  }
  if (url.pathname === '/.well-known/openid-configuration') {
    return route.fulfill({
      status: 200, headers: cors, json: {
        issuer: `https://${auth0Host}/`, jwks_uri: `https://${auth0Host}/.well-known/jwks.json`,
        authorization_endpoint: `https://${auth0Host}/authorize`, token_endpoint: `https://${auth0Host}/oauth/token`,
      },
    });
  }
  return route.fulfill({ status: 200, headers: cors, json: {} });
}

const fixtures = {
  '/api/auth/login': { success: true, accessToken: 'local-test-token', expiresInSeconds: 3600, tokenType: 'Bearer' },
  // Local account of the caller: drives the admin navigation entry (non-admin here).
  '/api/users/me': {
    id: 1, auth0UserId: 'auth0|test', email: 'test@example.com', firstName: 'Test', lastName: 'Hockey',
    isAdmin: false, roles: [], settings: { primaryColor: null, secondaryColor: null, extra: {} },
    // The account already follows a team: the first-run selection page is not shown.
    teamCount: 1,
  },
  '/api/offices': [{ id: 1, name: 'Association Exemple' }],
  '/api/offices/1/leagues': [{ id: 1, name: 'Ligue Exemple' }],
  '/api/leagues/1/teams': [{ id: 1, name: teamName }],
  // Team page: the teams the caller follows (database ids), then the
  // association → division → league → team levels of the selector.
  '/api/nav/teams': [{
    teamId: 1, name: teamName, leagueId: 1, leagueName: 'Ligue Exemple',
    divisionId: 6193, divisionName: 'Simple lettre', associationId: 1, associationName: 'Hockey Richelieu',
  }],
  '/api/nav/associations': [{ associationId: 1, name: 'Hockey Richelieu' }],
  '/api/nav/associations/1/divisions': [{ divisionId: 6193, name: 'Simple lettre' }],
  '/api/nav/divisions/6193/leagues': [{ leagueId: 1, name: 'Ligue Exemple' }],
  '/api/nav/leagues/1/teams': [{ teamId: 1, name: teamName }, { teamId: 2, name: opponentName }],
  // Detail of the selected followed team: keyed on teams.id only. Seven played
  // games page by five, three upcoming games show the next two, and teamLinks
  // maps the Spordle ids of the score lines to the page ids.
  '/api/teams/1': {
    leagueId: 1, team: standing, standings: [standing],
    games: [
      { game: { id: 11, date: '2026-01-03', time: '12:00', homeTeamId: 1, awayTeamId: 2,
        homeTeamName: teamName, awayTeamName: opponentName, homeScore: 3, awayScore: 2 } },
      { game: { id: 12, date: '2026-01-10', time: '13:00', homeTeamId: 2, awayTeamId: 1,
        homeTeamName: opponentName, awayTeamName: teamName, homeScore: 1, awayScore: 4 } },
      { game: { id: 13, date: '2026-01-17', time: '18:00', homeTeamId: 1, awayTeamId: 2,
        homeTeamName: teamName, awayTeamName: opponentName, homeScore: 0, awayScore: 0 } },
      { game: { id: 14, date: '2026-01-24', time: '10:00', homeTeamId: 2, awayTeamId: 1,
        homeTeamName: opponentName, awayTeamName: teamName, homeScore: 5, awayScore: 2 } },
      { game: { id: 15, date: '2026-01-31', time: '19:30', homeTeamId: 1, awayTeamId: 2,
        homeTeamName: teamName, awayTeamName: opponentName, homeScore: 4, awayScore: 1 } },
      { game: { id: 16, date: '2026-02-03', time: '09:00', homeTeamId: 2, awayTeamId: 1,
        homeTeamName: opponentName, awayTeamName: teamName, homeScore: 2, awayScore: 3 } },
      { game: { id: 17, date: '2026-02-05', time: '20:00', homeTeamId: 1, awayTeamId: 2,
        homeTeamName: teamName, awayTeamName: opponentName, homeScore: 1, awayScore: 6 } },
  ],
    upcomingGames: [
      { id: 20, date: '2026-02-07', time: '12:00', homeTeamId: 1, awayTeamId: 2,
        homeTeamName: teamName, awayTeamName: opponentName, homeScore: null, awayScore: null },
      { id: 21, date: '2026-02-14', time: '12:00', homeTeamId: 2, awayTeamId: 1,
        homeTeamName: opponentName, awayTeamName: teamName, homeScore: null, awayScore: null },
      { id: 22, date: '2026-02-21', time: '12:00', homeTeamId: 1, awayTeamId: 2,
        homeTeamName: teamName, awayTeamName: opponentName, homeScore: null, awayScore: null },
    ],
    players: [{ participantId: 1, name: playerName, number: 9, goals: 3, assists: 2, points: 5 }],
    totalGoals: 3, totalAssists: 2, totalPoints: 5, powerPlayGoals: 1,
    shorthandedGoals: 0, penalties: 0, completeRosterAvailable: true,
    teamLinks: [{ externalTeamId: 1, pageTeamId: 1 }, { externalTeamId: 2, pageTeamId: 2 }],
  },
  // The opponent's own page, opened from a next game or a score line.
  '/api/teams/2': {
    leagueId: 1,
    team: { ...standing, teamId: 2, teamName: opponentName, ranking: 2, wins: 1, losses: 3 },
    standings: [{ ...standing, teamId: 2, teamName: opponentName, ranking: 2, wins: 1, losses: 3 }],
    games: [], upcomingGames: [],
    players: [], totalGoals: 0, totalAssists: 0, totalPoints: 0, powerPlayGoals: 0,
    shorthandedGoals: 0, penalties: 0, completeRosterAvailable: true,
    teamLinks: [{ externalTeamId: 1, pageTeamId: 1 }, { externalTeamId: 2, pageTeamId: 2 }],
  },
  // Configuration page: followed teams + licence headroom, and the
  // association → division → league → team navigation of the selector.
  '/api/users/me/subscriptions': {
    teams: [{ teamId: 101, name: teamName, createdAt: '2026-11-01T12:00:00Z', lockedUntil: '2026-11-16T12:00:00Z' }],
    leagues: [],
    organizations: [],
    limits: [
      { kind: 'team', max: 5, used: 1, canAddMore: true },
      { kind: 'league', max: 0, used: 0, canAddMore: false },
      { kind: 'organization', max: null, used: 0, canAddMore: true },
    ],
  },
  '/api/subscriptions/associations': { associations: [{ id: 1, name: 'Hockey Richelieu' }] },
  // Anonymous landing-page preview: association → division → league → team.
  '/api/preview/associations': { associations: [{ id: 1, name: 'Hockey Richelieu' }] },
  '/api/preview/associations/1/divisions': { divisions: [{ id: 6193, name: 'Simple lettre' }] },
  '/api/preview/divisions/6193/leagues': {
    seasonId: '2026-27',
    leagues: [{ id: '13c38dd1-e464-4835-af5f-75be8561daf6', name: 'M13 B' }],
  },
  '/api/preview/leagues/13c38dd1-e464-4835-af5f-75be8561daf6/teams': {
    teams: [{ id: 1, name: teamName }, { id: 2, name: opponentName }],
  },
  '/api/subscriptions/associations/1/divisions': { divisions: [{ id: 6193, name: 'Simple lettre' }] },
  // Anonymous preview detail: same shape as /api/teams/1 (the landing page
  // renders it with the shared app-team-stats component).
  '/api/preview/teams/1': {
    leagueId: 1, team: standing, standings: [standing],
    games: [
      { game: { id: 11, date: '2026-01-03', time: '12:00', homeTeamId: 1, awayTeamId: 2,
        homeTeamName: teamName, awayTeamName: opponentName, homeScore: 3, awayScore: 2 } },
    ],
    upcomingGames: [
      { id: 20, date: '2026-02-07', time: '12:00', homeTeamId: 1, awayTeamId: 2,
        homeTeamName: teamName, awayTeamName: opponentName, homeScore: null, awayScore: null },
    ],
    players: [{ participantId: 1, name: playerName, number: 9, goals: 3, assists: 2, points: 5 }],
    totalGoals: 3, totalAssists: 2, totalPoints: 5, powerPlayGoals: 1,
    shorthandedGoals: 0, penalties: 0, completeRosterAvailable: true,
    teamLinks: [{ externalTeamId: 1, pageTeamId: 1 }, { externalTeamId: 2, pageTeamId: 2 }],
  },
  '/api/subscriptions/divisions/6193/leagues': {
    seasonId: '2026-27',
    leagues: [{ id: '13c38dd1-e464-4835-af5f-75be8561daf6', name: 'M13 B' }],
  },
  '/api/subscriptions/leagues/13c38dd1-e464-4835-af5f-75be8561daf6/teams': {
    teams: [{ id: 1, name: teamName }, { id: 102, name: opponentName }],
  },
  '/api/leagues/1/teams/1': {
    leagueId: 1, team: standing, standings: [standing], games: [{ game: {
      id: 1, date: '2026-01-03', time: '12:00', homeTeamId: 1, awayTeamId: 2,
      homeTeamName: teamName, awayTeamName: opponentName, homeScore: 3, awayScore: 2,
    } }], upcomingGames: [],
    players: [{ participantId: 1, name: playerName, number: 9, goals: 3, assists: 2, points: 5 }],
    totalGoals: 3, totalAssists: 2, totalPoints: 5, powerPlayGoals: 1,
    shorthandedGoals: 0, penalties: 0, completeRosterAvailable: true,
  },
  '/api/games/123': {
    id: 123, number: '3555', date: '2026-11-08', time: '11:00', division: 'M13 A',
    isLive: false, isApproved: true,
    homeTeam: { id: 1, name: teamName, logoUrl: null },
    awayTeam: { id: 2, name: opponentName, logoUrl: null },
    teamStats: [
      { teamId: 1, goalsFor: 3, penaltyMinutes: 0, powerPlay: '0/0', penaltyKill: '0/0', winLossType: 'overtime' },
      { teamId: 2, goalsFor: 2, penaltyMinutes: 0, powerPlay: '0/0', penaltyKill: '0/0', winLossType: 'overtime' },
    ],
    periodGoals: [
      { teamId: 1, period: 1, goals: 1 }, { teamId: 2, period: 2, goals: 1 },
      { teamId: 1, period: 3, goals: 1 }, { teamId: 2, period: 4, goals: 1 },
      { teamId: 1, period: 5, goals: 1 },
    ],
    goals: [{ teamId: 1, period: 5, time: '2:30', scorer: { name: playerName, number: 9 },
      assists: [], isPowerPlay: false, isShorthanded: false, isEmptyNet: false, isPenaltyShot: false }],
    penalties: [],
  },
  '/api/weekly-report': {
    startDate: '2026-01-01', endDate: '2026-01-07', teamIds: [1],
    divisions: [{
      divisionId: 1, divisionName: 'Simple lettre', scheduleId: 1, scheduleName: 'Saison 1', leagueName: 'Ligue Exemple',
      standings: { available: true, data: [{ ...standing, team: { name: standing.teamName, division: standing.division } }] },
      games: { available: true, data: [{
        id: 1, date: '2026-01-03', startTime: '2026-01-03T12:00:00Z',
        homeTeamId: 1, awayTeamId: 2, homeTeam: { name: teamName },
        awayTeam: { name: opponentName }, score: { 1: 3, 2: 2 },
      }] },
      suspensions: { available: true, data: [{
        id: 'suspension-1', status: 'Active', participant: { fullName: playerName, number: 9 },
        team: { id: 1, name: teamName }, code: 'A01', requiredGames: 2, purgedGames: [1],
      }] },
    }],
  },
};

fixtures['/api/me'] = { sub: 'auth0|test', name: 'Test Hockey', email: 'test@example.com', pictureUrl: null };

/** Every played game of the fixture shares one box score shape, re-keyed on its id. */
for (const id of [11, 12, 13, 14, 15, 16, 17]) {
  fixtures[`/api/games/${id}`] = { ...fixtures['/api/games/123'], id };
}

function browserPath() {
  const paths = [
    process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH,
    process.env.CHROME_PATH,
    process.env.ProgramFiles && resolve(process.env.ProgramFiles, 'Google/Chrome/Application/chrome.exe'),
    process.env['ProgramFiles(x86)'] && resolve(process.env['ProgramFiles(x86)'], 'Microsoft/Edge/Application/msedge.exe'),
    process.env.ProgramFiles && resolve(process.env.ProgramFiles, 'Microsoft/Edge/Application/msedge.exe'),
    '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
    '/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge',
    '/usr/bin/google-chrome', '/usr/bin/google-chrome-stable', '/usr/bin/microsoft-edge',
    '/usr/bin/microsoft-edge-stable', '/usr/bin/chromium', '/usr/bin/chromium-browser',
  ];
  const executable = paths.find(path => path && existsSync(path));
  if (!executable) throw new Error('Chrome/Edge introuvable. Définir PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH.');
  return executable;
}

async function freePort() {
  const server = createServer();
  await new Promise((resolvePromise, reject) => {
    server.once('error', reject);
    server.listen(0, '127.0.0.1', resolvePromise);
  });
  const port = server.address().port;
  await new Promise((resolvePromise, reject) => server.close(error => error ? reject(error) : resolvePromise()));
  return port;
}

async function startServer(port) {
  const server = spawn(process.execPath, [
    resolve(root, 'node_modules/@angular/cli/bin/ng.js'),
    'serve', '--host', '127.0.0.1', '--port', String(port), '--no-open',
  ], { cwd: root, stdio: ['ignore', 'pipe', 'pipe'] });
  let output = '';
  let spawnError;
  server.on('error', error => { spawnError = error; });
  for (const stream of [server.stdout, server.stderr]) {
    stream.on('data', data => { output = (output + data.toString()).slice(-8000); });
  }
  const deadline = Date.now() + 120_000;
  try {
    while (Date.now() < deadline) {
      if (spawnError || server.exitCode !== null || server.signalCode !== null) {
        throw new Error(`ng serve arrêté: ${spawnError ?? server.exitCode ?? server.signalCode}\n${output}`);
      }
      try {
        const response = await fetch(`http://127.0.0.1:${port}/`, { signal: AbortSignal.timeout(1000) });
        if (response.ok) return server;
      } catch { /* The development server is still starting. */ }
      await sleep(500);
    }
    throw new Error(`Délai de démarrage ng serve dépassé.\n${output}`);
  } catch (error) {
    server.kill();
    throw error;
  }
}

async function check(label, action) {
  try {
    await action();
    console.log(`  ✓ ${label}`);
  } catch (error) {
    failures.push(`${label}: ${error.message}`);
    console.error(`  ✗ ${label}: ${error.message}`);
  }
}

async function visible(locator) {
  await locator.waitFor({ state: 'visible', timeout: 10000 });
}

/**
 * Presses the sign-in button until the app leaves the public landing page. The
 * SDK handles the redirect back with a one-shot authorization code kept in
 * memory: if the browser reloads the callback URL before that page finished its
 * exchange, the code is consumed and the landing page shows an error, so a
 * retry is the only way on.
 */
async function signIn(page) {
  const input = page.locator('#chat-input');
  const button = page.locator('.hero-actions .login-submit');
  for (let attempt = 0; attempt < 4; attempt++) {
    if (await input.isVisible()) return;
    try {
      // The landing page renders nothing while the SDK initializes: wait for
      // the button, not just for its visibility.
      await button.waitFor({ state: 'visible', timeout: 15000 });
      await button.click();
      await input.waitFor({ state: 'visible', timeout: 20000 });
      return;
    } catch { /* The exchange failed: press the button again. */ }
  }
  await input.waitFor({ state: 'visible', timeout: 15000 });
}

/**
 * Public landing page: hero, the three-free-preview gate over the anonymous
 * preview API, its counter in local storage, and the account benefits.
 */
async function inspectLanding(page, width) {
  await visible(page.locator('#landing-title'));
  await visible(page.getByText('président d’association', { exact: false }));

  await check(`${width}px accueil: sélection association → division → ligue → équipe`, async () => {
    const selects = page.locator('.preview .selector select');
    const count = await selects.count();
    if (count !== 4) {
      throw new Error(`quatre listes attendues (association, division, ligue, équipe), ${count} trouvée(s) — ${await page.locator('.preview .state').allTextContents()}`);
    }
    // A single association / division / league is preselected, cascading down
    // to the team list; the preview stays disabled until a team is chosen.
    await selects.nth(3).locator('option', { hasText: teamName }).waitFor({ state: 'attached', timeout: 10000 });
    const button = page.locator('.preview-actions .preview-button');
    if (!await button.isDisabled()) throw new Error('aperçu actif avant le choix d’une équipe');
    await selects.nth(3).selectOption({ label: teamName });
    await page.waitForFunction(() => {
      const action = document.querySelector('.preview-actions .preview-button');
      return action !== null && !action.disabled;
    }, null, { timeout: 5000 });
    await inspectLayout(page, 'accueil sélection', width);
  });

  await check(`${width}px accueil: aperçu gratuit compté dans le stockage local`, async () => {
    const badge = page.locator('.trial-badge');
    if (!/3/.test(await badge.textContent())) throw new Error(`compteur initial inattendu: ${await badge.textContent()}`);
    const button = page.locator('.preview-actions .preview-button');
    await button.click();
    // The shared statistics component renders the same figures as the team page.
    await visible(page.locator('app-team-stats app-team-summary'));
    await visible(page.locator('app-team-stats').getByText(playerName));
    const used = await page.evaluate(() => localStorage.getItem('hockey-season-preview-trials'));
    if (used !== '1') throw new Error(`compteur local inattendu: ${used}`);
    if (!/2/.test(await badge.textContent())) throw new Error(`compteur non décrémenté: ${await badge.textContent()}`);
    await inspectLayout(page, 'accueil aperçu', width);
  });

  await check(`${width}px accueil: revoir la même équipe ne consomme pas d’essai`, async () => {
    // Re-viewing the team already on screen costs no attempt.
    const button = page.locator('.preview-actions .preview-button');
    await button.waitFor({ state: 'visible' });
    if (await button.isDisabled()) {
      const state = await page.evaluate(() => ({
        values: Array.from(document.querySelectorAll('.preview .selector select')).map(select => select.value),
        text: document.querySelector('.preview-actions')?.textContent,
      }));
      throw new Error(`bouton d’aperçu désactivé après un aperçu: ${JSON.stringify(state)}`);
    }
    await button.click();
    const used = await page.evaluate(() => localStorage.getItem('hockey-season-preview-trials'));
    if (used !== '1') throw new Error(`compteur modifié sans nouvelle équipe: ${used}`);
  });

  await check(`${width}px accueil: l’aperçu masque les liens membres`, async () => {
    // Guest mode: opponents stay plain text and no box-score button is offered.
    if (await page.locator('app-team-stats .game-team.opponent').count() !== 0) {
      throw new Error('les liens adverses doivent être masqués sans compte');
    }
    if (await page.locator('app-team-stats .detail-button').count() !== 0) {
      throw new Error('le détail des matchs doit être masqué sans compte');
    }
  });

  await check(`${width}px accueil: essais épuisés → création de compte`, async () => {
    // Once the counter is exhausted the preview gives way to the free account;
    // reload so the page reads the stored counter.
    await page.evaluate(() => localStorage.setItem('hockey-season-preview-trials', '3'));
    await page.reload({ waitUntil: 'domcontentloaded' });
    await visible(page.locator('#landing-title'));
    const badge = page.locator('.trial-badge');
    if (!/épuisés|used up/.test(await badge.textContent())) {
      throw new Error(`compteur épuisé non signalé: ${await badge.textContent()}`);
    }
    if (await page.locator('.preview-actions .preview-button').count() !== 0) {
      throw new Error('l’aperçu doit disparaître une fois les essais épuisés');
    }
    await visible(page.locator('.limit-card .signup-button'));
    await visible(page.locator('.benefits .signup-button'));
    await visible(page.locator('.benefits h3', { hasText: 'Directeur de catégorie' }));
    await visible(page.locator('.benefits h3', { hasText: 'Président d’association' }));
    await inspectLayout(page, 'accueil essais épuisés', width);
  });
  console.log(`  ✓ ${width}px accueil public: aperçu, compteur et avantages`);
}

async function inspectLayout(page, label, width) {
  await check(`${width}px ${label}: débordement`, async () => {
    const sizes = await page.evaluate(() => ({
      scroll: document.documentElement.scrollWidth,
      client: document.documentElement.clientWidth,
    }));
    if (sizes.scroll > sizes.client) throw new Error(`document ${sizes.scroll}px > viewport ${sizes.client}px`);
  });
}

async function inspectNavigation(page, width, path) {
  await check(`${width}px ${path}: indicateur actif`, async () => {
    const active = page.locator('.main-nav a[aria-current="page"]');
    if (await active.count() !== 1 || await active.getAttribute('href') !== path) {
      throw new Error(`lien actif absent ou incorrect pour ${path}`);
    }
  });
  if (width > 900) return;
  await check(`${width}px ${path}: cibles tactiles navigation`, async () => {
    const links = page.locator('.main-nav a');
    if (await links.count() !== 4) throw new Error('quatre liens de navigation attendus');
    for (const link of await links.all()) {
      const box = await link.boundingBox();
      const href = await link.getAttribute('href');
      if (!box || box.width < 44 || box.height < 44 || box.x < 0 || box.x + box.width > width + 0.5) {
        throw new Error(`${href}: cible ${box ? `${box.width.toFixed(1)}×${box.height.toFixed(1)} à x=${box.x.toFixed(1)}` : 'invisible'} (minimum 44×44 et dans la fenêtre)`);
      }
    }
  });
}

async function inspectComposer(page, width, height, label) {
  await check(`${width}×${height} ${label}: composer visible et sans chevauchement de navigation`, async () => {
    const geometry = await page.evaluate(() => {
      const rect = element => {
        const r = element.getBoundingClientRect();
        return { top: r.top, bottom: r.bottom, left: r.left, right: r.right, width: r.width, height: r.height };
      };
      return {
        input: rect(document.querySelector('#chat-input')),
        send: rect(document.querySelector('.send-button')),
        nav: rect(document.querySelector('.main-nav')),
        conversation: rect(document.querySelector('.conversation')),
      };
    });
    if (width <= 900 && height <= 450 && geometry.conversation.height < 80) {
      throw new Error(`Conversation trop petite en paysage: ${geometry.conversation.height}px`);
    }
    for (const [name, box] of Object.entries({ input: geometry.input, send: geometry.send })) {
      if (!box.width || !box.height || box.top < 0 || box.bottom > height || box.left < 0 || box.right > width) {
        throw new Error(`${name} hors fenêtre: ${JSON.stringify(box)}`);
      }
      const nav = geometry.nav;
      if (box.left < nav.right && box.right > nav.left && box.top < nav.bottom && box.bottom > nav.top) {
        throw new Error(`${name} chevauche la navigation`);
      }
      if (width <= 900 && nav.top >= height / 2 && box.bottom > nav.top) {
        throw new Error(`${name} n'est pas au-dessus de la navigation mobile`);
      }
    }
  });
}

async function inspectChat(page, width, height, requestBodies) {
  const input = page.locator('#chat-input');
  await input.fill(chatQuestion);
  await input.press('Shift+Enter');
  await input.pressSequentially('Merci');
  if (await input.inputValue() !== `${chatQuestion}\nMerci` || requestBodies.length !== 0) {
    throw new Error('Shift+Enter doit insérer une nouvelle ligne sans envoyer');
  }
  await input.press('Enter');
  await visible(page.locator('.user-bubble').getByText(chatQuestion, { exact: false }));
  const card = page.locator('app-standing-table .standing-card');
  await visible(card);
  await visible(card.locator('tbody').getByText(teamName));
  if (requestBodies.length !== 1 || !requestBodies[0].messages?.some(message =>
    message.role === 'user' && message.content === `${chatQuestion}\nMerci`)) {
    throw new Error('Le POST AG-UI ne contient pas exactement le message envoyé');
  }
  if (await input.inputValue() !== '') throw new Error('Le composer ne se vide pas après Enter');
  console.log(`  ✓ ${width}px chat: Shift+Enter et envoi Enter avec réponse AG-UI`);

  const table = card.locator('table');
  const expectedCells = ['1', teamName, '12', '8–3–1', '17', '38', '24', '14', '26', '21.5%', '82.4%'];
  const actualCells = await table.locator('tbody tr').first().locator('th, td').allTextContents();
  if (JSON.stringify(actualCells.map(text => text.trim())) !== JSON.stringify(expectedCells)) {
    throw new Error(`Colonnes du classement incorrectes: ${JSON.stringify(actualCells)}`);
  }
  const headers = table.locator('thead th');
  if (await headers.count() !== 11) throw new Error('Onze colonnes attendues dans le tableau complet');
  for (let index = 0; index < 11; index++) {
    const shown = await headers.nth(index).isVisible();
    if (shown !== (width > 600 || index < 5)) {
      throw new Error(`Visibilité incorrecte de la colonne ${index + 1}`);
    }
  }
  await inspectLayout(page, 'chat classement compact', width);
  const details = card.locator('.standing-mobile-details');
  if (width <= 600) {
    if (await details.evaluate(element => element.open)) throw new Error('Détails mobiles ouverts initialement');
    await details.locator('summary').click();
    if (!await details.evaluate(element => element.open)) throw new Error('Détails mobiles non ouverts');
    const extra = details.locator('.standing-extra');
    await visible(extra.getByText(teamName));
    const values = (await extra.locator('dd').allTextContents()).map(text => text.trim());
    if (JSON.stringify(values) !== JSON.stringify(expectedCells.slice(5))) {
      throw new Error(`Statistiques détaillées incorrectes: ${JSON.stringify(values)}`);
    }
    await inspectLayout(page, 'chat détails ouverts', width);
  } else if (await details.isVisible()) {
    throw new Error('Les détails compacts ne doivent pas apparaître sur desktop');
  }
  console.log(`  ✓ ${width}px classement: onze colonnes conservées et statistiques ${width <= 600 ? 'détaillées' : 'visibles'}`);
  await inspectComposer(page, width, height, 'chat avec classement');

  const language = page.locator('.language-button');
  await language.click();
  await page.waitForFunction(() => document.documentElement.lang === 'en');
  await visible(card.getByText('Standings', { exact: true }));
  if ((await headers.nth(1).textContent()).trim() !== 'Team' ||
      (width <= 600 && (await details.locator('summary').textContent()).trim() !== 'View detailed statistics')) {
    throw new Error('Traduction anglaise du classement incorrecte');
  }
  await inspectLayout(page, 'chat en anglais', width);
  await language.click();
  await page.waitForFunction(() => document.documentElement.lang === 'fr');
  await visible(card.getByText('Classement', { exact: true }));
  if ((await headers.nth(1).textContent()).trim() !== 'Équipe') throw new Error('Retour en français incorrect');
  console.log(`  ✓ ${width}px bascule FR → EN → FR`);
}

async function inspectGame(page, width, requestBodies) {
  const input = page.locator('#chat-input');
  await input.fill(gameQuestion);
  await input.press('Enter');
  const card = page.locator('app-game-card .game-card');
  await visible(card);
  if (requestBodies.length !== 2 || !requestBodies[1].messages?.some(message =>
    message.role === 'user' && message.content === gameQuestion)) {
    throw new Error('Second échange AG-UI manquant ou invalide');
  }
  await inspectLayout(page, 'chat carte de match', width);
  await check(`${width}px match: noms complets accessibles`, async () => {
    const names = card.locator('.score-team .team-name, .periods-row:not(.periods-head) .periods-lead');
    const expected = [teamName, opponentName, teamName, opponentName];
    if (await names.count() !== expected.length) throw new Error('Quatre noms attendus');
    for (let index = 0; index < expected.length; index++) {
      const name = names.nth(index);
      if ((await name.textContent()).trim() !== expected[index]) throw new Error(`Nom ${index + 1} incorrect`);
      if (await name.getAttribute('title') !== expected[index]) throw new Error(`Nom complet ${index + 1} indisponible`);
      const info = await name.evaluate(element => {
        const style = getComputedStyle(element);
        return { width: element.clientWidth, scrollWidth: element.scrollWidth,
          height: element.clientHeight, scrollHeight: element.scrollHeight,
          textOverflow: style.textOverflow, overflow: style.overflow, whiteSpace: style.whiteSpace };
      });
      if (width <= 600 && index < 2 && (info.scrollWidth > info.width + 1 || info.scrollHeight > info.height + 1)) {
        throw new Error(`Nom ${index + 1} tronqué: ${JSON.stringify(info)}`);
      }
    }
  });
  await check(`${width}px match: cinq périodes accessibles`, async () => {
    const scroll = card.locator('.periods-scroll');
    const headers = scroll.locator('.periods-head .periods-cell.head');
    const values = (await headers.allTextContents()).map(text => text.trim());
    if (JSON.stringify(values) !== JSON.stringify(['P1', 'P2', 'P3', 'PROL', 'PROL'])) {
      throw new Error(`Périodes incorrectes: ${JSON.stringify(values)}`);
    }
    const rows = scroll.locator('.periods-row:not(.periods-head)');
    const scores = await rows.evaluateAll(elements => elements.map(row =>
      Array.from(row.querySelectorAll('.periods-cell:not(.total)'), cell => cell.textContent.trim())));
    if (JSON.stringify(scores) !== JSON.stringify([['1', '0', '1', '0', '1'], ['0', '1', '0', '1', '0']])) {
      throw new Error(`Pointages par période incorrects: ${JSON.stringify(scores)}`);
    }
    for (const header of await headers.all()) {
      await header.evaluate(element => {
        const container = element.closest('.periods-scroll');
        container.scrollLeft = element.offsetLeft - container.offsetLeft;
      });
      const accessible = await header.evaluate(element => {
        const viewport = element.closest('.periods-scroll').getBoundingClientRect();
        const cell = element.getBoundingClientRect();
        return cell.left >= viewport.left - 1 && cell.right <= viewport.right + 1;
      });
      if (!accessible) throw new Error(`Période inaccessible: ${await header.textContent()}`);
    }
  });
}

async function inspectTeamGames(page, width) {
  const block = page.locator('app-team-games');
  await visible(block);
  await check(`${width}px équipe: cinq derniers matchs puis pagination de cinq`, async () => {
    const rows = block.locator('.games-block').first().locator('.game-row');
    if (await rows.count() !== 5) throw new Error(`${await rows.count()} matchs affichés au lieu de 5`);
    // Most recent first: the 2026-02-05 game is the last played one.
    const dates = (await rows.locator('.game-date').allTextContents()).map(text => text.trim());
    if (dates[0] !== '2026-02-0520:00') throw new Error(`ordre des matchs incorrect: ${JSON.stringify(dates)}`);
    const more = block.locator('.more-button');
    if ((await more.textContent()).trim() !== 'Voir 2 matchs de plus') {
      throw new Error(`libellé de pagination inattendu: ${await more.textContent()}`);
    }
    await more.click();
    await page.waitForFunction(() =>
      document.querySelectorAll('app-team-games .games-block:first-of-type .game-row').length === 7,
      null, { timeout: 5000 });
    if (await more.count() !== 0) throw new Error('la pagination doit disparaître une fois tout affiché');
  });
  await check(`${width}px équipe: détail d’un match dans la page`, async () => {
    const row = block.locator('.games-block').first().locator('.game-row').first();
    const button = row.locator('.detail-button');
    if ((await button.textContent()).trim() !== 'Détail du match') throw new Error('bouton de détail absent');
    if (await row.locator('app-game-detail').count() !== 0) throw new Error('détail affiché avant le clic');
    await button.click();
    const card = row.locator('app-game-detail .game-card');
    await visible(card);
    await visible(card.getByText(playerName, { exact: false }));
    if ((await button.textContent()).trim() !== 'Masquer le détail') throw new Error('libellé du bouton non inversé');
    await inspectLayout(page, 'équipe détail ouvert', width);
    await button.click();
    await page.waitForFunction(() => !document.querySelector('app-team-games app-game-detail'),
      null, { timeout: 5000 });
  });
  await check(`${width}px équipe: deux prochains matchs avec lien adverse`, async () => {
    const next = block.locator('.games-block').nth(1);
    const rows = next.locator('.game-row');
    if (await rows.count() !== 2) throw new Error(`${await rows.count()} prochains matchs au lieu de 2`);
    const links = next.locator('.game-team.opponent');
    if (await links.count() !== 2) throw new Error('les deux adversaires doivent être liés');
    for (const link of await links.all()) {
      if ((await link.textContent()).trim() !== opponentName) throw new Error('nom de l’adversaire incorrect');
      if (await link.getAttribute('href') !== '/team?association=1&division=6193&league=1&team=2') {
        throw new Error(`lien adverse inattendu: ${await link.getAttribute('href')}`);
      }
    }
    await links.first().click();
    await page.waitForFunction(() => new URLSearchParams(location.search).get('team') === '2');
    await visible(page.locator('.team-summary h2').getByText(opponentName, { exact: false }));
    await check(`${width}px équipe: adversaire ouvert sans rechargement`, async () => {
      // The page keeps its own component: the selection is re-applied in place.
      if (await page.locator('.team-summary h2').getByText(teamName, { exact: true }).count() !== 0) {
        throw new Error('l’ancienne équipe est encore affichée');
      }
    });
    await page.goBack();
    await page.waitForFunction(() => new URLSearchParams(location.search).get('team') === '1');
    await visible(page.locator('.team-summary h2').getByText(teamName, { exact: false }));
  });
}

async function inspectSubscriptionSelector(page, width) {
  await check(`${width}px configuration: chemin association → division → ligue → équipe`, async () => {
    const selects = page.locator('.selector-card select');
    if (await selects.count() !== 4) throw new Error('quatre listes attendues (association, division, ligue, équipe)');
    // A single association is preselected by the page: its divisions load
    // immediately, the deeper levels stay blocked until the parent is picked.
    await selects.nth(0).locator('option', { hasText: 'Hockey Richelieu' }).waitFor({ state: 'attached', timeout: 10000 });
    if (!await selects.nth(3).isDisabled()) throw new Error('liste des équipes active avant la ligue');
    await selects.nth(1).locator('option', { hasText: 'Simple lettre' }).waitFor({ state: 'attached', timeout: 10000 });
    await selects.nth(1).selectOption({ label: 'Simple lettre' });
    await selects.nth(2).locator('option', { hasText: 'M13 B' }).waitFor({ state: 'attached', timeout: 10000 });
    if (!await selects.nth(3).isDisabled()) throw new Error('liste des équipes active avant la ligue');
    await selects.nth(2).selectOption({ label: 'M13 B' });
    await selects.nth(3).locator('option', { hasText: opponentName }).waitFor({ state: 'attached', timeout: 10000 });
    await selects.nth(3).selectOption({ label: opponentName });
    const add = page.locator('.add-button');
    if (await add.isDisabled()) throw new Error('bouton d’ajout désactivé malgré une équipe et un quota disponible');
    // The badge shows the licence consumption ("3 / 5" style).
    const badge = page.locator('.limit-badge');
    if (!/\d+ \/ (\d+|∞)/.test((await badge.textContent()).trim())) {
      throw new Error(`quota illisible: ${await badge.textContent()}`);
    }
    await inspectLayout(page, 'configuration équipe choisie', width);
  });
  await check(`${width}px configuration: cibles tactiles`, async () => {
    if (width > 900) return;
    for (const selector of await page.locator('.selector-card select, .add-button, .remove-button').all()) {
      const box = await selector.boundingBox();
      if (!box || box.height < 34) throw new Error(`cible trop petite: ${JSON.stringify(box)}`);
    }
  });
}

/**
 * Header user menu: the avatar trigger and its dropdown holding the logout
 * entry. Regression guard — a failed `GET /api/me` used to make the whole
 * dropdown (the only logout entry) disappear.
 */
async function inspectUserMenu(page, width) {
  await check(`${width}px menu utilisateur: avatar et déconnexion accessibles`, async () => {
    const trigger = page.locator('.user-menu-trigger');
    await visible(trigger);
    if (await trigger.getAttribute('aria-expanded') !== 'false') {
      throw new Error('menu utilisateur déjà ouvert');
    }
    await trigger.click();
    const logout = page.locator('.user-menu-item-danger');
    await visible(logout);
    if ((await logout.textContent()).trim().length === 0) throw new Error('action de déconnexion vide');
    await page.keyboard.press('Escape');
    await logout.waitFor({ state: 'hidden', timeout: 5000 });
  });
}

async function runViewport(browser, baseUrl, viewport) {
  const { width, height } = viewport;
  console.log(`\nViewport ${width}×${height}px`);
  const context = await browser.newContext({ viewport, isMobile: width <= 900, hasTouch: width <= 900 });
  const page = await context.newPage();
  const requestBodies = [];
  const authState = { nonce: '' };
  try {
    await context.route('**/*', async route => {
      const url = new URL(route.request().url());
      if (url.host === auth0Host) return mockAuth0(route, url, baseUrl, authState);
      if (url.origin !== baseUrl) return route.abort();
      if (!url.pathname.startsWith('/api/')) return route.continue();
      if (url.pathname === '/api/agui/hockey-season-data.agent') {
        if (route.request().method() !== 'POST') {
          failures.push(`${width}px: méthode AG-UI inattendue ${route.request().method()}`);
          return route.fulfill({ status: 405 });
        }
        const input = route.request().postDataJSON();
        requestBodies.push(input);
        const isGame = requestBodies.length === 2;
        const messageId = isGame ? 'msg2' : 'msg1';
        const toolCallId = isGame ? 'tool2' : 'tool1';
        const events = [
          { type: 'RUN_STARTED', threadId: input.threadId, runId: input.runId },
          { type: 'TEXT_MESSAGE_START', messageId, role: 'assistant' },
          { type: 'TOOL_CALL_START', toolCallId, toolCallName: isGame ? 'show_game_detail' : 'show_league_standing', parentMessageId: messageId },
          { type: 'TOOL_CALL_ARGS', toolCallId, delta: JSON.stringify(isGame ? { gameId: 123 } : { leagueName: 'Ligue de la Vallée du Richelieu', standings: [chatStanding] }) },
          { type: 'TOOL_CALL_END', toolCallId },
          { type: 'TEXT_MESSAGE_END', messageId },
          { type: 'TOOL_CALL_RESULT', messageId: `toolresult${requestBodies.length}`, toolCallId, content: 'Done' },
          { type: 'RUN_FINISHED', threadId: input.threadId, runId: input.runId },
        ];
        return route.fulfill({ status: 200, contentType: 'text/event-stream', body: events.map(event => `data: ${JSON.stringify(event)}\n\n`).join('') });
      }
      const body = fixtures[url.pathname];
      if (!body) {
        failures.push(`${width}px: API non simulée ${url.pathname}`);
        return route.fulfill({ status: 404, json: { error: 'Unmocked API' } });
      }
      return route.fulfill({ status: 200, json: body });
    });
    await page.goto(`${baseUrl}/chat`, { waitUntil: 'domcontentloaded' });
    // The public landing page is what a visitor sees first: the routing itself
    // happens once the SDK reports a session.
    await inspectLanding(page, width);
    // Auth0 Universal Login: the fake tenant redirects straight back; the
    // login was requested with appState.target '/chat', so the SDK lands there.
    await page.locator('.hero-actions .login-submit').click();
    await signIn(page);
    await inspectLayout(page, 'chat', width);
    await inspectNavigation(page, width, '/chat');
    await inspectUserMenu(page, width);
    await inspectComposer(page, width, height, 'chat initial');
    await inspectChat(page, width, height, requestBodies);
    await inspectGame(page, width, requestBodies);
    await page.locator('.main-nav a[href="/team"]').click();
    await visible(page.locator('.team-page'));
    // The follow list of the team page lists each followed team by name.
    await visible(page.locator('.follow-list').getByText(teamName));
    // Four selectors: association → division → league → team, limited to the follows.
    const selects = page.locator('.team-page select');
    await selects.nth(0).locator('option', { hasText: 'Hockey Richelieu' }).waitFor({ state: 'attached', timeout: 10000 });
    await selects.nth(0).selectOption({ label: 'Hockey Richelieu' });
    await selects.nth(1).locator('option', { hasText: 'Simple lettre' }).waitFor({ state: 'attached', timeout: 10000 });
    await selects.nth(1).selectOption({ label: 'Simple lettre' });
    await selects.nth(2).locator('option', { hasText: 'Ligue Exemple' }).waitFor({ state: 'attached', timeout: 10000 });
    await selects.nth(2).selectOption({ label: 'Ligue Exemple' });
    await selects.nth(3).locator('option', { hasText: teamName }).waitFor({ state: 'attached', timeout: 10000 });
    await selects.nth(3).selectOption({ label: teamName });
    await visible(page.locator('.team-summary'));
    await visible(page.getByText(playerName));
    await inspectTeamGames(page, width);
    await inspectLayout(page, 'team avec données', width);
    await inspectNavigation(page, width, '/team');
    await page.locator('.main-nav a[href="/teams-weekly-report"]').click();
    await visible(page.locator('.division-card'));
    await visible(page.locator('.games-list').getByText(opponentName));
    await visible(page.locator('.suspension-list').getByText(playerName, { exact: false }));
    await inspectLayout(page, 'report avec données', width);
    await inspectNavigation(page, width, '/teams-weekly-report');
    await page.locator('.main-nav a[href="/configuration"]').click();
    await visible(page.locator('#config-title'));
    await visible(page.locator('.followed-list').getByText(teamName));
    await inspectLayout(page, 'configuration', width);
    await inspectNavigation(page, width, '/configuration');
    await inspectSubscriptionSelector(page, width);
    await page.locator('.main-nav a[href="/chat"]').click();
    await visible(page.locator('#chat-input'));
    await inspectNavigation(page, width, '/chat');
  } catch (error) {
    failures.push(`${width}px parcours: ${error.message}`);
    console.error(`  ✗ ${width}px parcours: ${error.message}`);
  } finally {
    await context.close();
  }
}

let server;
let browser;
let interrupted = false;
const interrupt = () => { interrupted = true; server?.kill(); void browser?.close(); };
process.once('SIGINT', interrupt);
process.once('SIGTERM', interrupt);
try {
  const executablePath = browserPath();
  const port = await freePort();
  const baseUrl = `http://127.0.0.1:${port}`;
  server = await startServer(port);
  browser = await chromium.launch({ executablePath, headless: true });
  for (const viewport of viewports) {
    if (interrupted) break;
    await runViewport(browser, baseUrl, viewport);
  }
  if (interrupted) failures.push('Test interrompu');
} catch (error) {
  failures.push(`Initialisation: ${error.message}`);
} finally {
  await browser?.close().catch(() => {});
  if (server && server.exitCode === null && server.signalCode === null) {
    const exited = new Promise(resolvePromise => server.once('exit', resolvePromise));
    server.kill();
    await Promise.race([exited, sleep(5000)]);
  }
}
console.log(`\n${failures.length ? `${failures.length} échec(s)` : 'Tous les contrôles ont réussi'}`);
if (failures.length) {
  for (const failure of failures) console.error(`- ${failure}`);
  process.exitCode = 1;
}
