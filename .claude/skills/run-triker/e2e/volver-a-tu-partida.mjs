// Comprobación en la app real de «Volver a tu partida» en el lobby. Solo contra localhost.
import { join } from 'node:path';
import { chromium, WEB, MOVIL, SHOTS, ok, espera, sleep, fallar, terminar, soloLocal } from './lib/comun.mjs';

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
// Como quien cierra la app y la vuelve a abrir: carga el lobby desde cero.
async function alLobby(page) {
  await page.goto(WEB + '/', { waitUntil: 'load' });
  await page.waitForSelector('.lobby .game-title', { timeout: 90_000 });
  await page.waitForSelector('.loading', { state: 'detached', timeout: 10_000 }).catch(() => {});
}
const shot = (page, name) => page.screenshot({ path: join(SHOTS, `${name}.png`), fullPage: true });
const aviso = (page, partida) => page.locator(`.resume-banner:has-text("${partida}")`);
const tarjeta = (page, partida) => page.locator(`.game-card:has-text("${partida}")`);
const asientos = (page) => page.evaluate(() => JSON.parse(localStorage.getItem('triker_asientos') || '[]'));

// Nombre distinto en cada pasada: las salas de pasadas anteriores siguen abiertas.
const MESA = 'Mesa V' + (Date.now() % 100000);
const browser = await chromium.launch({ channel: 'chrome', headless: true });
try {
  const ana = await jugador(browser, 'Ana');
  const bea = await jugador(browser, 'Bea');

  console.log('== A. Sala de espera ==');
  await ana.click('.lobby-actions .btn-new-game');
  await ana.fill('.create-form .name-input', MESA);
  await ana.click('.create-form .btn-primary');
  await ana.waitForSelector('.waiting-room');
  await alLobby(ana);
  ok(await espera(aviso(ana, MESA).waitFor({ timeout: 5000 })), `el lobby avisa de que sigues en «${MESA}»`);
  await shot(ana, 'v01-lobby-con-aviso');
  ok(await tarjeta(ana, MESA).locator('button:has-text("Unirse")').count() === 0, 'tu propia sala no ofrece «Unirse»');
  ok(await tarjeta(ana, MESA).locator('.btn-resume').count() === 1, 'la tarjeta de tu sala ofrece «Volver»');
  const scroll = await ana.evaluate(() => document.documentElement.scrollWidth - innerWidth);
  ok(scroll <= 0, `sin scroll horizontal a 360 px (${scroll} px)`);

  await alLobby(bea);
  ok(await aviso(bea, MESA).count() === 0, 'quien no está en la partida no ve el aviso');
  ok(await tarjeta(bea, MESA).locator('button:has-text("Unirse")').count() === 1, 'y sigue viendo «Unirse»');

  await aviso(ana, MESA).locator('button').click({ timeout: 5000 }).catch(() => {});
  ok(await espera(ana.waitForSelector('.waiting-room', { timeout: 10_000 })), 'el aviso te devuelve a la sala de espera');
  ok(await ana.locator('.waiting-player').count() === 1, 'vuelves a tu asiento, no a uno nuevo (1 jugador en la sala)');
  ok(!/token/i.test(ana.url()), 'el token no viaja en la URL');

  console.log('== B. En juego ==');
  await ana.click('.waiting-actions .btn-secondary'); // + bot
  await ana.waitForFunction(() => document.querySelectorAll('.waiting-player').length === 2);
  await ana.click('.btn-start');
  await ana.waitForSelector('.turn-indicator.my-turn', { timeout: 30_000 });
  await alLobby(ana);
  ok(await espera(aviso(ana, MESA).waitFor({ timeout: 5000 })), 'en juego el lobby también avisa');
  ok(await tarjeta(ana, MESA).locator('.btn-spectate').count() === 0, 'tu partida en juego no ofrece «Ver»');
  await tarjeta(ana, MESA).locator('.btn-resume').click({ timeout: 5000 }).catch(() => {});
  ok(await espera(ana.waitForSelector('.turn-indicator.my-turn', { timeout: 10_000 })), '«Volver» en la tarjeta te sienta en tu turno');

  console.log('== C. Entre rondas (la partida no sale en la lista) ==');
  await ana.click('.btn-pass');
  await ana.waitForSelector('.results-view .result-entry', { timeout: 30_000 });
  await alLobby(ana);
  ok(await tarjeta(ana, MESA).count() === 0, '(la partida entre rondas no está en la lista)');
  ok(await espera(aviso(ana, MESA).waitFor({ timeout: 5000 })), 'aun así el lobby avisa y deja volver');
  await shot(ana, 'v02-lobby-entre-rondas');
  await aviso(ana, MESA).locator('button').click({ timeout: 5000 }).catch(() => {});
  ok(await espera(ana.waitForSelector('.results-view .result-entry', { timeout: 10_000 })), 'y vuelves a la pantalla de resultados');

  console.log('== D. Asientos que ya no valen ==');
  await ana.click('.results-view button:has-text("Salir de la mesa")', { timeout: 5000 }).catch(() => {});
  await ana.waitForSelector('.lobby .game-title', { timeout: 10_000 });
  await sleep(500);
  ok(await ana.locator('.resume-banner').count() === 0, 'tras salir de la mesa el lobby ya no avisa');

  await ana.evaluate(() => localStorage.setItem('triker_asientos', JSON.stringify([
    { GameId: crypto.randomUUID(), Id: crypto.randomUUID(), Token: 'caducado', Espectador: false },
  ])));
  await alLobby(ana);
  await sleep(1500);
  ok(await ana.locator('.resume-banner').count() === 0, 'un asiento de una partida que ya no existe no genera aviso');
  ok((await asientos(ana)).length === 0, 'y se borra de localStorage');
} catch (e) {
  fallar(e);
} finally {
  await browser.close();
}
terminar();
