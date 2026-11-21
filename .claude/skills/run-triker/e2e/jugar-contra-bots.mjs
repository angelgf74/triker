// Comprobación en la app real de «Jugar contra bots» en un toque. Solo contra localhost.
import { join } from 'node:path';
import { chromium, WEB, MOVIL, SHOTS, ok, espera, fallar, terminar, soloLocal } from './lib/comun.mjs';

soloLocal();

const browser = await chromium.launch({ channel: 'chrome', headless: true });
try {
  const ctx = await browser.newContext({ ...MOVIL });
  const page = await ctx.newPage();
  page.on('pageerror', (e) => { if (!/ServiceWorker/.test(e.message)) console.log(`  [pageerror] ${e.message.slice(0, 160)}`); });
  const peticiones = [];
  page.on('request', (r) => { if (r.url().includes('/api/games') && r.method() === 'POST') peticiones.push(r.url().replace(/.*\/api\/games/, '') || '/'); });
  await page.goto(WEB + '/', { waitUntil: 'load' });
  await page.waitForSelector('.lobby .game-title', { timeout: 90_000 });

  console.log('== A. Sin nombre ==');
  ok(await page.locator('.btn-play-now').count() === 1, 'el lobby ofrece jugar contra bots');
  await page.click('.btn-play-now', { timeout: 3000 }).catch(() => {});
  ok(await espera(page.waitForSelector('#nombre-aviso', { timeout: 3000 })), 'sin nombre lo pide en vez de fallar');
  ok(!page.url().includes('/game/'), 'y no crea ninguna partida');
  const scroll = await page.evaluate(() => document.documentElement.scrollWidth - innerWidth);
  ok(scroll <= 0, `sin scroll horizontal a 360 px (${scroll} px)`);
  await page.screenshot({ path: join(SHOTS, 'j01-lobby.png'), fullPage: true });

  console.log('== B. Con nombre: un toque ==');
  await page.fill('#nombre-jugador', 'Ana');
  const t0 = Date.now();
  await page.click('.btn-play-now', { timeout: 3000 }).catch(() => {});
  ok(await espera(page.waitForSelector('.turn-indicator.my-turn', { timeout: 15_000 })), `un toque y ya es tu turno (${Date.now() - t0} ms)`);
  ok(await page.locator('.waiting-room').count() === 0, 'sin pasar por la sala de espera');
  ok(await page.locator('.other-player').count() === 3, `con 3 bots en la mesa (${await page.locator('.other-player').count()})`);
  ok(await page.locator('.hand-cards .card').count() === 5, 'con tus 5 cartas repartidas');
  ok(await espera(page.waitForSelector('.countdown-fill', { timeout: 3000 })), 'y el reloj del turno en marcha');
  ok(peticiones.length === 1, `con una sola petición de creación: ${JSON.stringify(peticiones)}`);
  await page.screenshot({ path: join(SHOTS, 'j02-mesa.png'), fullPage: true });

  console.log('== C. La ronda se juega entera ==');
  await page.click('.btn-pass');
  ok(await espera(page.waitForSelector('.results-view .result-entry', { timeout: 40_000 })), 'los bots juegan y salen los resultados');
  ok(await page.locator('.results-view .result-entry').count() === 4, 'con los cuatro jugadores');

  console.log('== D. Crear una partida normal sigue igual ==');
  const otra = await ctx.newPage();
  await otra.goto(WEB + '/', { waitUntil: 'load' });
  await otra.waitForSelector('.lobby .game-title', { timeout: 90_000 });
  await otra.click('.btn-new-game', { timeout: 3000 }).catch(() => {});
  ok(await espera(otra.waitForSelector('.create-form', { timeout: 3000 })), '«+ Nueva partida» abre su formulario');
  await otra.click('.create-form .btn-primary');
  ok(await espera(otra.waitForSelector('.waiting-room', { timeout: 10_000 })), 'y lleva a la sala de espera');
  await otra.click('.btn-leave').catch(() => {});
} catch (e) {
  fallar(e);
} finally {
  await browser.close();
}
terminar();
