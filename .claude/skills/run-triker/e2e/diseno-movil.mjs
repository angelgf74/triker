// Medidas de diseño a 360 px: el logo igual en la carga y en el lobby, los botones
// de la cabecera a 44 px, los del lobby al mismo ancho, el icono de copiar el código
// privado en su línea, una sola acción principal en la sala de espera y las tablas
// de historial y sesión dentro de su tarjeta. Vale contra producción: crea una mesa
// privada y sale de ella. SHOT_PRE cambia el prefijo de las capturas.
import { join } from 'node:path';
import { chromium, WEB, MOVIL, SHOTS, ok, fallar, terminar } from './lib/comun.mjs';

const PRE = process.env.SHOT_PRE ?? 'diseno-';
const caja = (page, sel) => page.locator(sel).first().evaluate((e) => { const r = e.getBoundingClientRect(); const s = getComputedStyle(e); return { x: Math.round(r.x), y: Math.round(r.y), w: Math.round(r.width), h: Math.round(r.height), color: s.color, borde: s.borderColor, letra: parseFloat(s.fontSize) }; });
const desborde = (page) => page.evaluate(() => document.documentElement.scrollWidth - innerWidth);

const browser = await chromium.launch({ channel: 'chrome', headless: true });
try {
  const ctx = await browser.newContext({ ...MOVIL });
  const page = await ctx.newPage();
  page.on('pageerror', (e) => { if (!/ServiceWorker/.test(e.message)) console.log(`  [pageerror] ${e.message.slice(0, 160)}`); });

  console.log('== A. Pantalla de carga y logo ==');
  // La pantalla de carga es HTML fijo: se lee tal cual llega, antes de que arranque la app.
  const html = await (await ctx.request.get(WEB + '/')).text();
  const carga = /<h1 class="loading-title">(.*?)<\/h1>/s.exec(html)?.[1].replace(/<[^>]+>/g, '') ?? '';
  await page.goto(WEB + '/', { waitUntil: 'load' });
  await page.waitForSelector('.lobby .game-title', { timeout: 90_000 });
  const logo = (await page.locator('.lobby .game-title').first().textContent()).trim();
  ok(carga === logo, `el título de la pantalla de carga es el mismo que el del lobby: «${carga}» y «${logo}»`);

  console.log('== B. Lobby ==');
  const sug = await caja(page, '.btn-sug'), ayuda = await caja(page, '.btn-help');
  ok(sug.w >= 44 && sug.h >= 44, `el botón de sugerencias mide al menos 44 px (${sug.w}x${sug.h})`);
  ok(sug.w === ayuda.w && sug.h === ayuda.h, `y lo mismo que el de ayuda (${ayuda.w}x${ayuda.h})`);
  ok(sug.borde === ayuda.borde && sug.color === ayuda.color, `con el mismo estilo (${sug.borde} / ${ayuda.borde})`);
  const jugar = await caja(page, '.btn-play-now'), nueva = await caja(page, '.btn-new-game');
  ok(jugar.w === nueva.w && jugar.x === nueva.x, `los dos botones de acción van alineados y con el mismo ancho (${jugar.w} y ${nueva.w} px)`);
  ok(await desborde(page) <= 0, `sin scroll horizontal (${await desborde(page)} px)`);
  await page.screenshot({ path: join(SHOTS, `${PRE}01-lobby.png`) });

  console.log('== C. Sala de espera privada ==');
  await page.fill('#nombre-jugador', 'Ana');
  await page.click('.btn-new-game');
  await page.fill('.create-form .name-input', `Diseño ${Date.now() % 100000}`);
  await page.check('.private-toggle input');
  await page.click('.create-form .btn-primary');
  await page.waitForSelector('.waiting-room');
  const codigo = await caja(page, '.btn-copy-code');
  const icono = await page.locator('.btn-copy-code .copy-icon').first().evaluate((e) => parseFloat(getComputedStyle(e).fontSize)).catch(() => null);
  ok(icono !== null && icono <= 20, `el icono de copiar va a tamaño de texto normal (${icono ?? 'sin elemento propio: hereda ' + codigo.letra} px)`);
  ok(codigo.h < codigo.letra * 1.8, `y el código queda en una sola línea (alto ${codigo.h} px con letra de ${codigo.letra})`);
  ok(await page.locator('.btn-invite.btn-primary').count() === 1, 'sola en la sala, «Invitar» es la acción principal');
  await page.screenshot({ path: join(SHOTS, `${PRE}02-sala-privada.png`) });
  await page.click('.waiting-actions .btn-secondary');   // + bot
  await page.waitForFunction(() => document.querySelectorAll('.waiting-player').length === 2);
  ok(await page.locator('.btn-invite.btn-secondary').count() === 1 && await page.locator('.btn-start.btn-primary:not([disabled])').count() === 1,
     'con dos en la mesa, la principal pasa a ser «Iniciar partida» e «Invitar» queda secundaria');
  await page.screenshot({ path: join(SHOTS, `${PRE}03-sala-con-dos.png`) });

  console.log('== D. Mesa en la segunda ronda (historial y sesión) ==');
  await page.click('.btn-start');
  await page.waitForSelector('.turn-indicator.my-turn', { timeout: 30_000 });
  await page.click('.btn-pass');
  await page.waitForSelector('.results-view .result-entry', { timeout: 40_000 });
  await page.click('.continue-buttons .btn-primary');
  await page.waitForSelector('.round-badge:has-text("Ronda 2")', { timeout: 20_000 });
  await page.waitForSelector('.info-row', { timeout: 10_000 });
  await page.waitForTimeout(800);
  ok(await desborde(page) <= 0, `sin scroll horizontal con el historial y la sesión a la vista (${await desborde(page)} px)`);
  const cols = await page.locator('.info-row .info-col').evaluateAll((els) => els.map((e) => { const r = e.getBoundingClientRect(); return { x: Math.round(r.x), w: Math.round(r.width), dentro: e.scrollWidth <= e.clientWidth + 1 }; }));
  ok(cols.every((c) => c.dentro), `ninguna tabla se sale de su tarjeta (${JSON.stringify(cols)})`);
  await page.screenshot({ path: join(SHOTS, `${PRE}04-mesa-ronda2.png`), fullPage: true });
  await page.click('.btn-leave');
  await page.click('.leave-confirm .btn-danger');
  // Sin esperar al lobby, cerrar el navegador corta la peticion de salida y la mesa se queda abierta.
  await page.waitForSelector('.lobby .game-title', { timeout: 15_000 });
} catch (e) {
  fallar(e);
} finally {
  await browser.close();
}
terminar();
