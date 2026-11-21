// Comprobación en la app real de las salas de espera sin nadie conectado.
// Solo contra localhost. Tarda unos 4 min: el margen para dejar de listar una
// sala es de 2 min y la limpieza pasa cada minuto.
import { join } from 'node:path';
import { chromium, WEB, API, MOVIL, SHOTS, ok, espera, fallar, terminar, soloLocal } from './lib/comun.mjs';

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
  return page.url().match(/game\/([0-9a-f-]{36})/)[1];
}
const shot = (page, name) => page.screenshot({ path: join(SHOTS, `${name}.png`), fullPage: true });
const tarjeta = (page, partida) => page.locator(`.game-card:has-text("${partida}")`);
const resumen = async (id) => (await (await fetch(`${API}/api/games`)).json()).find((g) => g.id === id);

const n = Date.now() % 100000;
const SALA_H = `Sala H${n}`, SALA_I = `Sala I${n}`;
const browser = await chromium.launch({ channel: 'chrome', headless: true });
try {
  const ana = await jugador(browser, 'Ana');
  const dani = await jugador(browser, 'Dani');
  const bea = await jugador(browser, 'Bea');     // se queda mirando el lobby, sin recargar

  console.log('== A. Las salas se listan mientras hay alguien dentro ==');
  const idH = await crear(ana, SALA_H);
  const idI = await crear(dani, SALA_I);
  ok(await espera(tarjeta(bea, SALA_H).waitFor({ timeout: 5000 })) && await espera(tarjeta(bea, SALA_I).waitFor({ timeout: 5000 })),
     'el lobby lista las dos salas recién creadas');

  console.log('== B. Los creadores cierran la pestaña ==');
  const t0 = Date.now();
  await ana.close();
  await dani.close();
  ok(await tarjeta(bea, SALA_H).count() === 1, 'nada más cerrar siguen listadas (margen para recargar o reconectar)');
  const oculta = await espera(tarjeta(bea, SALA_H).waitFor({ state: 'detached', timeout: 220_000 }));
  ok(oculta, `la sala sin nadie deja de listarse sola, sin recargar el lobby (${Math.round((Date.now() - t0) / 1000)} s)`);
  ok(await espera(tarjeta(bea, SALA_I).waitFor({ state: 'detached', timeout: 70_000 })), 'la otra también');
  await shot(bea, 'h01-lobby-sin-huerfanas');
  const r = await resumen(idH);
  ok(r && r.unattended === true, `la sala sigue viva en la API, marcada como desatendida: ${JSON.stringify(r && { status: r.status, unattended: r.unattended })}`);

  console.log('== C. La creadora vuelve ==');
  const ana2 = await ana.context().newPage();
  await ana2.goto(WEB + '/', { waitUntil: 'load' });
  ok(await espera(ana2.locator(`.resume-banner:has-text("${SALA_H}")`).waitFor({ timeout: 90_000 })), 'su lobby le avisa de que sigue en la sala');
  ok(await tarjeta(ana2, SALA_H).count() === 1, 'y ella sí la ve en la lista');
  ok(await tarjeta(bea, SALA_H).count() === 0, 'los demás siguen sin verla mientras no entre');
  await ana2.locator('.resume-banner button').click();
  await ana2.waitForSelector('.waiting-room', { timeout: 10_000 });
  ok(await espera(tarjeta(bea, SALA_H).waitFor({ timeout: 5000 })), 'al entrar, la sala vuelve al lobby de los demás al momento');

  console.log('== D. La invitación a una sala que no se lista sigue valiendo ==');
  const carla = await jugador(browser, 'Carla');
  ok(await tarjeta(carla, SALA_I).count() === 0, '(la sala de Dani no está en la lista de Carla)');
  await carla.goto(`${WEB}/?join=${idI}`, { waitUntil: 'load' });
  ok(await espera(carla.waitForSelector('.waiting-room', { timeout: 90_000 })), 'el enlace de invitación la sienta en la sala');
  ok(await espera(tarjeta(bea, SALA_I).waitFor({ timeout: 5000 })), 'y con alguien dentro la sala vuelve a listarse');
  await shot(bea, 'h02-lobby-salas-de-vuelta');
} catch (e) {
  fallar(e);
} finally {
  await browser.close();
}
terminar();
