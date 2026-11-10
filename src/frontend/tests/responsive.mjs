import { chromium } from 'playwright-core';
import { spawn } from 'node:child_process';
import { existsSync } from 'node:fs';
import { createServer } from 'node:net';
import { setTimeout as sleep } from 'node:timers/promises';
import { fileURLToPath } from 'node:url';
import { resolve } from 'node:path';

const root = fileURLToPath(new URL('..', import.meta.url));
const viewports = [
  { width: 320, height: 568 }, { width: 375, height: 667 },
  { width: 390, height: 844 }, { width: 430, height: 932 },
  { width: 568, height: 320 }, { width: 768, height: 1024 }, { width: 1280, height: 800 },
];
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
const fixtures = {
  '/api/auth/login': { success: true, accessToken: 'local-test-token', expiresInSeconds: 3600, tokenType: 'Bearer' },
  '/api/offices': [{ id: 1, name: 'Association Exemple' }],
  '/api/offices/1/leagues': [{ id: 1, name: 'Ligue Exemple' }],
  '/api/leagues/1/teams': [{ id: 1, name: teamName }],
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
      divisionId: 1, leagueName: 'Ligue Exemple',
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

async function runViewport(browser, baseUrl, viewport) {
  const { width, height } = viewport;
  console.log(`\nViewport ${width}×${height}px`);
  const context = await browser.newContext({ viewport, isMobile: width <= 900, hasTouch: width <= 900 });
  const page = await context.newPage();
  const requestBodies = [];
  try {
    await context.route('**/*', async route => {
      const url = new URL(route.request().url());
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
    await visible(page.locator('#access-code'));
    await inspectLayout(page, 'login', width);
    await page.locator('#access-code').fill('123456');
    await page.locator('.login-submit').click();
    await visible(page.locator('#chat-input'));
    await inspectLayout(page, 'chat', width);
    await inspectNavigation(page, width, '/chat');
    await inspectComposer(page, width, height, 'chat initial');
    await inspectChat(page, width, height, requestBodies);
    await inspectGame(page, width, requestBodies);
    await page.locator('.main-nav a[href="/team"]').click();
    await visible(page.locator('.team-page'));
    const selects = page.locator('.team-page select');
    await selects.nth(0).locator('option', { hasText: 'Association Exemple' }).waitFor({ state: 'attached', timeout: 10000 });
    await selects.nth(0).selectOption({ label: 'Association Exemple' });
    await selects.nth(1).locator('option', { hasText: 'Ligue Exemple' }).waitFor({ state: 'attached', timeout: 10000 });
    await selects.nth(1).selectOption({ label: 'Ligue Exemple' });
    await selects.nth(2).locator('option', { hasText: teamName }).waitFor({ state: 'attached', timeout: 10000 });
    await selects.nth(2).selectOption({ label: teamName });
    await visible(page.locator('.team-summary'));
    await visible(page.getByText(playerName));
    await visible(page.locator('.team-summary .games').getByText(opponentName, { exact: false }));
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
    await inspectLayout(page, 'configuration', width);
    await inspectNavigation(page, width, '/configuration');
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
