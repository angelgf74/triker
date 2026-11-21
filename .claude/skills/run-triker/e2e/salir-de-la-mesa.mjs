// Comprobación en la app real de «Salir de la mesa». Solo contra localhost.
import { join } from 'node:path';
import { chromium, WEB, API, MOVIL, SHOTS, ok, sleep, fallar, terminar, soloLocal } from './lib/comun.mjs';

soloLocal();

async function jugador(browser, nombre) {
  const ctx = await browser.newContext({ ...MOVIL });
  const page = await ctx.newPage();
  page.on('pageerror', (e) => { if (!/ServiceWorker/.test(e.message)) console.log(`  [${nombre} pageerror] ${e.message.slice(0, 160)}`); });
  await page.goto(WEB + '/', { waitUntil: 'load' });
  await page.waitForSelector('.game-title', { timeout: 90_000 });
  await page.fill('#nombre-jugador', nombre);
  return page;
}
async function crear(page, partida) {
  await page.click('.lobby-actions .btn-new-game');
  await page.fill('.create-form .name-input', partida);
  await page.click('.create-form .btn-primary');
  await page.waitForSelector('.waiting-room');
}
async function unirse(page, partida) {
  await page.locator(`.game-card:has-text("${partida}") .btn-join`).click();
  await page.waitForSelector('.waiting-room');
}
const shot = (page, name) => page.screenshot({ path: join(SHOTS, `${name}.png`) });
const enLobby = (page) => page.waitForSelector('.lobby .game-title', { timeout: 10_000 }).then(() => true, () => false);

const browser = await chromium.launch({ channel: 'chrome', headless: true });
try {
  // ── A. Sala de espera ───────────────────────────────────────────────────
  console.log('== A. Salir de la sala de espera ==');
  const ana = await jugador(browser, 'Ana');
  const berto = await jugador(browser, 'Berto');
  await crear(ana, 'Sala A');
  await unirse(berto, 'Sala A');
  await ana.waitForFunction(() => document.querySelectorAll('.waiting-player').length === 2);
  await shot(berto, 's01-sala-con-salir');
  ok(await berto.locator('.btn-leave').count() === 1, 'el invitado ve el botón «Salir» en la sala de espera');
  await berto.click('.btn-leave', { timeout: 5000 }).catch(() => {});
  ok(await enLobby(berto), 'el invitado vuelve al lobby al salir');
  ok(await ana.waitForFunction(() => document.querySelectorAll('.waiting-player').length === 1, null, { timeout: 5000 }).then(() => true, () => false),
     'la creadora deja de verle en la sala');
  await ana.click('.btn-leave', { timeout: 5000 }).catch(() => {});
  ok(await enLobby(ana), 'la creadora vuelve al lobby al salir');
  await sleep(800);
  ok(await berto.locator('.game-card:has-text("Sala A")').count() === 0, 'la sala sin humanos desaparece del lobby');

  // ── B. A mitad de ronda ─────────────────────────────────────────────────
  console.log('== B. Salir a mitad de ronda ==');
  await crear(ana, 'Mesa B');
  const urlAna = ana.url();
  await unirse(berto, 'Mesa B');
  await ana.waitForFunction(() => document.querySelectorAll('.waiting-player').length === 2);
  await ana.click('.waiting-actions .btn-secondary'); // + bot
  await ana.waitForFunction(() => document.querySelectorAll('.waiting-player').length === 3);
  await ana.click('.btn-start');
  await ana.waitForSelector('.turn-indicator.my-turn', { timeout: 30_000 });
  await shot(ana, 's02-en-juego-con-salir');
  const titulo = await ana.locator('.game-title-small').evaluate((e) => ({ visible: e.clientWidth, total: e.scrollWidth, texto: e.textContent }));
  console.log(`  título en cabecera (360 px): ${JSON.stringify(titulo)}; scroll horizontal: ${await ana.evaluate(() => document.documentElement.scrollWidth - innerWidth)} px`);
  await ana.click('.btn-leave', { timeout: 5000 }).catch(() => {});
  ok(await ana.locator('.leave-confirm').count() === 1, 'en juego pide confirmación antes de salir');
  await shot(ana, 's03-confirmar-salida');
  await ana.click('.leave-confirm .btn-secondary', { timeout: 5000 }).catch(() => {});
  ok(await ana.locator('.leave-confirm').count() === 0 && await ana.locator('.turn-indicator.my-turn').count() === 1, '«Seguir jugando» cierra la confirmación y sigue siendo su turno');
  await ana.click('.btn-leave', { timeout: 5000 }).catch(() => {});
  const t0 = Date.now();
  await ana.click('.leave-confirm .btn-danger', { timeout: 5000 }).catch(() => {});
  ok(await enLobby(ana), `quien sale vuelve al lobby (${Date.now() - t0} ms, sin esperar a los bots)`);
  ok(await berto.waitForSelector('.turn-indicator.my-turn', { timeout: 5000 }).then(() => true, () => false), 'al otro jugador le toca al momento, sin esperar 60 s');
  ok(await berto.locator('.other-player.has-left:has-text("Ana")').count() === 1, 'la mesa marca a Ana como «salió»');
  await shot(berto, 's04-rival-salio');
  await berto.click('.btn-pass');
  await berto.waitForSelector('.results-view .result-entry', { timeout: 30_000 });
  const nombres = await berto.locator('.result-player-name').allInnerTexts();
  ok(nombres.length === 2 && !nombres.some((n) => n.includes('Ana')), `los resultados no cuentan su mano: ${JSON.stringify(nombres)}`);
  await ana.goto(urlAna, { waitUntil: 'load' });
  await ana.waitForSelector('.room-problem', { timeout: 90_000 });
  ok(/No formas parte|ya no existe|Ya no estás/.test(await ana.locator('.room-problem-title').innerText()), 'su enlace antiguo ya no le sienta en la mesa');

  // ── C. La API exige el token ────────────────────────────────────────────
  console.log('== C. API ==');
  const c = await (await fetch(`${API}/api/games`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ playerName: 'Carla', gameName: 'Mesa C' }) })).json();
  const leave = (token) => fetch(`${API}/api/games/${c.gameState.id}/leave?playerId=${c.playerId}`, { method: 'POST', headers: token ? { 'X-Triker-Token': token } : {} }).then((r) => r.status);
  ok(await leave(null) === 404, 'salir sin token: 404');
  ok(await leave('inventado') === 404, 'salir con token ajeno: 404');
  ok(await leave(c.playerToken) === 200, 'salir con su token: 200');
  ok(await leave(c.playerToken) === 404, 'salir dos veces: 404 (la partida ya no existe)');
} catch (e) {
  fallar(e);
} finally {
  await browser.close();
}
terminar();
