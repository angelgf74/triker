// Paso propio de srv (pasos en srv.json): pone la Content-Security-Policy en el
// index.html del paquete.
//
// Para qué: los asientos (id + token) viven en localStorage. Si algún día se
// colara un script ajeno en la página, con esta política no se ejecuta y, aunque
// lo hiciera, no podría enviar nada fuera de los sitios de la lista.
//
// Por qué aquí y no escrita en index.html:
//   - El <script type="importmap"> lo rellena Blazor al publicar y cambia en cada
//     compilación. Es un script en línea, y la política solo deja pasar los que
//     llevan su huella: hay que calcularla sobre el paquete ya montado.
//   - En desarrollo no hace falta: la API es otra (localhost) y el servidor de
//     desarrollo no es el de producción.
//
// Va en un <meta> porque el sitio de nginx es una plantilla de srv común a todas
// las apps. Lo que un <meta> no puede llevar (frame-ancestors) ya lo pone esa
// plantilla como cabecera.
//
// Falla, y con ello no se publica, si el paquete no es como espera: mejor eso que
// publicar una web que no arranca o una política que no protege.
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';

const paquete = process.env.SRV_PAQUETE;
if (!paquete) {
    console.error('falta SRV_PAQUETE: este paso lo lanza srv subir');
    process.exit(1);
}
const fallar = (motivo) => {
    console.error(`csp: ${motivo}`);
    process.exit(1);
};

const web = path.join(paquete, 'web');
const index = path.join(web, 'index.html');
// El navegador pasa los saltos de línea a \n antes de nada: la huella se calcula
// sobre el texto como él lo ve, y el fichero se escribe así.
const html = fs.readFileSync(index, 'utf8').replace(/\r\n?/g, '\n');
const sinComentarios = html.replace(/<!--[\s\S]*?-->/g, '');

if (/http-equiv\s*=\s*["']?Content-Security-Policy/i.test(sinComentarios))
    fallar(`${index} ya lleva una política: este paso se ha ejecutado dos veces`);

// --- Scripts: ninguno en línea salvo el importmap de Blazor -----------------------------
const scripts = [...sinComentarios.matchAll(/<script\b([^>]*)>([\s\S]*?)<\/script>/gi)].map((m) => ({
    atributos: m[1],
    texto: m[2],
    src: /\bsrc\s*=\s*["']([^"']+)["']/i.exec(m[1])?.[1] ?? null,
    importmap: /\btype\s*=\s*["']importmap["']/i.test(m[1]),
}));
const enLinea = scripts.filter((s) => !s.src && !s.importmap);
if (enLinea.length > 0)
    fallar(`index.html tiene ${enLinea.length} script(s) en línea, que la política no dejaría ejecutar. Pásalos a un fichero de js/. Empieza por: ${enLinea[0].texto.trim().slice(0, 80)}`);
const importmaps = scripts.filter((s) => s.importmap);
if (importmaps.length !== 1 || importmaps[0].texto.trim() === '')
    fallar(`se esperaba un <script type="importmap"> relleno por Blazor y hay ${importmaps.length} (o está vacío)`);
const huella = `'sha256-${crypto.createHash('sha256').update(importmaps[0].texto, 'utf8').digest('base64')}'`;

// Manejadores en línea (onclick="...") tampoco se ejecutarían.
const manejador = /<[a-z][^>]*\son[a-z]+\s*=/i.exec(sinComentarios);
if (manejador) fallar(`index.html tiene un manejador de eventos en línea, que la política no dejaría ejecutar: ${manejador[0].slice(0, 80)}`);

// --- De dónde puede venir cada cosa ---------------------------------------------------
// Scripts de otros sitios: los que index.html carga a la vista (hoy, la analítica propia).
const deOtroSitio = [...new Set(scripts.filter((s) => /^https?:\/\//i.test(s.src ?? '')).map((s) => new URL(s.src).origin))];
for (const origen of deOtroSitio)
    if (!origen.startsWith('https://')) fallar(`index.html carga un script sin https: ${origen}`);

// La API del juego: la que usa la web publicada. Peticiones (https) y SignalR (wss).
const ajustes = path.join(web, 'appsettings.Production.json');
let api;
try { api = new URL(JSON.parse(fs.readFileSync(ajustes, 'utf8').replace(/^﻿/, '')).ApiBaseUrl); }
catch (e) { fallar(`no se puede leer ApiBaseUrl de ${ajustes}: ${e.message}`); }
if (api.protocol !== 'https:') fallar(`ApiBaseUrl de producción no es https: ${api.href}`);

// El buzón de sugerencias (UrlDelBuzon en Triker.Web/Components/SugerenciasModal.razor).
// Va compilado en la app: si cambia allí, hay que cambiarlo aquí o el buzón dejará de enviar.
const BUZON = 'https://gestorsugerencias-api.angelgf.com.es';

const politica = [
    "default-src 'self'",
    // 'wasm-unsafe-eval': lo que necesita .NET para ejecutar WebAssembly. No abre eval().
    `script-src 'self' 'wasm-unsafe-eval' ${huella} ${deOtroSitio.join(' ')}`.trim(),
    // Los componentes ponen estilos con el atributo style (anchos de barras, color de grupo).
    "style-src 'self' 'unsafe-inline'",
    // data: y blob:, por la imagen de la jugada que se genera para compartir.
    "img-src 'self' data: blob:",
    "font-src 'self'",
    `connect-src 'self' ${api.origin} wss://${api.host} ${[...deOtroSitio, BUZON].join(' ')}`,
    "worker-src 'self'",
    "manifest-src 'self'",
    "base-uri 'self'",
    "form-action 'self'",
    "object-src 'none'",
].join('; ');

// La primera cosa de <head>, justo tras el charset: la política vale para lo que viene después.
const charset = /<meta\s+charset\s*=\s*["']?utf-8["']?\s*\/?>/i;
if (html.split(charset).length !== 2) fallar(`${index}: se esperaba un <meta charset="utf-8"> y solo uno`);
const conPolitica = html.replace(charset, (m) => `${m}\n    <meta http-equiv="Content-Security-Policy" content="${politica}" />`);
fs.writeFileSync(index, conPolitica);

// Las copias comprimidas no llevarían la política: fuera, y srv rehace las que hagan falta.
for (const copia of [`${index}.br`, `${index}.gz`]) fs.rmSync(copia, { force: true });

console.log(`csp: política puesta en ${index}`);
console.log(`     scripts de: la propia web${deOtroSitio.map((o) => ', ' + o).join('')}; importmap ${huella}`);
console.log(`     conexiones a: la propia web, ${api.origin}, wss://${api.host}${[...deOtroSitio, BUZON].map((o) => ', ' + o).join('')}`);
