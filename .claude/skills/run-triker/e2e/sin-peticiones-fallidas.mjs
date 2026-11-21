// Ninguna petición a la propia web falla al cargar el lobby, una mesa y los
// resultados. Caza lo que queda colgando al borrar o mover un fichero de wwwroot
// (se usó al quitar wwwroot/lib/bootstrap y NavMenu). Solo contra localhost.
import { chromium, WEB, MOVIL, ok, fallar, terminar, soloLocal } from './lib/comun.mjs';

soloLocal();

const browser = await chromium.launch({ channel: 'chrome', headless: true });
try {
  const ctx = await browser.newContext({ ...MOVIL });
  const page = await ctx.newPage();
  const origen = new URL(WEB).origin;
  const malas = [];
  page.on('response', (r) => { if (r.status() >= 400 && r.url().startsWith(origen)) malas.push(`${r.status()} ${new URL(r.url()).pathname}`); });
  await page.goto(WEB + '/', { waitUntil: 'load' });
  await page.waitForSelector('.lobby .game-title', { timeout: 90_000 });
  await page.fill('#nombre-jugador', 'Ana');
  await page.click('.btn-play-now');
  await page.waitForSelector('.turn-indicator.my-turn', { timeout: 20_000 });
  await page.click('.btn-pass');
  await page.waitForSelector('.results-view .result-entry', { timeout: 40_000 });
  ok(malas.length === 0, `ninguna petición de la web falla (lobby, mesa y resultados)${malas.length ? ': ' + malas.join(', ') : ''}`);
  await page.click('button:has-text("Salir de la mesa")');
  await page.waitForSelector('.lobby .game-title', { timeout: 15_000 });
} catch (e) {
  fallar(e);
} finally {
  await browser.close();
}
terminar();
