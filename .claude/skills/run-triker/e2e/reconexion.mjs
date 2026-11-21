// Reconexión tras un corte de red: la mesa y el lobby vuelven solos, sin
// «Recargar», aunque el corte dure más que los 42 s en que antes se rendían.
// Solo contra localhost. Tarda unos 3 min.
import { chromium, WEB, MOVIL, ok, espera, sleep, fallar, terminar, soloLocal } from './lib/comun.mjs';

soloLocal();

import { conCorte } from './lib/corte.mjs';

const AVISO = '.reconnect-banner:not(.timeout-banner)';
const aviso = async (page) => (await page.locator(AVISO).first().textContent({ timeout: 400 }).catch(() => null))?.trim().replace(/\s+/g, ' ') ?? '(sin aviso)';

const browser = await chromium.launch({ channel: 'chrome', headless: true });
async function jugador(nombre, corte) {
  const ctx = await browser.newContext({ ...MOVIL });
  const red = await conCorte(ctx, corte);
  const page = await ctx.newPage();
  page.on('pageerror', (e) => { if (!/ServiceWorker/.test(e.message)) console.log(`  [${nombre} pageerror] ${e.message.slice(0, 160)}`); });
  await page.goto(WEB + '/', { waitUntil: 'load' });
  await page.waitForSelector('.lobby .game-title', { timeout: 90_000 });
  await page.fill('#nombre-jugador', nombre);
  await page.evaluate(() => { window.__sinRecargar = true; });   // se pierde si la página se recarga
  return { page, red };
}
async function mesaDeDos(a, b, nombre) {
  await a.click('.lobby-actions .btn-new-game');
  await a.fill('.create-form .name-input', nombre);
  await a.click('.create-form .btn-primary');
  await a.waitForSelector('.waiting-room');
  await b.locator(`.game-card:has-text("${nombre}") .btn-join`).click();
  await b.waitForSelector('.waiting-room');
  await a.waitForFunction(() => document.querySelectorAll('.waiting-player').length === 2);
  await a.click('.btn-start');
}
const n = Date.now() % 100000;

try {
  console.log('== A. Corte de 50 s a mitad de partida (antes se rendía a los 42 s) ==');
  const ana = await jugador('Ana'), berto = await jugador('Berto');
  await mesaDeDos(ana.page, berto.page, `Corte A${n}`);
  // Quién tiene el turno (X) y quién espera (Y): se corta la red de Y.
  const xEsAna = await Promise.race([
    ana.page.waitForSelector('.turn-indicator.my-turn', { timeout: 30_000 }).then(() => true),
    berto.page.waitForSelector('.turn-indicator.my-turn', { timeout: 30_000 }).then(() => false),
  ]);
  const x = xEsAna ? ana : berto, y = xEsAna ? berto : ana;
  await sleep(3000);                                  // el hub conecta después de pintar la mesa
  await y.red.cortar();
  ok(await espera(y.page.locator(`${AVISO}:has-text("Reconectando")`).waitFor({ timeout: 5000 })), 'al cortarse la red avisa de que está reconectando');
  await sleep(20_000);
  await x.page.click('.btn-pass');                    // el turno pasa a Y mientras no tiene red
  await sleep(26_000);                                // 46 s de corte: ya pasaron los 42
  ok(/Reconectando/.test(await aviso(y.page)), `a los 46 s sigue intentándolo, no pide recargar: «${await aviso(y.page)}»`);
  await sleep(4000);
  y.red.volver();
  const t0 = Date.now();
  ok(await espera(y.page.waitForSelector('.turn-indicator.my-turn', { timeout: 9000 })), `al volver la red recupera la mesa al día: es su turno (${((Date.now() - t0) / 1000).toFixed(1)} s)`);
  ok(await aviso(y.page) === '(sin aviso)', 'y el aviso desaparece');
  ok(await y.page.evaluate(() => window.__sinRecargar === true), 'sin recargar la página');
  await y.page.click('.btn-pass', { timeout: 3000 }).catch(() => {});
  ok(await espera(x.page.waitForSelector('.results-view .result-entry', { timeout: 15_000 })) && await espera(y.page.waitForSelector('.results-view .result-entry', { timeout: 5000 })),
     'y la partida sigue: los dos ven los resultados');
  await ana.page.context().close(); await berto.page.context().close();

  console.log('== B. La primera conexión del hub falla (la mesa ya cargó por REST) ==');
  const carla = await jugador('Carla', { soloHub: true });
  await carla.red.cortar();
  await carla.page.click('.btn-play-now');
  await carla.page.waitForSelector('.hand-cards .card', { timeout: 20_000 });
  await sleep(2500);
  ok(/Reconectando/.test(await aviso(carla.page)), `sigue intentándolo en vez de dar la conexión por perdida: «${await aviso(carla.page)}»`);
  carla.red.volver();
  const t1 = Date.now();
  ok(await espera(carla.page.locator(AVISO).waitFor({ state: 'detached', timeout: 9000 })), `al poder conectar, el aviso desaparece (${((Date.now() - t1) / 1000).toFixed(1)} s)`);
  await carla.page.waitForSelector('.turn-indicator.my-turn', { timeout: 20_000 });
  await carla.page.click('.btn-pass');
  ok(await espera(carla.page.waitForSelector('.results-view .result-entry', { timeout: 40_000 })), 'y la mesa recibe en directo: los bots juegan y salen los resultados');
  await carla.page.context().close();

  console.log('== C. El lobby tras un corte de 50 s ==');
  const dani = await jugador('Dani');
  await sleep(2000);
  await dani.red.cortar();
  await sleep(50_000);
  dani.red.volver();
  await sleep(7000);                                  // margen para que reconecte
  const eva = await jugador('Eva');
  await eva.page.click('.lobby-actions .btn-new-game');
  await eva.page.fill('.create-form .name-input', `Corte C${n}`);
  await eva.page.click('.create-form .btn-primary');
  await eva.page.waitForSelector('.waiting-room');
  ok(await espera(dani.page.locator(`.game-card:has-text("Corte C${n}")`).waitFor({ timeout: 6000 })), 'el lobby vuelve a recibir las partidas nuevas en directo');
  ok(await dani.page.evaluate(() => window.__sinRecargar === true), 'sin recargar la página');
  await eva.page.click('.btn-leave').catch(() => {});
} catch (e) {
  fallar(e);
} finally {
  await browser.close();
}
terminar();
