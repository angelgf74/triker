// Tope de partidas abiertas por IP, contra la API real en local. Crea partidas
// hasta que la API diga basta. Tarda unos 6 min: solo deja crear 10 por minuto.
import { chromium, WEB, API, MOVIL, ok, sleep, fallar, terminar, soloLocal } from './lib/comun.mjs';

soloLocal();

const TOPE = 40;

async function crear(i) {
  const res = await fetch(`${API}/api/games`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ playerName: 'Tope', gameName: `Tope ${i}` }),
  });
  return { status: res.status, cuerpo: res.ok ? await res.json() : await res.text() };
}
// Crea respetando el límite por minuto: si la API pide esperar, espera y repite.
async function crearConPaciencia(i) {
  for (;;) {
    const r = await crear(i);
    if (r.status === 429 && /Demasiadas peticiones/.test(r.cuerpo)) { await sleep(10_000); continue; }
    return r;
  }
}

const creadas = [];
let rechazo = null;
const t0 = Date.now();
for (let i = 1; i <= TOPE + 3 && !rechazo; i++) {
  const r = await crearConPaciencia(i);
  if (r.status === 200) creadas.push(r.cuerpo);
  else rechazo = r;
  if (creadas.length % 10 === 0 && !rechazo) console.log(`  … ${creadas.length} creadas (${Math.round((Date.now() - t0) / 1000)} s)`);
}
ok(creadas.length === TOPE, `la API deja abrir ${TOPE} partidas desde una IP (abiertas: ${creadas.length})`);
ok(rechazo?.status === 429 && /partidas abiertas desde tu conexión/.test(rechazo?.cuerpo ?? ''), `y rechaza la siguiente con su propio aviso: ${rechazo?.status} «${rechazo?.cuerpo}»`);

console.log('== La web enseña ese aviso ==');
await sleep(61_000);                                 // ventana nueva del límite por minuto
const browser = await chromium.launch({ channel: 'chrome', headless: true });
try {
  const ctx = await browser.newContext({ ...MOVIL });
  const page = await ctx.newPage();
  await page.goto(WEB + '/', { waitUntil: 'load' });
  await page.waitForSelector('.lobby .game-title', { timeout: 90_000 });
  await page.fill('#nombre-jugador', 'Ana');
  await page.click('.btn-play-now');
  const aviso = await page.locator('.error-msg, [role=alert]').first().textContent({ timeout: 8000 }).catch(() => null);
  ok(/partidas abiertas desde tu conexión/.test(aviso ?? ''), `«Jugar contra bots» muestra el motivo: «${aviso?.trim()}»`);
  ok(!page.url().includes('/game/'), 'y se queda en el lobby');

  console.log('== Al cerrar una partida se recupera el sitio ==');
  const una = creadas[0];
  const sale = await fetch(`${API}/api/games/${una.gameState.id}/leave?playerId=${una.playerId}`, { method: 'POST', headers: { 'X-Triker-Token': una.playerToken } });
  ok(sale.ok, `la creadora sale de una sala y esta se cierra (${sale.status})`);
  await page.click('.btn-play-now');
  ok(await page.waitForSelector('.turn-indicator.my-turn', { timeout: 20_000 }).then(() => true, () => false), 'y ya se puede crear otra');
} catch (e) {
  fallar(e);
} finally {
  await browser.close();
}
terminar();
