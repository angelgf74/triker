// La mesa en tu turno, a 360 px: tu zona es un tapete de color propio, las cartas
// ocupan el ancho (antes medían 56 px), la jugada va con su escala de 16 escalones y
// el botón de jugar es siempre la acción principal. Vale contra producción: crea una
// mesa y sale de ella.
import { join } from 'node:path';
import { chromium, WEB, MOVIL, SHOTS, ok, fallar, terminar } from './lib/comun.mjs';

const DORADO = 'rgb(240, 192, 64)';

const browser = await chromium.launch({ channel: 'chrome', headless: true });
try {
  const ctx = await browser.newContext({ ...MOVIL });
  const page = await ctx.newPage();
  page.on('pageerror', (e) => { if (!/ServiceWorker/.test(e.message)) console.log(`  [pageerror] ${e.message.slice(0, 160)}`); });
  await page.goto(WEB + '/', { waitUntil: 'load' });
  await page.waitForSelector('.lobby .game-title', { timeout: 90_000 });
  await page.fill('#nombre-jugador', 'Ana');
  await page.click('.btn-play-now');
  await page.waitForSelector('.turn-indicator.my-turn', { timeout: 20_000 });
  await page.waitForFunction(() => document.querySelectorAll('.hand-cards .card.dealing').length === 0, null, { timeout: 10_000 });

  console.log('== A. Tapete y cartas ==');
  const zona = await page.locator('.my-area').evaluate((e) => {
    const s = getComputedStyle(e), fondo = getComputedStyle(document.body).backgroundColor;
    const tapete = getComputedStyle(document.documentElement).getPropertyValue('--tapete').trim();
    const r = e.getBoundingClientRect();
    return { fondo: s.backgroundColor, pagina: fondo, tapete, x: Math.round(r.x), w: Math.round(r.width) };
  });
  ok(/^#[0-9a-f]{6}$/i.test(zona.tapete) && zona.fondo !== zona.pagina && !/rgba\(.*, 0(\.\d+)?\)$/.test(zona.fondo),
     `tu zona es un tapete de color propio, opaco (--tapete ${zona.tapete || 'sin definir'}, pintado ${zona.fondo})`);
  ok(zona.x === 0 && zona.w === 360, `y en el móvil va de lado a lado (x=${zona.x}, ancho ${zona.w})`);
  const cartas = await page.locator('.hand-cards .card').evaluateAll((els) => els.map((e) => { const r = e.getBoundingClientRect(); return { x: Math.round(r.x), y: Math.round(r.y), w: Math.round(r.width), fin: Math.round(r.right) }; }));
  // Un píxel de margen: se reparten el ancho y el redondeo las deja a 63 o 64 de ancho.
  ok(cartas.length === 5 && cartas.every((c) => Math.abs(c.y - cartas[0].y) <= 2), `las cinco cartas van en una fila (y: ${cartas.map((c) => c.y).join(", ")})`);
  ok(cartas.every((c) => c.w >= 62), `y miden al menos 62 px de ancho (${cartas.map((c) => c.w).join(', ')})`);
  ok(cartas[0].x >= 0 && cartas[4].fin <= 360 && await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), 'sin salirse de la pantalla ni scroll horizontal');

  console.log('== B. La jugada y su escala ==');
  const escala = await page.locator('.escala-jugada').evaluate((e) => ({
    pasos: e.querySelectorAll('.escala-paso').length,
    subidos: e.querySelectorAll('.escala-paso.subido').length,
    extremos: [...e.querySelectorAll('.escala-extremos span')].map((s) => s.textContent.trim()),
    leido: e.getAttribute('aria-label'),
  })).catch(() => null);
  const puesto = Number((await page.locator('.my-play-badge .grp-ladder').textContent()).trim().split('/')[0]);
  ok(escala?.pasos === 16, `la escala tiene 16 escalones (${escala?.pasos ?? 'no hay escala'})`);
  ok(escala?.subidos === 17 - puesto, `con el puesto ${puesto} hay ${17 - puesto} subidos (${escala?.subidos})`);
  ok(escala?.extremos.join('|') === 'Sin jugada|Retriker Fuerte', `los extremos llevan nombre: ${escala?.extremos.join(' … ')}`);
  ok(/1 es la mejor/.test(escala?.leido ?? ''), `y un lector de pantalla la lee: «${escala?.leido}»`);

  console.log('== C. El botón de jugar ==');
  const boton = (sel) => page.locator(sel).evaluate((e) => { const s = getComputedStyle(e), r = e.getBoundingClientRect(); return { fondo: s.backgroundColor, w: Math.round(r.width), y: Math.round(r.y), h: Math.round(r.height) }; });
  const pasar = await boton('.btn-pass');
  ok(pasar.fondo === DORADO, `«Pasar» es la acción principal (${pasar.fondo})`);
  await page.screenshot({ path: join(SHOTS, 'mesa-01-turno.png') });
  await page.locator('.hand-cards .card').nth(3).click();
  await page.locator('.hand-cards .card').nth(4).click();
  await page.waitForSelector('.btn-discard');
  await page.waitForTimeout(400);
  const descartar = await boton('.btn-discard');
  ok(descartar.fondo === DORADO && descartar.y === pasar.y && descartar.h === pasar.h, `y «Descartar» ocupa el mismo sitio: nada salta al seleccionar (y ${pasar.y} / ${descartar.y}, alto ${pasar.h} / ${descartar.h})`);
  const elegida = await page.locator('.hand-cards .card.selected').first().evaluate((e) => Math.round(e.getBoundingClientRect().top - e.closest('.my-area').getBoundingClientRect().top));
  ok(elegida >= 0, `la carta elegida sube sin salirse del tapete (${elegida} px desde su borde)`);
  await page.screenshot({ path: join(SHOTS, 'mesa-02-seleccion.png') });

  await page.click('.btn-leave');
  await page.click('.leave-confirm .btn-danger');
  await page.waitForSelector('.lobby .game-title', { timeout: 15_000 });
} catch (e) {
  fallar(e);
} finally {
  await browser.close();
}
terminar();
