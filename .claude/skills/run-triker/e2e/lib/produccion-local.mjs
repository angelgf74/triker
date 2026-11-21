// Producción simulada: prueba el paquete publicado tal cual (con su política de
// seguridad y sus direcciones reales) sin tocar producción. El navegador cree estar
// en la web de verdad:
//   - la web sale de la carpeta del paquete (la que deja montar-paquete.mjs), con
//     vuelta a index.html para las rutas de la app;
//   - la API de producción se desvía a la API local, también el WebSocket;
//   - todo lo demás se corta y se apunta: nada sale a Internet.
// Hace falta la API local en marcha (https://localhost:7077).
//
// Por qué existe: la política (deploy/csp.mjs) solo está en lo publicado, no en el
// servidor de desarrollo, y lleva escritas las direcciones de producción.
import fs from 'node:fs';
import path from 'node:path';

process.env.NODE_TLS_REJECT_UNAUTHORIZED = '0';
const API_LOCAL = 'https://localhost:7077';
const TIPOS = {
  '.html': 'text/html; charset=utf-8', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css',
  '.json': 'application/json', '.wasm': 'application/wasm', '.svg': 'image/svg+xml', '.png': 'image/png',
  '.woff2': 'font/woff2', '.txt': 'text/plain; charset=utf-8', '.dat': 'application/octet-stream',
};

// Las direcciones de producción, leídas de lo que se va a publicar: srv.json (el dominio
// de la web) y el appsettings de producción del paquete (la API).
export function direcciones(carpetaWeb, raizRepo) {
  const srv = JSON.parse(fs.readFileSync(path.join(raizRepo, 'srv.json'), 'utf8'));
  const ajustes = JSON.parse(fs.readFileSync(path.join(carpetaWeb, 'appsettings.Production.json'), 'utf8').replace(/^\uFEFF/, ''));
  return { web: `https://${srv.web.dominio}`, api: new URL(ajustes.ApiBaseUrl).origin };
}

// otros: { '<origen>': (peticion) => respuesta } para los sitios previstos que la prueba
// contesta ella misma (analítica, buzón). -> { cortadas }: lo que la página intentó pedir
// a cualquier otro sitio.
export async function simularProduccion(ctx, carpetaWeb, { web, api, otros = {} }) {
  const cortadas = [];
  const cors = { 'access-control-allow-origin': web, 'access-control-allow-headers': '*', 'access-control-allow-methods': '*', 'access-control-allow-credentials': 'true' };

  await ctx.route('**/*', async (route) => {
    const req = route.request();
    const url = new URL(req.url());

    if (url.origin === web) {
      let fichero = path.join(carpetaWeb, decodeURIComponent(url.pathname));
      if (url.pathname.endsWith('/') || !fs.existsSync(fichero) || fs.statSync(fichero).isDirectory()) fichero = path.join(carpetaWeb, 'index.html');
      return route.fulfill({ status: 200, contentType: TIPOS[path.extname(fichero)] ?? 'application/octet-stream', body: fs.readFileSync(fichero) });
    }

    if (url.origin === api) {
      if (req.method() === 'OPTIONS') return route.fulfill({ status: 204, headers: cors });
      // La API local solo acepta su origen de desarrollo; la respuesta vuelve con el de producción.
      const res = await route.fetch({ url: API_LOCAL + url.pathname + url.search, headers: { ...req.headers(), origin: 'https://localhost:7164' } });
      return route.fulfill({ response: res, headers: { ...res.headers(), ...cors } });
    }

    for (const [origen, responder] of Object.entries(otros)) {
      if (url.origin !== origen) continue;
      if (req.method() === 'OPTIONS') return route.fulfill({ status: 204, headers: cors });
      const respuesta = responder(req);
      return route.fulfill({ ...respuesta, headers: { ...(respuesta.headers ?? {}), ...cors } });
    }

    cortadas.push(`${req.method()} ${url.origin}${url.pathname}`);
    return route.abort('blockedbyclient');
  });

  // El WebSocket del juego: de la API de producción a la local, mensaje a mensaje.
  const hostApi = new URL(api).host.replace(/\./g, '\\.');
  await ctx.routeWebSocket(new RegExp(`^wss://${hostApi}/`), (ws) => {
    const u = new URL(ws.url());
    const real = new WebSocket('wss://localhost:7077' + u.pathname + u.search);
    const cola = [];
    real.binaryType = 'arraybuffer';
    real.onopen = () => { for (const m of cola) real.send(m); cola.length = 0; };
    real.onmessage = (e) => ws.send(typeof e.data === 'string' ? e.data : Buffer.from(e.data));
    real.onclose = () => ws.close().catch(() => {});
    real.onerror = () => ws.close().catch(() => {});
    ws.onMessage((m) => (real.readyState === 1 ? real.send(m) : cola.push(m)));
    ws.onClose(() => { try { real.close(); } catch { /* ya cerrado */ } });
  });

  return { cortadas };
}

// Para addInitScript: apunta en window.__csp cada violación de la política.
//
// Ojo al comprobar qué corta la política: lo que se ejecuta directamente desde
// page.evaluate está exento (el navegador lo trata como la consola de depuración).
// eval() hay que pedirlo desde código de la página, por ejemplo dentro de un
// setTimeout, o parecerá que la política no lo corta.
export function vigilarCsp() {
  window.__csp = [];
  document.addEventListener('securitypolicyviolation', (e) => {
    window.__csp.push(`${e.effectiveDirective} bloquea ${e.blockedURI || 'en línea'}${e.sourceFile ? ' (en ' + e.sourceFile.split('/').pop() + ':' + e.lineNumber + ')' : ''}`);
  });
}
