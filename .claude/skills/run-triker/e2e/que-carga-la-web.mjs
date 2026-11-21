// Inventario, no prueba: qué carga la web y a dónde conecta en un recorrido completo
// (lobby, ayuda, buzón sin enviar, partida, resultados, imagen de compartir).
//
// Es la lista que tiene que permitir la política de seguridad (deploy/csp.mjs).
// Pasarlo cuando se añada algo que cargue o llame a otro sitio, y comparar con lo
// que la política permite. Pensado para producción, donde están las direcciones de
// verdad; crea una mesa de prueba y sale de ella.
//
//   TRIKER_WEB=https://triker-app.angelgf.com.es node que-carga-la-web.mjs
import { chromium, WEB, MOVIL } from './lib/comun.mjs';

const browser = await chromium.launch({ channel: 'chrome', headless: true });
const ctx = await browser.newContext({ ...MOVIL });
const page = await ctx.newPage();
const visto = new Map();   // "tipo origen" -> veces
const apunta = (tipo, url) => {
  let origen;
  try { const u = new URL(url); origen = ['data:', 'blob:'].includes(u.protocol) ? u.protocol : u.origin; } catch { origen = url.slice(0, 30); }
  const k = `${tipo.padEnd(11)} ${origen}`;
  visto.set(k, (visto.get(k) ?? 0) + 1);
};
page.on('request', (r) => apunta(r.resourceType(), r.url()));
page.on('websocket', (ws) => apunta('websocket', ws.url()));

try {
  await page.goto(WEB + '/', { waitUntil: 'load' });
  await page.waitForSelector('.lobby .game-title', { timeout: 90_000 });
  const html = await page.evaluate(() => ({
    enLinea: [...document.scripts].filter((s) => !s.src).map((s) => `${s.type || 'js'} (${s.textContent.trim().length} caracteres)`),
    externos: [...document.scripts].filter((s) => s.src).map((s) => new URL(s.src).origin + new URL(s.src).pathname.replace(/\.[a-z0-9]{10}\./, '.*.')),
    politica: document.querySelector('meta[http-equiv="Content-Security-Policy"]')?.content ?? '(ninguna)',
  }));
  console.log('POLÍTICA ACTUAL  :', html.politica);
  console.log('SCRIPTS EN LÍNEA :', html.enLinea.join(' | ') || '(ninguno)');
  console.log('SCRIPTS EXTERNOS :', html.externos.join(' | '));

  await page.click('.btn-help'); await page.waitForSelector('.help-modal'); await page.keyboard.press('Escape');
  await page.click('.btn-sug'); await page.waitForTimeout(500); await page.keyboard.press('Escape');
  await page.fill('#nombre-jugador', 'Prueba');
  await page.click('.btn-play-now');
  await page.waitForSelector('.turn-indicator.my-turn', { timeout: 20_000 });
  console.log(`ESTILOS EN LÍNEA : ${await page.evaluate(() => document.querySelectorAll('[style]').length)} atributos style en la mesa`);
  await page.click('.btn-pass');
  await page.waitForSelector('.results-view .result-entry', { timeout: 40_000 });
  const imagen = await page.evaluate(async () => {
    try { return String(await window.TrikerHandImage.render([{ letter: 'T', figure: '●', color: '#e53935', groupColor: null }], 'Prueba', 1, 'Prueba')).slice(0, 22); }
    catch (e) { return 'error: ' + e.message; }
  });
  console.log('IMAGEN COMPARTIR :', imagen);
  await page.click('button:has-text("Salir de la mesa")');
  await page.waitForSelector('.lobby .game-title', { timeout: 15_000 });
  await page.waitForTimeout(1500);
} catch (e) {
  console.log('ERROR:', e.message.slice(0, 200));
} finally {
  await browser.close();
}
console.log('PETICIONES POR TIPO Y ORIGEN:');
for (const [k, n] of [...visto].sort()) console.log(`  ${k}  (${n})`);
