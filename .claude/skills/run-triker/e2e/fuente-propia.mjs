// La tipografía es Nunito Sans en toda la app (antes solo en el logo, y el resto en la
// fuente de cada sistema: Segoe en Windows, Roboto en Android, San Francisco en iPhone).
// Se sirve desde la propia web, en un solo fichero variable: ninguna petición a Google.
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
  page.on('response', async (r) => {
    if (!/\.woff2(\?|$)/.test(r.url())) return;
    const peso = (await r.body().catch(() => Buffer.alloc(0))).length;
    fuentes.push({ url: r.url(), estado: r.status(), tipo: r.headers()['content-type'], peso });
  });
  await page.goto(WEB + '/', { waitUntil: 'load' });
  await page.waitForSelector('.lobby .game-title', { timeout: 90_000 });
  await page.evaluate(() => document.fonts.ready);
  await page.waitForTimeout(800);

  ok(aGoogle.length === 0, `la página no pide nada a Google Fonts (${aGoogle.length} peticiones${aGoogle.length ? ': ' + [...new Set(aGoogle)].join(', ') : ''})`);
  const propia = fuentes.find((f) => new URL(f.url).host === new URL(WEB).host);
  ok(propia?.estado === 200 && /font\/woff2/.test(propia?.tipo ?? ''), `la fuente llega de la propia web: ${propia ? `${propia.estado} ${propia.tipo} ${new URL(propia.url).pathname}` : 'no se ha pedido'}`);
  const caras = await page.evaluate(() => [...document.fonts].filter((f) => /Nunito Sans/.test(f.family)).map((f) => `${f.weight} ${f.status}`));
  ok(caras.length === 1 && caras[0] === '400 900 loaded', `en una sola cara que cubre todos los pesos, del normal al del logo (${caras.join(', ') || 'ninguna'})`);
  ok(fuentes.length === 1 && propia?.peso > 10_000 && propia?.peso < 40_000, `y un solo fichero de menos de 40 KB (${fuentes.length} ficheros, ${Math.round((propia?.peso ?? 0) / 1024)} KB)`);
  const familias = await page.evaluate(() => {
    const de = (sel) => getComputedStyle(document.querySelector(sel)).fontFamily.split(',')[0].replace(/["']/g, '').trim();
    return { pagina: de('body'), boton: de('.btn-play-now'), campo: de('#nombre-jugador'), subtitulo: de('.game-subtitle') };
  });
  ok(Object.values(familias).every((v) => v === 'Nunito Sans'), `toda la página usa Nunito Sans: ${JSON.stringify(familias)}`);
  // Que la use de verdad y no la de reserva: el mismo texto mide distinto con una y con otra.
  const anchos = await page.evaluate(() => {
    const medir = (familia) => {
      const e = document.createElement('span');
      e.style.cssText = `position:absolute;visibility:hidden;font:600 20px ${familia}`;
      e.textContent = 'Jugar contra bots';
      document.body.append(e);
      const w = e.getBoundingClientRect().width;
      e.remove();
      return w;
    };
    return { nunito: medir('"Nunito Sans", monospace'), reserva: medir('monospace') };
  });
  ok(Math.abs(anchos.nunito - anchos.reserva) > 5, `y el texto se pinta con ella, no con la de reserva (${anchos.nunito.toFixed(0)} px frente a ${anchos.reserva.toFixed(0)})`);
  const precarga = await page.evaluate(() => { const l = document.querySelector('link[rel="preload"][as="font"]'); return l ? { tipo: l.type, cors: l.crossOrigin, href: l.getAttribute('href') } : null; });
  ok(precarga?.tipo === 'font/woff2' && precarga?.cors !== null && fuentes.some((f) => f.url.endsWith(precarga.href.split('/').pop())),
     `la página la precarga, para que el texto no cambie de letra al arrancar (${precarga ? precarga.href : 'sin precarga'})`);
  const logo = await page.locator('.lobby .game-title').first().evaluate((e) => ({ ancho: Math.round(e.getBoundingClientRect().width), familia: getComputedStyle(e).fontFamily.split(',')[0] }));
  ok(logo.ancho > 150 && logo.ancho < 280, `el logo mide lo de siempre (${logo.ancho} px de ancho, ${logo.familia})`);
  await page.locator('.lobby-header').screenshot({ path: join(SHOTS, `${PRE}logo.png`) });
  const privacidad = await (await ctx.request.get(WEB + '/privacidad.html')).text();
  ok(!/fonts\.(googleapis|gstatic)\.com/.test(privacidad), 'la página de privacidad tampoco carga fuentes de Google');

  // La página de privacidad es HTML fijo, con sus estilos y su política de seguridad
  // propios: tiene que verse como la app (misma letra, mismos colores) y su política
  // tiene que dejarle cargar la fuente.
  const app = await page.evaluate(() => {
    const s = getComputedStyle(document.documentElement);
    const rgb = (v) => { const h = s.getPropertyValue(v).trim(); return `rgb(${[1, 3, 5].map((i) => parseInt(h.slice(i, i + 2), 16)).join(', ')})`; };
    return { fondo: rgb('--fondo'), panel: rgb('--panel'), texto: rgb('--texto'), acento: rgb('--acento') };
  });
  const cortes = [];
  page.on('console', (m) => { if (/Content Security Policy|Refused to/i.test(m.text())) cortes.push(m.text().slice(0, 140)); });
  await page.goto(WEB + '/privacidad.html', { waitUntil: 'load' });
  await page.evaluate(() => document.fonts.ready);
  await page.waitForTimeout(500);
  const pagina = await page.evaluate(() => {
    const de = (sel, p) => getComputedStyle(document.querySelector(sel))[p];
    return {
      familia: de('body', 'fontFamily').split(',')[0].replace(/["']/g, '').trim(),
      cargada: [...document.fonts].some((f) => /Nunito Sans/.test(f.family) && f.status === 'loaded'),
      fondo: de('body', 'backgroundColor'), texto: de('body', 'color'), panel: de('.card', 'backgroundColor'),
      titulo: de('h1', 'color'), apartado: de('h2', 'color'), enlace: de('a', 'color'),
    };
  });
  ok(pagina.familia === 'Nunito Sans' && pagina.cargada, `la página de privacidad usa la misma letra, y carga (${pagina.familia}, ${pagina.cargada ? 'cargada' : 'sin cargar'})`);
  ok(cortes.length === 0, `sin que su política de seguridad corte nada${cortes.length ? ': ' + cortes.join(' | ') : ''}`);
  ok(pagina.fondo === app.fondo && pagina.texto === app.texto && pagina.panel === app.panel, `con los colores de la app (fondo ${pagina.fondo}, texto ${pagina.texto}, panel ${pagina.panel})`);
  ok(pagina.enlace === app.acento && pagina.titulo === app.texto && pagina.apartado === app.texto, `y el dorado solo en los enlaces, no en los títulos (título ${pagina.titulo}, enlace ${pagina.enlace})`);
  await page.screenshot({ path: join(SHOTS, `${PRE}privacidad.png`) });
} catch (e) {
  fallar(e);
} finally {
  await browser.close();
}
terminar();
