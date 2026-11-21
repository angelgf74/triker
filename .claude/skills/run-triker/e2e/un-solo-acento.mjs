// La base de color: un solo acento (el dorado) y nada de morado fuera de las cartas.
//
// El morado de la jugada competía con el dorado y además es casi el magenta de las
// cartas, que es un atributo que puntúa. Aquí se recorre todo lo pintado en el lobby,
// la mesa y los resultados buscando morados, y se comprueba que el botón principal
// es dorado liso, que los secundarios no usan el acento y que el texto suave se lee
// sobre el fondo y sobre los paneles. Vale contra producción: crea una mesa y sale.
import { join } from 'node:path';
import { chromium, WEB, MOVIL, SHOTS, ok, fallar, terminar } from './lib/comun.mjs';

const DORADO = 'rgb(240, 192, 64)';

// Todo lo morado que hay pintado fuera de las cartas: [selector, propiedad, color].
const morados = (page) => page.evaluate(() => {
  const esMorado = (c) => {
    const m = /rgba?\((\d+), (\d+), (\d+)(?:, ([\d.]+))?\)/.exec(c);
    if (!m) return false;
    const [r, g, b, a] = [Number(m[1]), Number(m[2]), Number(m[3]), m[4] === undefined ? 1 : Number(m[4])];
    return a > 0 && b > 140 && r > 110 && g < r - 8 && g < b - 40;
  };
  const vistos = new Set();
  for (const e of document.querySelectorAll('body *')) {
    if (e.closest('.card') || !e.getClientRects().length) continue;
    const s = getComputedStyle(e);
    for (const p of ['color', 'backgroundColor', 'borderTopColor']) {
      if (p === 'borderTopColor' && parseFloat(s.borderTopWidth) === 0) continue;
      if (esMorado(s[p])) vistos.add(`${e.className || e.tagName.toLowerCase()} ${p} ${s[p]}`);
    }
  }
  return [...vistos];
});

// Contraste WCAG entre dos colores #rrggbb.
function contraste(a, b) {
  const luz = (hex) => {
    const [r, g, bl] = [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16) / 255).map((v) => (v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4));
    return 0.2126 * r + 0.7152 * g + 0.0722 * bl;
  };
  const [x, y] = [luz(a), luz(b)].sort((p, q) => q - p);
  return (x + 0.05) / (y + 0.05);
}

const browser = await chromium.launch({ channel: 'chrome', headless: true });
try {
  const ctx = await browser.newContext({ ...MOVIL });
  const page = await ctx.newPage();
  page.on('pageerror', (e) => { if (!/ServiceWorker/.test(e.message)) console.log(`  [pageerror] ${e.message.slice(0, 160)}`); });

  console.log('== A. Lobby ==');
  await page.goto(WEB + '/', { waitUntil: 'load' });
  await page.waitForSelector('.lobby .game-title', { timeout: 90_000 });
  const estilo = (sel) => page.locator(sel).first().evaluate((e) => { const s = getComputedStyle(e); return { fondo: s.backgroundColor, imagen: s.backgroundImage, borde: s.borderTopColor, color: s.color }; });
  const jugar = await estilo('.btn-play-now');
  ok(jugar.fondo === DORADO && jugar.imagen === 'none', `el botón principal es dorado liso, sin degradado (${jugar.fondo}, ${jugar.imagen.slice(0, 40)})`);
  const nueva = await estilo('.btn-new-game'), ayuda = await estilo('.btn-help'), sug = await estilo('.btn-sug');
  ok([nueva, ayuda, sug].every((b) => b.borde !== DORADO && b.color !== DORADO && b.fondo !== DORADO),
     `los secundarios no usan el acento (nueva ${nueva.borde}, ayuda ${ayuda.borde}, sugerencias ${sug.borde})`);
  const tokens = await page.evaluate(() => { const s = getComputedStyle(document.documentElement); return Object.fromEntries(['fondo', 'panel', 'texto', 'texto-suave', 'acento', 'sobre-acento'].map((t) => [t, s.getPropertyValue('--' + t).trim()])); });
  const hex = (t) => /^#[0-9a-f]{6}$/i.test(tokens[t] ?? '');
  ok(['fondo', 'panel', 'texto', 'texto-suave', 'acento', 'sobre-acento'].every(hex), `los colores base están definidos en :root (${JSON.stringify(tokens)})`);
  if (['fondo', 'panel', 'texto-suave', 'acento', 'sobre-acento'].every(hex)) {
    const c1 = contraste(tokens['texto-suave'], tokens.fondo), c2 = contraste(tokens['texto-suave'], tokens.panel), c3 = contraste(tokens['sobre-acento'], tokens.acento);
    ok(c1 >= 4.5 && c2 >= 4.5, `el texto suave se lee sobre el fondo (${c1.toFixed(1)}:1) y sobre los paneles (${c2.toFixed(1)}:1)`);
    ok(c3 >= 7, `y el texto del botón principal sobre el dorado (${c3.toFixed(1)}:1)`);
  }
  let m = await morados(page);
  ok(m.length === 0, `nada morado en el lobby${m.length ? ': ' + m.join(' | ') : ''}`);

  console.log('== B. Mesa en tu turno ==');
  await page.fill('#nombre-jugador', 'Ana');
  await page.click('.btn-play-now');
  await page.waitForSelector('.turn-indicator.my-turn', { timeout: 20_000 });
  await page.waitForFunction(() => document.querySelectorAll('.hand-cards .card.dealing').length === 0, null, { timeout: 10_000 });
  ok(await page.locator('.my-play-badge .grp-hand-name').count() === 1, 'la jugada de tu mano está a la vista');
  m = await morados(page);
  ok(m.length === 0, `nada morado en la mesa${m.length ? ': ' + m.join(' | ') : ''}`);
  await page.locator('.hand-cards .card').first().click();
  await page.waitForSelector('.btn-discard');
  await page.waitForTimeout(400);   // el botón cambia de clase con una transición de 0,15 s
  const descartar = await estilo('.btn-discard');
  ok(descartar.fondo === DORADO && descartar.imagen === 'none', `«Descartar» es la acción principal, en dorado (${descartar.fondo}, ${descartar.imagen.slice(0, 40)})`);
  await page.screenshot({ path: join(SHOTS, 'acento-mesa.png') });
  await page.locator('.hand-cards .card').first().click();

  console.log('== C. Resultados ==');
  await page.click('.btn-pass');
  await page.waitForSelector('.results-view .result-entry', { timeout: 40_000 });
  await page.waitForTimeout(600);
  m = await morados(page);
  ok(m.length === 0, `nada morado en los resultados${m.length ? ': ' + m.join(' | ') : ''}`);
  await page.screenshot({ path: join(SHOTS, 'acento-resultados.png'), fullPage: true });

  await page.click('button:has-text("Salir de la mesa")');
  await page.waitForSelector('.lobby .game-title', { timeout: 15_000 });
  await page.screenshot({ path: join(SHOTS, 'acento-lobby.png') });
} catch (e) {
  fallar(e);
} finally {
  await browser.close();
}
terminar();
