#!/usr/bin/env node
// Triker driver — drives the running API (HTTP/SignalR-backed REST) and the
// Blazor WASM frontend (Playwright + installed Chrome).
//
//   node .claude/skills/run-triker/driver.mjs api
//   node .claude/skills/run-triker/driver.mjs ui
//   node .claude/skills/run-triker/driver.mjs shot <route> [name]
//
// Requires both servers already running (see SKILL.md).
// The ASP.NET dev cert is untrusted in this container, so every HTTPS path
// here disables cert validation.

import { chromium } from 'playwright';
import * as signalR from '@microsoft/signalr';
import { mkdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = dirname(fileURLToPath(import.meta.url));
const SHOTS = join(HERE, 'shots');
const API = process.env.TRIKER_API ?? 'https://localhost:7077';
// index.html hardcodes <base href="/triker/">, so the dev server must be run
// with --pathbase=/triker and the app lives under that path. See SKILL.md.
const WEB = (process.env.TRIKER_WEB ?? 'https://localhost:7164/triker').replace(/\/$/, '');

// Node's fetch rejects the untrusted ASP.NET dev cert; this is a local-only driver.
process.env.NODE_TLS_REJECT_UNAUTHORIZED = '0';

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const log = (...a) => console.log(...a);

async function api(method, path, body) {
  const res = await fetch(`${API}${path}`, {
    method,
    headers: body === undefined ? {} : { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await res.text();
  if (!res.ok) throw new Error(`${method} ${path} -> ${res.status} ${text}`);
  return text ? JSON.parse(text) : null;
}

// ---------------------------------------------------------------- api smoke

// Connect to /gamehub as a real player. This is mandatory, not optional:
// - the "HandsRevealed" payload is sent ONLY to the SignalR group named after
//   the gameId, which you join by connecting with ?gameId=&playerId=;
// - GameBroadcaster.BroadcastPersonalized only pushes to players that have a
//   ConnectionId, which is registered in GameHub.OnConnectedAsync.
// A REST-only client never sees round results.
async function connectHub(gameId, playerId, sink) {
  const conn = new signalR.HubConnectionBuilder()
    .withUrl(`${API}/gamehub?gameId=${gameId}&playerId=${playerId}`, {
      // Node has no browser TLS store; force ws transport with lax certs.
      withCredentials: false,
    })
    .build();
  conn.on('GameState', (dto) => sink.states.push(dto));
  conn.on('HandsRevealed', (results) => sink.revealed.push(results));
  conn.on('GamesUpdated', (games) => sink.lobby.push(games));
  conn.on('Reaction', (r) => sink.reactions.push(r));
  await conn.start();
  return conn;
}

async function waitFor(pred, label, ms = 30_000) {
  const t0 = Date.now();
  while (Date.now() - t0 < ms) {
    const v = pred();
    if (v) return v;
    await sleep(250);
  }
  throw new Error(`timed out waiting for ${label}`);
}

async function runApi() {
  log('== API smoke ==');

  const created = await api('POST', '/api/games', {
    playerName: 'DriverAna',
    gameName: `driver-${Date.now()}`,
    targetWins: 3,
    isPrivate: false,
  });
  const gameId = created.gameState.id;
  const me = created.playerId;
  log(`created game ${gameId} as ${me}`);

  const sink = { states: [], revealed: [], lobby: [], reactions: [] };
  const hub = await connectHub(gameId, me, sink);
  log('hub connected');

  await api('POST', `/api/games/${gameId}/addbot?playerId=${me}`);
  await api('POST', `/api/games/${gameId}/addbot?playerId=${me}`);
  log('added 2 bots');

  await api('POST', `/api/games/${gameId}/start?playerId=${me}`);
  let state = await api('GET', `/api/games/${gameId}?playerId=${me}`);
  const mine = state.players.find((p) => p.id === me);
  log(`status=${state.status} round=${state.roundNumber} deck=${state.deckSize} hand=${mine.hand.length}`);
  log(`my current play: ${mine.play ? `${mine.play.handName} [${mine.play.handRank}] ${mine.play.totalScore} pts` : '—'}`);

  // Bots are auto-played by InactivityService before /start returns, so it is
  // our turn by now. Discard the 2 first cards.
  if (mine.hand.length !== 5) throw new Error(`expected 5 cards, got ${mine.hand.length}`);
  const toss = mine.hand.slice(0, 2).map((c) => c.id);
  await api('POST', `/api/games/${gameId}/discard?playerId=${me}`, { cardIds: toss });
  log('discarded 2 cards');

  const results = await waitFor(() => sink.revealed[0], 'HandsRevealed over SignalR');
  log('hands revealed (via SignalR):');
  // HandResultDto.rank is the 1-based placement; handRank is the HandRank enum
  // (string, thanks to the server's JsonStringEnumConverter); handName is the
  // Spanish label the UI shows.
  for (const r of results)
    log(`  #${r.rank} ${String(r.playerName).padEnd(12)} ${String(r.handName).padEnd(22)} [${r.handRank}] ${r.totalScore} pts`);
  if (results.length !== 3) throw new Error(`expected 3 results, got ${results.length}`);

  // Winner is decided purely by HandRank order, not by TotalScore.
  const winner = results.find((r) => r.rank === 1);
  log(`winner: ${winner ? winner.playerName : '(none)'}`);

  await api('POST', `/api/games/${gameId}/react?playerId=${me}`, { emoji: '🎉' });
  await waitFor(() => sink.reactions[0], 'Reaction echo');
  log(`reaction echoed: ${sink.reactions[0].emoji}`);

  await api('POST', `/api/games/${gameId}/continue?playerId=${me}`, { wantsToContinue: true });
  const next = await waitFor(
    () => sink.states.find((s) => s.roundNumber === 2 && s.status === 'Playing'),
    'round 2 to start',
  );
  log(`round ${next.roundNumber} started, status=${next.status}`);

  const listed = await api('GET', '/api/games');
  log(`lobby lists ${listed.length} game(s)`);

  await hub.stop();
  log('== API smoke OK ==');
  return { gameId, playerId: me };
}

// ----------------------------------------------------------------- ui smoke

async function withBrowser(fn) {
  mkdirSync(SHOTS, { recursive: true });
  const browser = await chromium.launch({
    channel: 'chrome', // use the machine's Chrome; no playwright browser download
    headless: true,
  });
  const ctx = await browser.newContext({
    ignoreHTTPSErrors: true, // ASP.NET dev cert is untrusted here
    viewport: { width: 1280, height: 900 },
  });
  const page = await ctx.newPage();
  page.on('console', (m) => {
    if (m.type() === 'error') console.log(`  [console.error] ${m.text()}`);
  });
  page.on('pageerror', (e) => console.log(`  [pageerror] ${e.message}`));
  try {
    return await fn(page);
  } finally {
    await browser.close();
  }
}

// Cards are dealt with a staggered CSS animation; HandView strips the
// `dealing` class once the last one lands. Screenshotting before that catches
// a hand with 4 invisible cards, even though all 5 are already in the DOM.
async function waitDealt(page) {
  await page.waitForFunction(
    () =>
      document.querySelectorAll('.hand-cards .card').length === 5 &&
      document.querySelectorAll('.hand-cards .card.dealing').length === 0,
    null,
    { timeout: 30_000 },
  );
}

async function shot(page, name) {
  const p = join(SHOTS, `${name}.png`);
  await page.screenshot({ path: p, fullPage: true });
  log(`  shot -> ${p}`);
}

async function runUi() {
  log('== UI smoke ==');
  await withBrowser(async (page) => {
    await page.goto(WEB, { waitUntil: 'networkidle' });
    // Blazor WASM boots after the .NET runtime downloads; wait for real content.
    await page.waitForSelector('.game-title', { timeout: 60_000 });
    log('lobby loaded');
    await shot(page, '01-lobby');

    await page.fill('.name-input', 'DriverAna');
    await page.click('.lobby-actions .btn-primary'); // + Nueva partida
    await page.waitForSelector('.create-form');
    await page.fill('.create-form .name-input', `ui-${Date.now()}`);
    await page.click('.create-form .btn-primary'); // Crear

    // Navigates to /game/{id}?playerId={pid}
    await page.waitForURL(/\/game\//, { timeout: 20_000 });
    await page.waitForSelector('.waiting-room');
    log(`in waiting room: ${page.url()}`);
    await shot(page, '02-waiting-room');

    await page.click('.waiting-actions .btn-secondary'); // + Añadir bot
    await page.click('.waiting-actions .btn-secondary');
    await page.waitForFunction(() => document.querySelectorAll('.waiting-player').length === 3);
    log('2 bots added');

    await page.click('.btn-start');
    await page.waitForSelector('.hand-cards .card', { timeout: 30_000 });
    await waitDealt(page);
    const cards = await page.locator('.hand-cards .card').count();
    log(`game started, ${cards} cards in hand`);
    await shot(page, '03-hand-dealt');

    // Wait for our turn (bots move first depending on seat order).
    await page.waitForSelector('.turn-indicator.my-turn', { timeout: 60_000 });
    await page.locator('.hand-cards .card').nth(0).click();
    await page.locator('.hand-cards .card').nth(1).click();
    await page.waitForSelector('.hand-cards .card.selected');
    await shot(page, '04-cards-selected');

    await page.click('.btn-discard');
    log('discard submitted');

    // The results screen only appears when the HandsRevealed SignalR message
    // arrives. Do NOT wait on .my-play-badge — that badge is visible during the
    // whole Playing phase and matches long before the round ends.
    await page.waitForSelector('.results-view .result-entry', { timeout: 90_000 });
    const entries = await page.locator('.result-entry').allInnerTexts();
    log(`results screen with ${entries.length} entries:`);
    for (const e of entries) log(`  ${e.replace(/\s+/g, ' ').slice(0, 90)}`);
    if (entries.length !== 3) throw new Error(`expected 3 result entries, got ${entries.length}`);
    await shot(page, '05-results');

    // Round 2: press "Próxima partida" and confirm fresh cards are dealt.
    await page.click('.continue-buttons .btn-primary');
    await page.waitForSelector('.round-badge:has-text("Ronda 2")', { timeout: 60_000 });
    // The badge flips before the deal animation finishes.
    await waitDealt(page);
    log('round 2 started, 5 cards redealt');
    await shot(page, '06-round-2');

    log('== UI smoke OK ==');
  });
}

async function runShot(route = '', name = 'shot') {
  // Pass routes WITHOUT a leading slash ("game/<id>?playerId=<pid>"): Git Bash
  // rewrites a bare "/..." argument into an MSYS filesystem path. Use "." for
  // the lobby — PowerShell 5.1 drops an empty "" argument to a native exe.
  const path = !route || route === '.' ? '/' : `/${route.replace(/^\/+/, '')}`;
  await withBrowser(async (page) => {
    await page.goto(`${WEB}${path}`, { waitUntil: 'networkidle' });
    await page.waitForSelector('.game-title, .game-room', { timeout: 60_000 });
    await shot(page, name);
  });
}

// --------------------------------------------------------------------- main

const [cmd, ...rest] = process.argv.slice(2);
try {
  if (cmd === 'api') await runApi();
  else if (cmd === 'ui') await runUi();
  else if (cmd === 'shot') await runShot(rest[0], rest[1]);
  else {
    console.error('usage: driver.mjs <api|ui|shot [route] [name]>');
    process.exit(2);
  }
} catch (e) {
  console.error(`FAILED: ${e.message}`);
  process.exit(1);
}
