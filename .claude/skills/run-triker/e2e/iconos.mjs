// Los iconos de la interfaz son dibujos de línea (componente Icono), no emojis.
//
// Un emoji lo pinta el sistema: cambia de un móvil a otro, no hereda el color del
// texto y no se puede alinear con él. Dos comprobaciones:
//   A. En el código: ningún .razor de la web lleva emojis fuera de lo permitido
//      (las reacciones y los textos de compartir, que son contenido, y las medallas).
//   B. En pantalla: los botones llevan su icono y no queda ningún emoji pintado en
//      el lobby, la sala de espera, la mesa ni los resultados.
// Solo contra localhost: la parte A lee el código del repo.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium, WEB, MOVIL, SHOTS, ok, fallar, terminar, soloLocal } from './lib/comun.mjs';

soloLocal();

const WEB_SRC = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..', '..', '..', 'Triker.Web');
const EMOJI = /\p{Extended_Pictographic}/gu;
// Las figuras de las cartas son contenido del juego; las medallas siguen siendo emojis.
const PERMITIDOS = new Set(['♥', '♣', '♦', '♠', '🥇', '🥈', '🥉']);
// Líneas cuyo emoji es contenido que viaja fuera de la app o que elige el jugador.
const LINEA_PERMITIDA = /_emojis\s*=|var text = \$"|^\s*(\? )?\$?"(👉|🔒)/u;

console.log('== A. En el código ==');
const sobran = [];
const usados = new Map();   // nombre de icono -> dónde se usa
(function andar(dir) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    if (['bin', 'obj', 'wwwroot'].includes(e.name)) continue;
    const p = path.join(dir, e.name);
    if (e.isDirectory()) andar(p);
    else if (e.name.endsWith('.razor')) {
      fs.readFileSync(p, 'utf8').split(/\r?\n/).forEach((linea, i) => {
        // Los nombres van literales o en un condicional: Nombre="x" / Nombre="@(c ? "x" : "y")".
        for (const uso of linea.match(/<Icono Nombre="(@\([^)]*\)|[^"]*)"/g) ?? []) {
          const valor = uso.slice('<Icono Nombre="'.length, -1);
          const nombres = valor.startsWith('@(') ? [...valor.matchAll(/"([a-z]+)"/g)].map((m) => m[1]) : [valor];
          for (const n of nombres) usados.set(n, `${path.basename(p)}:${i + 1}`);
        }
        if (LINEA_PERMITIDA.test(linea)) return;
        const malos = [...new Set(linea.match(EMOJI) ?? [])].filter((c) => !PERMITIDOS.has(c));
        if (malos.length) sobran.push(`${path.basename(p)}:${i + 1} ${malos.join('')}`);
      });
    }
  }
})(WEB_SRC);
ok(sobran.length === 0, `ningún emoji de interfaz en los .razor${sobran.length ? ': ' + sobran.join(' | ') : ''}`);
// Un nombre mal escrito lanza al pintar, y hay iconos que solo salen en pantallas raras
// (fin de partida, turno agotado, espectador): se comprueban todos aquí.
const definidos = new Set([...fs.readFileSync(path.join(WEB_SRC, 'Components', 'Icono.razor'), 'utf8').matchAll(/\["([a-z]+)"\] = \[/g)].map((m) => m[1]));
const sinDefinir = [...usados].filter(([n]) => !definidos.has(n)).map(([n, donde]) => `${n} (${donde})`);
ok(usados.size >= 10 && sinDefinir.length === 0, `los ${usados.size} iconos que se usan existen en Icono.razor${sinDefinir.length ? '; faltan: ' + sinDefinir.join(', ') : ''}`);

// Emojis pintados fuera de las cartas, las reacciones y las medallas.
const pintados = (page) => page.evaluate(() => {
  const permitidos = new Set(['♥', '♣', '♦', '♠', '🥇', '🥈', '🥉']);
  const vistos = new Set();
  const w = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
  for (let n = w.nextNode(); n; n = w.nextNode()) {
    const el = n.parentElement;
    if (!el || el.closest('.card, .reaction-toolbar, .reaction-bubble, .help-mini-card, script, style') || !el.getClientRects().length) continue;
    for (const c of n.textContent.match(/\p{Extended_Pictographic}/gu) ?? []) if (!permitidos.has(c)) vistos.add(`${c} en .${el.className || el.tagName.toLowerCase()}`);
  }
  return [...vistos];
});
const conIcono = (page, sel) => page.locator(`${sel} svg.icono`).count().then((n) => n >= 1);
// El icono toma el color del texto de su botón. Un botón sin color propio usa el negro
// por defecto del navegador, y ahí el icono no se vería sobre el fondo oscuro (el emoji
// traía sus colores y lo tapaba).
const seVe = (page, sel) => page.locator(`${sel} svg.icono`).first().evaluate((e) => {
  const color = getComputedStyle(e).color;
  const caja = e.getBoundingClientRect();
  return color === getComputedStyle(e.closest('button')).color && color !== 'rgb(0, 0, 0)' && caja.width >= 12 && caja.height >= 12;
});

const browser = await chromium.launch({ channel: 'chrome', headless: true });
try {
  const ctx = await browser.newContext({ ...MOVIL });
  const page = await ctx.newPage();
  page.on('pageerror', (e) => { if (!/ServiceWorker/.test(e.message)) console.log(`  [pageerror] ${e.message.slice(0, 200)}`); });

  console.log('== B. Lobby ==');
  await page.goto(WEB + '/', { waitUntil: 'load' });
  await page.waitForSelector('.lobby .game-title', { timeout: 90_000 });
  for (const [sel, que] of [['.btn-sug', 'sugerencias'], ['.btn-play-now', '«Jugar contra bots»'], ['.btn-refresh', 'actualizar']]) {
    ok(await conIcono(page, sel) && await seVe(page, sel), `${que} lleva su icono y se ve`);
  }
  await page.fill('#nombre-jugador', 'Ana');
  await page.click('.btn-new-game');
  await page.waitForSelector('.create-form');
  ok(await conIcono(page, '.private-toggle'), 'la casilla de partida privada lleva el candado');
  let e = await pintados(page);
  ok(e.length === 0, `ningún emoji pintado en el lobby${e.length ? ': ' + e.join(' | ') : ''}`);
  await page.screenshot({ path: join('iconos-lobby') });

  console.log('== C. Sala de espera privada ==');
  await page.fill('.create-form .name-input', `Iconos ${Date.now() % 100000}`);
  await page.check('.private-toggle input');
  await page.click('.create-form .btn-primary');
  await page.waitForSelector('.waiting-room');
  for (const [sel, que] of [['.private-code-label', 'el código privado'], ['.btn-copy-code', 'copiar'], ['.btn-invite', 'invitar'], ['.btn-start', 'iniciar']]) {
    ok(await conIcono(page, sel), `${que} lleva su icono`);
  }
  e = await pintados(page);
  ok(e.length === 0, `ningún emoji pintado en la sala de espera${e.length ? ': ' + e.join(' | ') : ''}`);
  await page.screenshot({ path: join('iconos-sala') });

  console.log('== D. Mesa y resultados ==');
  await page.click('.waiting-actions .btn-secondary');   // + bot
  await page.waitForFunction(() => document.querySelectorAll('.waiting-player').length === 2);
  await page.click('.btn-start');
  await page.waitForSelector('.turn-indicator.my-turn', { timeout: 30_000 });
  ok(await conIcono(page, '.btn-mute') && await seVe(page, '.btn-mute'), 'el botón de sonido lleva su icono y se ve');
  const antes = await page.locator('.btn-mute svg.icono').innerHTML();
  await page.click('.btn-mute');
  ok(await page.locator('.btn-mute svg.icono').innerHTML() !== antes, 'y cambia al silenciar');
  await page.click('.btn-mute');
  e = await pintados(page);
  ok(e.length === 0, `ningún emoji pintado en la mesa${e.length ? ': ' + e.join(' | ') : ''}`);
  await page.screenshot({ path: join('iconos-mesa') });
  await page.click('.btn-pass');
  await page.waitForSelector('.results-view .result-entry', { timeout: 40_000 });
  await page.waitForTimeout(500);
  ok(await conIcono(page, '.btn-share') && await seVe(page, '.btn-share'), '«Compartir» lleva su icono y se ve');
  e = await pintados(page);
  ok(e.length === 0, `ningún emoji pintado en los resultados${e.length ? ': ' + e.join(' | ') : ''}`);
  await page.screenshot({ path: join('iconos-resultados'), fullPage: true });

  await page.click('button:has-text("Salir de la mesa")');
  await page.waitForSelector('.lobby .game-title', { timeout: 15_000 });
} catch (err) {
  fallar(err);
} finally {
  await browser.close();
}
terminar();

function join(nombre) { return path.join(SHOTS, `${nombre}.png`); }
