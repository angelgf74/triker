// La fuente del logo se sirve desde la propia web: ninguna petición a Google al abrir la página.
// Por defecto contra localhost; TRIKER_WEB para producción.
import { join } from 'node:path';
import { chromium, WEB, MOVIL, SHOTS, ok, fallar, terminar } from './lib/comun.mjs';

const PRE = process.env.SHOT_PRE ?? 'fuente-';

const browser = await chromium.launch({ channel: 'chrome', headless: true });
try {
  const ctx = await browser.newContext({ ...MOVIL });
  const page = await ctx.newPage();
  const aGoogle = [], fuentes = [];
  page.on('request', (r) => { if (/fonts\.(googleapis|gstatic)\.com/.test(r.url())) aGoogle.push(new URL(r.url()).host); });
  page.on('response', (r) => { if (/\.woff2(\?|$)/.test(r.url())) fuentes.push({ url: r.url(), estado: r.status(), tipo: r.headers()['content-type'] }); });
  await page.goto(WEB + '/', { waitUntil: 'load' });
  await page.waitForSelector('.lobby .game-title', { timeout: 90_000 });
  await page.evaluate(() => document.fonts.ready);
  await page.waitForTimeout(800);

  ok(aGoogle.length === 0, `la página no pide nada a Google Fonts (${aGoogle.length} peticiones${aGoogle.length ? ': ' + [...new Set(aGoogle)].join(', ') : ''})`);
  const propia = fuentes.find((f) => new URL(f.url).host === new URL(WEB).host);
  ok(propia?.estado === 200 && /font\/woff2/.test(propia?.tipo ?? ''), `la fuente llega de la propia web: ${propia ? `${propia.estado} ${propia.tipo} ${new URL(propia.url).pathname}` : 'no se ha pedido'}`);
  const caras = await page.evaluate(() => [...document.fonts].filter((f) => /Nunito Sans/.test(f.family)).map((f) => `${f.weight} ${f.status}`));
  ok(caras.includes('900 loaded'), `y el navegador la ha cargado para el logo (${caras.join(', ') || 'ninguna'})`);
  const logo = await page.locator('.lobby .game-title').first().evaluate((e) => ({ ancho: Math.round(e.getBoundingClientRect().width), familia: getComputedStyle(e).fontFamily.split(',')[0] }));
  ok(logo.ancho > 150 && logo.ancho < 280, `el logo mide lo de siempre (${logo.ancho} px de ancho, ${logo.familia})`);
  await page.locator('.lobby-header').screenshot({ path: join(SHOTS, `${PRE}logo.png`) });
  const privacidad = await (await ctx.request.get(WEB + '/privacidad.html')).text();
  ok(!/fonts\.(googleapis|gstatic)\.com/.test(privacidad), 'la página de privacidad tampoco carga fuentes de Google');
} catch (e) {
  fallar(e);
} finally {
  await browser.close();
}
terminar();
