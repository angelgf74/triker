// El texto se lee: contraste WCAG de al menos 4,5:1 (3:1 si es grande) contra el fondo
// que tiene detrás de verdad, medido en la página pintada y no en la hoja de estilos. Un
// gris que vale sobre el fondo de la página (#888 da 4,9:1) no llega sobre un panel (4:1),
// y eso solo se ve midiendo cada texto donde cae.
// Recorre los diálogos de ayuda y de sugerencias, lobby con una partida, formulario, sala
// de espera, mesa, confirmación de salida, resultados y la segunda ronda, y lo que ve un
// espectador de la mesa y de los resultados. No mide las cartas, tampoco las de ejemplo de
// la ayuda (su color es un atributo del juego), los emojis ni los controles desactivados,
// que la norma deja fuera.
// Vale contra producción: crea dos mesas y sale de ellas.
import { chromium, WEB, MOVIL, ok, espera, fallar, terminar } from './lib/comun.mjs';

const flojos = (page) => page.evaluate(() => {
  const rgba = (s) => { const m = s.match(/[\d.]+/g).map(Number); return { r: m[0], g: m[1], b: m[2], a: m[3] ?? 1 }; };
  const sobre = (c, f) => ({ r: c.r * c.a + f.r * (1 - c.a), g: c.g * c.a + f.g * (1 - c.a), b: c.b * c.a + f.b * (1 - c.a), a: 1 });
  const luz = ({ r, g, b }) => { const [x, y, z] = [r, g, b].map((v) => v / 255).map((v) => (v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4)); return 0.2126 * x + 0.7152 * y + 0.0722 * z; };
  const hex = ({ r, g, b }) => '#' + [r, g, b].map((v) => Math.round(v).toString(16).padStart(2, '0')).join('');
  const vistos = new Map();
  for (const e of document.querySelectorAll('body *')) {
    if (e.closest('.card, .help-mini-card') || e.closest('svg') || e.closest(':disabled')) continue;
    const texto = [...e.childNodes].filter((n) => n.nodeType === 3).map((n) => n.textContent).join('').trim();
    if (!/[\p{L}\p{N}\p{Sm}]/u.test(texto)) continue;   // sin letras, cifras ni un «+»: vacío, un signo o un emoji
    const caja = e.getBoundingClientRect();
    if (caja.width === 0 || caja.height === 0) continue;
    const s = getComputedStyle(e);
    // El logo: letras transparentes sobre un degradado propio.
    if (s.visibility === 'hidden' || s.webkitTextFillColor === 'rgba(0, 0, 0, 0)') continue;
    // El fondo de verdad: las capas de color de los antepasados, de fuera adentro.
    const capas = []; let opacidad = 1;
    for (let p = e; p; p = p.parentElement) {
      const ps = getComputedStyle(p);
      capas.unshift(rgba(ps.backgroundColor));
      opacidad *= parseFloat(ps.opacity);
    }
    let fondo = { r: 255, g: 255, b: 255, a: 1 };
    for (const c of capas) fondo = sobre(c, fondo);
    const tinta = rgba(s.color); tinta.a *= opacidad;
    const pintado = sobre(tinta, fondo);
    const [a, b] = [luz(pintado), luz(fondo)].sort((x, y) => y - x);
    const c = (a + 0.05) / (b + 0.05);
    const px = parseFloat(s.fontSize), grande = px >= 24 || (px >= 18.66 && parseInt(s.fontWeight) >= 700);
    if (c >= (grande ? 3 : 4.5)) continue;
    const clave = `${(e.className || e.tagName).toString().trim().slice(0, 36)} ${hex(pintado)} sobre ${hex(fondo)}`;
    if (!vistos.has(clave)) vistos.set(clave, `«${texto.slice(0, 24)}» ${c.toFixed(2)}:1 (${clave}, ${px} px)`);
  }
  return [...vistos.values()].sort();
});
const medir = async (page, donde) => {
  await page.mouse.move(0, 0);       // sin :hover pegado a lo último que se tocó
  await page.waitForTimeout(450);    // y con las transiciones de color acabadas
  const f = await flojos(page);
  ok(f.length === 0, `${donde}: todo el texto llega al contraste mínimo${f.length ? ' (no llegan: ' + f.join(' | ') + ')' : ''}`);
};

const browser = await chromium.launch({ channel: 'chrome', headless: true });
const entrar = async (nombre) => {
  const ctx = await browser.newContext({ ...MOVIL });
  const page = await ctx.newPage();
  page.on('pageerror', (e) => { if (!/ServiceWorker/.test(e.message)) console.log(`  [pageerror] ${e.message.slice(0, 160)}`); });
  await page.goto(WEB + '/', { waitUntil: 'load' });
  await page.waitForSelector('.lobby .game-title', { timeout: 90_000 });
  await page.fill('#nombre-jugador', nombre);
  return page;
};
// El nombre de Ana lleva número: su partida contra bots se llama «Partida de <nombre>» y la
// espectadora tiene que dar con ella en el lobby, también en producción.
const N = Date.now() % 100000, MESA = `Contraste ${N}`, ANA = `Ana ${N}`;
let berto;
try {
  console.log('== A. Diálogos, sala de espera y lobby con una partida ==');
  berto = await entrar('Berto');
  await berto.click('.btn-help');
  await berto.waitForSelector('.help-modal');
  await medir(berto, 'diálogo de ayuda');
  await berto.click('.help-close');
  await berto.click('.btn-sug');
  await berto.waitForSelector('.sug-modal');
  await medir(berto, 'diálogo de sugerencias');
  await berto.click('.help-close');
  // Berto abre una mesa: su sala de espera es una pantalla y su fila en el lobby de Ana, otra.
  await berto.click('.btn-new-game');
  await berto.fill('.create-form .name-input', MESA);
  await medir(berto, 'formulario de nueva partida');
  await berto.click('.create-form .btn-primary');
  await berto.waitForSelector('.waiting-room');
  await medir(berto, 'sala de espera');

  const page = await entrar(ANA);
  const carla = await entrar('Carla');
  await page.click('.btn-refresh');
  await page.locator(`.game-card:has-text("${MESA}")`).waitFor({ timeout: 10_000 });
  await medir(page, 'lobby con una partida en la lista');

  console.log('== B. Mesa ==');
  await page.click('.btn-play-now');
  await page.waitForSelector('.turn-indicator.my-turn', { timeout: 20_000 });
  await page.waitForFunction(() => document.querySelectorAll('.hand-cards .card.dealing').length === 0, null, { timeout: 10_000 });
  await medir(page, 'mesa en tu turno');
  await page.click('.btn-leave');
  await page.waitForSelector('.leave-confirm');
  await medir(page, 'confirmación de salida');
  await page.click('.leave-confirm .btn-secondary');

  console.log('== C. Lo que ve un espectador ==');
  await carla.click('.btn-refresh');
  await carla.click(`.game-card:has-text("Partida de ${ANA}") .btn-spectate`, { timeout: 10_000 });
  await carla.waitForSelector('.spectator-badge', { timeout: 15_000 });
  await carla.waitForSelector('.other-player', { timeout: 10_000 });
  await medir(carla, 'mesa vista por un espectador');

  console.log('== D. Resultados y segunda ronda ==');
  await page.click('.btn-pass');
  await page.waitForSelector('.results-view .result-entry', { timeout: 40_000 });
  await medir(page, 'resultados');
  // «Esperando a los jugadores…»: la misma línea gris que ve quien ya decidió y espera al resto.
  await carla.waitForSelector('.results-view .waiting-others', { timeout: 10_000 });
  await medir(carla, 'resultados vistos por un espectador');
  await carla.click('.results-view .btn-secondary:has-text("Dejar de ver")');
  await carla.waitForSelector('.lobby .game-title', { timeout: 15_000 });
  await page.click('.continue-buttons .btn-primary');
  await page.waitForSelector('.round-badge:has-text("Ronda 2")', { timeout: 20_000 });
  await page.waitForSelector('.info-row', { timeout: 10_000 });
  // En la segunda ronda empieza un rival: es cuando se ve «Turno de …» sobre el tapete.
  const turnoDeOtro = await espera(page.waitForSelector('.turn-indicator.waiting-turn', { timeout: 5_000 }));
  const f = turnoDeOtro ? await flojos(page) : null;
  ok(turnoDeOtro, 'en la segunda ronda se llega a ver el turno de un rival');
  if (f) ok(f.length === 0, `mesa con el turno de un rival, historial y sesión: todo el texto llega al contraste mínimo${f.length ? ' (no llegan: ' + f.join(' | ') + ')' : ''}`);
  await page.waitForSelector('.turn-indicator.my-turn', { timeout: 30_000 });
  await page.click('.btn-pass');
  await page.waitForSelector('.results-view .result-entry', { timeout: 40_000 });
  await medir(page, 'resultados de la segunda ronda, con la tabla de la sesión');
  await page.click('.continue-buttons .btn-secondary');
  await page.waitForSelector('.lobby .game-title', { timeout: 15_000 });
} catch (e) {
  fallar(e);
} finally {
  // Sin esperar al lobby, cerrar el navegador corta la petición de salida y la mesa se queda abierta.
  if (berto) await espera(berto.click('.btn-leave').then(() => berto.waitForSelector('.lobby .game-title', { timeout: 15_000 })));
  await browser.close();
}
terminar();
