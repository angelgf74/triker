// Comprobación en la app real de la barra de cuenta atrás y del aviso de
// turno agotado. Solo contra localhost. Tarda unos 2,5 min: deja agotar dos turnos.
import { join } from 'node:path';
import { chromium, WEB, MOVIL, SHOTS, ok, espera, sleep, fallar, terminar, soloLocal } from './lib/comun.mjs';

soloLocal();

const shot = (page, name) => page.screenshot({ path: join(SHOTS, `${name}.png`), fullPage: true });

const barra = (page) => page.evaluate(() => {
  const fill = document.querySelector('.countdown-fill');
  const bar = document.querySelector('.countdown-bar');
  if (!fill || !bar) return null;
  return {
    estilo: fill.getAttribute('style'),
    ancho: fill.getBoundingClientRect().width / bar.getBoundingClientRect().width,
    segundos: parseInt(document.querySelector('.countdown-secs').textContent, 10),
  };
});

const browser = await chromium.launch({ channel: 'chrome', headless: true });
try {
  const ctx = await browser.newContext({ ...MOVIL });
  const page = await ctx.newPage();
  page.on('pageerror', (e) => { if (!/ServiceWorker/.test(e.message)) console.log(`  [pageerror] ${e.message.slice(0, 160)}`); });
  await page.goto(WEB + '/', { waitUntil: 'load' });
  await page.waitForSelector('.game-title', { timeout: 90_000 });
  await page.fill('#nombre-jugador', 'Ana');
  await page.click('.lobby-actions .btn-new-game');
  await page.fill('.create-form .name-input', `Reloj ${Date.now() % 100000}`);
  await page.click('.create-form .btn-primary');
  await page.waitForSelector('.waiting-room');
  await page.click('.waiting-actions .btn-secondary'); // + bot
  await page.waitForFunction(() => document.querySelectorAll('.waiting-player').length === 2);
  await page.click('.btn-start');
  await page.waitForSelector('.turn-indicator.my-turn', { timeout: 30_000 });

  console.log('== A. Barra de cuenta atrás (navegador en es-ES) ==');
  await page.waitForSelector('.countdown-fill');
  await sleep(4000);
  const b1 = await barra(page);
  console.log(`  a los ~4 s: ${JSON.stringify(b1)}`);
  ok(b1 && !b1.estilo.includes(','), `el ancho se escribe sin coma decimal: "${b1?.estilo}"`);
  ok(b1 && Math.abs(b1.ancho - b1.segundos / 60) < 0.03, `la barra mide lo que queda (${(b1.ancho * 100).toFixed(0)} % con ${b1.segundos} s)`);
  await shot(page, 'r01-barra');
  await sleep(8000);
  const b2 = await barra(page);
  ok(b2 && b2.ancho < b1.ancho - 0.08, `y va bajando (${(b2.ancho * 100).toFixed(0)} % con ${b2.segundos} s)`);

  console.log('== B. Turno agotado sin ser el último de la ronda (Ana empieza la ronda 1) ==');
  ok(await espera(page.waitForSelector('.timeout-banner', { timeout: 70_000 })), 'aparece el aviso al agotarse el tiempo');
  await page.waitForSelector('.results-view .result-entry', { timeout: 30_000 });
  await sleep(1500);
  ok(await page.locator('.timeout-banner').count() === 1, 'y sigue en la pantalla de resultados');

  console.log('== C. Turno agotado siendo la última de la ronda (ronda 2: el bot juega antes) ==');
  await page.click('.continue-buttons .btn-primary');
  await page.waitForSelector('.round-badge:has-text("Ronda 2")', { timeout: 60_000 });
  await page.waitForSelector('.turn-indicator.my-turn', { timeout: 30_000 });
  ok(await page.locator('.timeout-banner').count() === 0, 'al repartir la ronda nueva el aviso anterior se quita');
  // Apunta cada cambio entre mesa y resultados: tras revelar no debe volver la mesa.
  await page.evaluate(() => {
    window.__vistas = [];
    const mira = () => {
      const v = document.querySelector('.results-view') ? 'resultados' : document.querySelector('.hand-cards') ? 'mesa' : 'otra';
      if (window.__vistas.at(-1) !== v) window.__vistas.push(v);
    };
    mira();
    new MutationObserver(mira).observe(document.body, { childList: true, subtree: true });
  });
  await page.waitForSelector('.results-view .result-entry', { timeout: 80_000 });
  await sleep(1500);
  ok(await page.locator('.timeout-banner').count() === 1, 'el aviso de turno agotado sigue visible en los resultados');
  await shot(page, 'r02-aviso-en-resultados');
  const vistas = await page.evaluate(() => window.__vistas);
  ok(vistas.join('>') === 'mesa>resultados', `al revelar no reaparece la mesa: ${vistas.join(' > ')}`);
} catch (e) {
  fallar(e);
} finally {
  await browser.close();
}
terminar();
