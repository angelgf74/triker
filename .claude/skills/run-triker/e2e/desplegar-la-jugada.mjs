// La etiqueta de una jugada con varios grupos («Doble Pareja ▾») los despliega. Se
// toca, así que tiene que ser un botón: con el dedo mide 44 px y con el teclado se
// llega a ella y se abre. Antes era un <span> con un clic de 26 px y sin teclado.
// La pastilla que se ve no crece ni tapa lo de al lado.
//
// Admite producción: crea una mesa contra bots y la cierra al salir.
import { join } from 'node:path';
import { chromium, WEB, MOVIL, SHOTS, RUTA_CREAR, ok, fallar, terminar } from './lib/comun.mjs';

// La mesa llena: con 16 manos es casi seguro que alguna tiene dos grupos (en las
// medidas salieron 11 de 16 y 3 de 8; con 8 manos fallaría alguna vez por azar).
const BOTS = 15;

const browser = await chromium.launch({ channel: 'chrome', headless: true });
try {
  const ctx = await browser.newContext({ ...MOVIL });
  const page = await ctx.newPage();
  page.on('pageerror', (e) => { if (!/ServiceWorker/.test(e.message)) console.log(`  [pageerror] ${e.message.slice(0, 160)}`); });
  await page.route(RUTA_CREAR, async (route) => {
    if (route.request().method() !== 'POST') return route.continue();
    await route.continue({ postData: JSON.stringify({ ...route.request().postDataJSON(), bots: BOTS }) });
  });
  await page.goto(WEB + '/', { waitUntil: 'load' });
  await page.waitForSelector('.lobby .game-title', { timeout: 90_000 });
  await page.fill('#nombre-jugador', 'Ana');
  await page.click('.btn-play-now');
  await page.waitForSelector('.turn-indicator.my-turn', { timeout: 20_000 });
  await page.waitForFunction(() => document.querySelectorAll('.hand-cards .card.dealing').length === 0, null, { timeout: 10_000 });

  // En la mesa solo se puede medir si a ti te ha tocado una mano con dos grupos.
  const enLaMesa = await page.evaluate(() => {
    const e = document.querySelector('.my-play-badge .grp-hand-name.expandable');
    if (!e) return null;
    return { alto: e.getBoundingClientRect().height, fila: document.querySelector('.my-play-badge .result-badges').getBoundingClientRect().height };
  });
  if (enLaMesa) {
    ok(enLaMesa.alto >= 44, `en la mesa, «Tu jugada» mide ${enLaMesa.alto} px de alto`);
    ok(enLaMesa.fila <= 28, `y su fila no crece (${enLaMesa.fila} px)`);
  } else {
    console.log('  (tu mano no tiene dos grupos: «Tu jugada» no se mide en la mesa esta vez)');
  }

  await page.click('.btn-pass');
  await page.waitForSelector('.results-view .result-entry', { timeout: 60_000 });
  if (await page.locator('.btn-open-all').count()) await page.click('.btn-open-all');
  await page.waitForFunction(() => !document.querySelector('.result-collapsed'), null, { timeout: 5000 });

  const etiquetas = page.locator('.results-view .grp-hand-name.expandable');
  const n = await etiquetas.count();
  ok(n > 0, `en los resultados hay manos con grupos que desplegar (${n} de ${BOTS + 1})`);
  const etiqueta = etiquetas.first();
  await etiqueta.scrollIntoViewIfNeeded();

  const m = await etiqueta.evaluate((e) => {
    const r = e.getBoundingClientRect();
    const pastilla = parseFloat(getComputedStyle(e, '::before').height) || r.height;
    const fila = e.closest('.result-badges').getBoundingClientRect().height;
    // Lo que se toca al lado: no puede quedar debajo del área de la etiqueta.
    const tapa = [...document.querySelectorAll('button, a[href]')].filter((o) => {
      if (o === e || e.contains(o) || o.contains(e)) return false;
      const q = o.getBoundingClientRect();
      return q.width > 0 && q.left < r.right && q.right > r.left && q.top < r.bottom && q.bottom > r.top;
    }).map((o) => o.className || o.tagName);
    return { tag: e.tagName, alto: Math.round(r.height * 10) / 10, ancho: Math.round(r.width), pastilla: Math.round(pastilla * 10) / 10, fila: Math.round(fila * 10) / 10, tapa, expandido: e.getAttribute('aria-expanded') };
  });
  ok(m.tag === 'BUTTON', `es un botón (<${m.tag.toLowerCase()}>)`);
  ok(m.alto >= 44 && m.ancho >= 44, `mide al menos 44 px para el dedo (${m.ancho}×${m.alto} px)`);
  ok(m.pastilla <= 28 && m.fila <= 28, `la pastilla que se ve sigue igual de baja (${m.pastilla} px) y su fila no crece (${m.fila} px)`);
  ok(m.tapa.length === 0, `no tapa ningún otro control (${JSON.stringify(m.tapa)})`);
  ok(m.expandido === 'false', `dice que está plegada (aria-expanded=${m.expandido})`);

  const desglose = () => etiqueta.evaluate((e) => ({
    abierto: !!e.closest('.result-badges').querySelector('.grp-breakdown'),
    expandido: e.getAttribute('aria-expanded'),
  }));
  const enfocada = await etiqueta.evaluate((e) => { e.focus(); return document.activeElement === e; });
  ok(enfocada, 'el teclado llega a ella');
  await page.keyboard.press('Enter');
  let d = await desglose();
  ok(d.abierto && d.expandido === 'true', `Enter despliega los grupos (desglose ${d.abierto ? 'abierto' : 'cerrado'}, aria-expanded=${d.expandido})`);
  await page.screenshot({ path: join(SHOTS, 'desplegar-abierta.png') });
  await page.keyboard.press('Space');
  d = await desglose();
  ok(!d.abierto && d.expandido === 'false', `espacio los vuelve a plegar (desglose ${d.abierto ? 'abierto' : 'cerrado'}, aria-expanded=${d.expandido})`);
  await etiqueta.tap();
  d = await desglose();
  ok(d.abierto, 'y con el dedo también se despliega');

  await page.click('button:has-text("Salir de la mesa")', { timeout: 5000 });
  // Esperar al lobby: cerrar el navegador antes corta la petición de salida y la mesa se queda abierta.
  await page.waitForSelector('.lobby .game-title', { timeout: 15_000 });
} catch (e) {
  fallar(e);
} finally {
  await browser.close();
}
terminar();
