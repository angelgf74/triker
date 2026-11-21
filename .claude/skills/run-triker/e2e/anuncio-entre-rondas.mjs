// Momento del intersticial: no al revelar las manos, sino al pulsar «Siguiente
// ronda» (y al salir desde resultados), y la ronda no empieza hasta que se cierra.
// Simula el puente de la app (window.AndroidAds): al pedir un anuncio la página
// pasa a oculta y vuelve a visible al cerrarlo, como se midió en el emulador.
// Solo contra localhost.
import { chromium, WEB, MOVIL, ok, espera, sleep, fallar, terminar, soloLocal } from './lib/comun.mjs';

soloLocal();

// modo: 'anuncio' (se muestra y dura `duracion` ms), 'sin-anuncio' (la app no
// tenía ninguno cargado: no pasa nada) o 'web' (sin puente: navegador normal).
function puente({ modo, duracion }) {
  const reg = (window.__reg = { pedidos: [], cierres: [], envios: [] });
  const fetchReal = window.fetch;
  window.fetch = function (input, init) {
    const url = typeof input === 'string' ? input : input.url;
    const metodo = (init && init.method) || (typeof input === 'object' && input.method) || 'GET';
    const m = /\/api\/games\/[0-9a-f-]+\/(continue|leave)/.exec(url);
    if (m && metodo === 'POST') reg.envios.push({ t: Date.now(), que: m[1], oculta: document.hidden });
    return fetchReal.apply(this, arguments);
  };
  if (modo === 'web') return;
  let oculta = false;
  Object.defineProperty(document, 'hidden', { get: () => oculta, configurable: true });
  Object.defineProperty(document, 'visibilityState', { get: () => (oculta ? 'hidden' : 'visible'), configurable: true });
  const cambia = (v) => { oculta = v; document.dispatchEvent(new Event('visibilitychange')); };
  window.AndroidAds = {
    showInterstitial() {
      reg.pedidos.push({ t: Date.now(), resultados: !!document.querySelector('.results-view'), decidido: !document.querySelector('.continue-buttons') });
      if (modo !== 'anuncio') return;
      setTimeout(() => {
        cambia(true);
        setTimeout(() => { reg.cierres.push(Date.now()); cambia(false); }, duracion);
      }, 45);
    },
    privacyOptionsRequired() { return false; },
    showPrivacyOptions() {},
  };
}

const browser = await chromium.launch({ channel: 'chrome', headless: true });
async function mesa(opciones) {
  const ctx = await browser.newContext({ ...MOVIL });
  await ctx.addInitScript(puente, opciones);
  const page = await ctx.newPage();
  page.on('pageerror', (e) => { if (!/ServiceWorker/.test(e.message)) console.log(`  [pageerror] ${e.message.slice(0, 160)}`); });
  await page.goto(WEB + '/', { waitUntil: 'load' });
  await page.waitForSelector('.lobby .game-title', { timeout: 90_000 });
  await page.fill('#nombre-jugador', 'Ana');
  await page.click('.btn-play-now');
  await page.waitForSelector('.turn-indicator.my-turn', { timeout: 20_000 });
  await page.click('.btn-pass');
  await page.waitForSelector('.results-view .result-entry', { timeout: 40_000 });
  return page;
}
const reg = (page) => page.evaluate(() => window.__reg);
const SIGUIENTE = '.continue-buttons .btn-primary';

try {
  console.log('== A. Con anuncio (dura 3 s) ==');
  let page = await mesa({ modo: 'anuncio', duracion: 3000 });
  await sleep(2000);
  let r = await reg(page);
  ok(r.pedidos.length === 0, `al revelar las manos no se pide anuncio: los resultados se ven (${r.pedidos.length} pedidos)`);
  let pulsado = await page.evaluate(() => Date.now());
  await page.click(SIGUIENTE);
  await sleep(1500);
  r = await reg(page);
  ok(r.pedidos.length === 1 && r.pedidos[0].t >= pulsado, `al pulsar «Siguiente ronda» se pide el anuncio (${r.pedidos.length})`);
  ok(r.envios.length === 0, 'mientras el anuncio está delante no se pide la ronda nueva');
  ok(await page.locator(SIGUIENTE).count() === 0, 'y el botón ya no se puede volver a pulsar');
  ok(await espera(page.waitForSelector('.round-badge:has-text("Ronda 2")', { timeout: 15_000 })), 'al cerrarlo empieza la ronda 2');
  r = await reg(page);
  const retraso = r.envios[0] && r.cierres[0] ? r.envios[0].t - r.cierres[0] : NaN;
  ok(r.envios.length === 1 && retraso >= 0 && retraso < 800, `la petición sale justo al cerrar el anuncio (${retraso} ms después)`);

  console.log('   -- segunda ronda antes de un minuto: no toca anuncio --');
  await page.waitForSelector('.turn-indicator.my-turn', { timeout: 40_000 });
  await page.click('.btn-pass');
  await page.waitForSelector('.results-view .result-entry', { timeout: 40_000 });
  let t0 = Date.now();
  await page.click(SIGUIENTE);
  ok(await espera(page.waitForFunction(() => window.__reg.envios.length === 2, null, { timeout: 5000 })), 'sin anuncio pendiente continúa');
  r = await reg(page);
  ok(r.pedidos.length === 1 && Date.now() - t0 < 1200, `al momento y sin pedir otro (${Date.now() - t0} ms)`);
  await page.context().close();

  console.log('== B. La app no tenía anuncio cargado ==');
  page = await mesa({ modo: 'sin-anuncio' });
  t0 = Date.now();
  await page.click(SIGUIENTE);
  ok(await espera(page.waitForFunction(() => window.__reg.envios.length === 1, null, { timeout: 6000 })), 'continúa igualmente');
  ok(Date.now() - t0 < 3000, `tras un margen corto (${Date.now() - t0} ms)`);
  ok((await reg(page)).pedidos.length === 1, 'habiéndolo pedido');
  ok(await espera(page.waitForSelector('.round-badge:has-text("Ronda 2")', { timeout: 15_000 })), 'y empieza la ronda 2');
  await page.context().close();

  console.log('== C. Navegador normal (sin app) ==');
  page = await mesa({ modo: 'web' });
  t0 = Date.now();
  await page.click(SIGUIENTE);
  ok(await espera(page.waitForFunction(() => window.__reg.envios.length === 1, null, { timeout: 5000 })), 'continúa');
  ok(Date.now() - t0 < 1200, `sin ninguna espera añadida (${Date.now() - t0} ms)`);
  await page.context().close();

  console.log('== D. «Salir de la mesa» desde resultados ==');
  page = await mesa({ modo: 'anuncio', duracion: 3000 });
  pulsado = await page.evaluate(() => Date.now());
  await page.click('button:has-text("Salir de la mesa")');
  ok(await espera(page.waitForFunction(() => window.__reg.envios.length === 1, null, { timeout: 2500 })), 'la salida se envía sin esperar al anuncio (los demás no se quedan colgados)');
  r = await reg(page);
  ok(r.pedidos.length === 1 && r.pedidos[0].t >= pulsado, `y se pide el anuncio en ese momento (${r.pedidos.length})`);
  ok(await espera(page.waitForSelector('.lobby .game-title', { timeout: 10_000 })), 'al cerrarlo está en el lobby');
  await page.context().close();
} catch (e) {
  fallar(e);
} finally {
  await browser.close();
}
terminar();
