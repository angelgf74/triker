// Plazo para decidir entre rondas: cuando un humano pulsa «Siguiente ronda» al
// resto le queda un minuto, y la pantalla lo dice. Solo contra localhost.
// Tarda unos 3 min: deja vencer el plazo una vez y casi vencer otra.
import { join } from 'node:path';
import { chromium, WEB, MOVIL, SHOTS, ok, espera, sleep, fallar, terminar, soloLocal } from './lib/comun.mjs';

soloLocal();

// Puente de la app simulado: el anuncio tapa la página `duracion` ms.
function puente({ duracion }) {
  const reg = (window.__reg = { pedidos: 0, envios: [] });
  const fetchReal = window.fetch;
  window.fetch = function (input, init) {
    const url = typeof input === 'string' ? input : input.url;
    if (/\/api\/games\/[0-9a-f-]+\/continue/.test(url)) reg.envios.push(Date.now());
    return fetchReal.apply(this, arguments);
  };
  let oculta = false;
  Object.defineProperty(document, 'hidden', { get: () => oculta, configurable: true });
  Object.defineProperty(document, 'visibilityState', { get: () => (oculta ? 'hidden' : 'visible'), configurable: true });
  const cambia = (v) => { oculta = v; document.dispatchEvent(new Event('visibilitychange')); };
  window.AndroidAds = {
    showInterstitial() { reg.pedidos++; setTimeout(() => { cambia(true); setTimeout(() => cambia(false), duracion); }, 45); },
    privacyOptionsRequired() { return false; },
    showPrivacyOptions() {},
  };
}

const browser = await chromium.launch({ channel: 'chrome', headless: true });
async function jugador(nombre, anuncio) {
  const ctx = await browser.newContext({ ...MOVIL });
  if (anuncio) await ctx.addInitScript(puente, anuncio);
  const page = await ctx.newPage();
  page.on('pageerror', (e) => { if (!/ServiceWorker/.test(e.message)) console.log(`  [${nombre} pageerror] ${e.message.slice(0, 160)}`); });
  await page.goto(WEB + '/', { waitUntil: 'load' });
  await page.waitForSelector('.lobby .game-title', { timeout: 90_000 });
  await page.fill('#nombre-jugador', nombre);
  return page;
}
// Mesa de dos humanos con la primera ronda terminada.
async function mesa(a, b, nombre) {
  await a.click('.lobby-actions .btn-new-game');
  await a.fill('.create-form .name-input', nombre);
  await a.click('.create-form .btn-primary');
  await a.waitForSelector('.waiting-room');
  await b.locator(`.game-card:has-text("${nombre}") .btn-join`).click();
  await b.waitForSelector('.waiting-room');
  await a.waitForFunction(() => document.querySelectorAll('.waiting-player').length === 2);
  await a.click('.btn-start');
  for (let i = 0; i < 2; i++) {
    const turno = await Promise.race([
      a.waitForSelector('.turn-indicator.my-turn', { timeout: 30_000 }).then(() => a),
      b.waitForSelector('.turn-indicator.my-turn', { timeout: 30_000 }).then(() => b),
    ]);
    await turno.click('.btn-pass');
    await turno.waitForSelector('.turn-indicator.my-turn', { state: 'detached', timeout: 10_000 }).catch(() => {});
  }
  await a.waitForSelector('.results-view .result-entry', { timeout: 30_000 });
  await b.waitForSelector('.results-view .result-entry', { timeout: 30_000 });
}
const SIGUIENTE = '.continue-buttons .btn-primary';
const plazo = async (page) => {
  const t = await page.locator('.continue-deadline').first().textContent({ timeout: 1500 }).catch(() => null);
  const m = t && /(\d+)\s*s/.exec(t);
  return { texto: t?.trim().replace(/\s+/g, ' ') ?? null, segundos: m ? +m[1] : null };
};
const n = Date.now() % 100000;

try {
  console.log('== A. Nadie ha decidido: sin prisas ==');
  const ana = await jugador('Ana');
  const berto = await jugador('Berto');
  await mesa(ana, berto, `Plazo A${n}`);
  await sleep(1500);
  ok((await plazo(ana)).texto === null && (await plazo(berto)).texto === null, 'con cinco minutos por delante no se enseña ninguna cuenta atrás');

  console.log('== B. Ana pulsa «Siguiente ronda»; Berto no hace nada ==');
  const t0 = Date.now();
  await ana.click(SIGUIENTE);
  await sleep(1500);
  const pa = await plazo(ana), pb = await plazo(berto);
  ok(pa.segundos > 50 && pa.segundos <= 60 && /sale de la mesa/.test(pa.texto ?? ''), `quien ya decidió ve cuánto queda como mucho: «${pa.texto}»`);
  ok(pb.segundos > 50 && pb.segundos <= 60 && /Decide/.test(pb.texto ?? ''), `quien no ha decidido ve su plazo: «${pb.texto}»`);
  ok(await berto.locator(SIGUIENTE).count() === 1, 'y sigue teniendo los botones');
  await ana.screenshot({ path: join(SHOTS, 'z01-ana-espera.png') });
  await berto.screenshot({ path: join(SHOTS, 'z02-berto-decide.png') });
  await sleep(5000);
  const pb2 = await plazo(berto);
  ok(pb2.segundos < pb.segundos - 3, `la cuenta baja (${pb.segundos} s → ${pb2.segundos} s)`);
  ok(await espera(berto.waitForSelector('.lobby .game-title', { timeout: 70_000 })), `al vencer, Berto sale de la mesa (${Math.round((Date.now() - t0) / 1000)} s después, no 5 min)`);
  ok(await espera(berto.locator('text=no decidiste a tiempo').waitFor({ timeout: 5000 })), 'con el aviso de por qué');
  ok(await espera(ana.waitForSelector('.waiting-room', { timeout: 10_000 })), 'y Ana, que se queda sola, vuelve a la sala de espera');
  const seg = Math.round((Date.now() - t0) / 1000);
  ok(seg >= 55 && seg <= 75, `todo en torno a un minuto (${seg} s)`);
  await ana.context().close(); await berto.context().close();

  console.log('== C. Carla ve un anuncio largo; Dani decide mientras tanto ==');
  const carla = await jugador('Carla', { duracion: 100_000 });
  const dani = await jugador('Dani');
  await mesa(carla, dani, `Plazo C${n}`);
  await carla.click(SIGUIENTE);                 // se abre su anuncio: su decisión aún no ha salido
  await sleep(2000);
  ok((await carla.evaluate(() => window.__reg)).envios.length === 0, 'con el anuncio delante, la decisión de Carla aún no se ha enviado');
  const t1 = Date.now();
  await dani.click(SIGUIENTE);                  // empieza el minuto para Carla
  ok(await espera(dani.waitForSelector('.round-badge:has-text("Ronda 2")', { timeout: 70_000 })), 'la ronda 2 empieza');
  const tardo = Math.round((Date.now() - t1) / 1000);
  ok(tardo >= 45 && tardo < 60, `antes de vencer el plazo, porque la web de Carla envía su decisión a tiempo (${tardo} s)`);
  await sleep(1500);
  ok(await carla.locator('.round-badge:has-text("Ronda 2")').count() === 1 && !(await carla.url()).endsWith('aviso=fuera'), 'y Carla sigue en la mesa: ver el anuncio no la ha echado');
  ok(await dani.locator('.other-player').count() === 1, 'con los dos jugadores');
  await carla.context().close(); await dani.context().close();

  console.log('== D. Contra bots no cambia nada ==');
  const eva = await jugador('Eva');
  await eva.click('.btn-play-now');
  await eva.waitForSelector('.turn-indicator.my-turn', { timeout: 20_000 });
  await eva.click('.btn-pass');
  await eva.waitForSelector('.results-view .result-entry', { timeout: 40_000 });
  await sleep(1500);
  ok((await plazo(eva)).texto === null, 'sin cuenta atrás en los resultados');
  await eva.click(SIGUIENTE);
  ok(await espera(eva.waitForSelector('.round-badge:has-text("Ronda 2")', { timeout: 15_000 })), 'y «Siguiente ronda» reparte al momento');
  await eva.context().close();
} catch (e) {
  fallar(e);
} finally {
  await browser.close();
}
terminar();
