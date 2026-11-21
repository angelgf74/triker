// Humo: lo mínimo que tiene que funcionar. Una partida contra bots de principio a
// fin por la interfaz, y la mesa cerrada al salir. Es lo que se pasa contra
// producción justo después de publicar:
//
//   TRIKER_WEB=https://triker-app.angelgf.com.es TRIKER_API=https://triker-api.angelgf.com.es node humo.mjs
//
// Crea una sola mesa de prueba, pública mientras dura (unos 10 s), y sale de ella.
import { join } from 'node:path';
import { chromium, WEB, API, MOVIL, SHOTS, ok, espera, fallar, terminar } from './lib/comun.mjs';

const browser = await chromium.launch({ channel: 'chrome', headless: true });
let id = null;
try {
  const ctx = await browser.newContext({ ...MOVIL });
  const page = await ctx.newPage();
  page.on('pageerror', (e) => { if (!/ServiceWorker/.test(e.message)) console.log(`  [pageerror] ${e.message.slice(0, 160)}`); });
  const creaciones = [];
  page.on('request', (r) => { if (r.method() === 'POST' && /\/api\/games$/.test(new URL(r.url()).pathname)) creaciones.push(r.url()); });
  await page.goto(WEB + '/', { waitUntil: 'load' });
  ok(await espera(page.waitForSelector('.lobby .game-title', { timeout: 90_000 })), 'la web carga y pinta el lobby');
  await page.screenshot({ path: join(SHOTS, 'humo-lobby.png') });

  await page.fill('#nombre-jugador', 'Prueba');
  const t0 = Date.now();
  await page.click('.btn-play-now');
  ok(await espera(page.waitForSelector('.turn-indicator.my-turn', { timeout: 20_000 })), `«Jugar contra bots»: un toque y es tu turno (${Date.now() - t0} ms)`);
  id = page.url().match(/game\/([0-9a-f-]{36})/)?.[1];
  ok(await page.locator('.other-player').count() === 3 && await page.locator('.hand-cards .card').count() === 5, '3 bots en la mesa y tus 5 cartas');
  ok(creaciones.length === 1, `con una sola petición de creación (${creaciones.length})`);

  await page.click('.btn-pass');
  ok(await espera(page.waitForSelector('.results-view .result-entry', { timeout: 40_000 })), 'los bots juegan y los resultados llegan en directo');
  ok(await page.locator('.results-view .result-entry').count() === 4, 'con los cuatro jugadores');
  await page.screenshot({ path: join(SHOTS, 'humo-resultados.png') });

  await page.click('button:has-text("Salir de la mesa")', { timeout: 5000 });
  // Esperar al lobby: cerrar el navegador antes corta la petición de salida y la mesa se queda abierta.
  await page.waitForSelector('.lobby .game-title', { timeout: 15_000 });
} catch (e) {
  fallar(e);
} finally {
  await browser.close();
}
const abiertas = await (await fetch(`${API}/api/games`)).json();
ok(id && !abiertas.some((g) => g.id === id), 'al salir, la mesa de prueba se cierra (no queda en la API)');
terminar();
