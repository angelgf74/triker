// Lo común a las pruebas de e2e/: dónde está la app, el navegador de móvil con el
// que se prueba y las afirmaciones.
//
// Por defecto van contra los servidores de desarrollo (ver ../SKILL.md). Las que
// lo admiten se pueden lanzar contra producción con las mismas variables que
// driver.mjs:
//   TRIKER_WEB=https://triker-app.angelgf.com.es TRIKER_API=https://triker-api.angelgf.com.es
import { mkdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';

export { chromium };

export const WEB = (process.env.TRIKER_WEB ?? 'https://localhost:7164/triker').replace(/\/$/, '');
export const API = (process.env.TRIKER_API ?? 'https://localhost:7077').replace(/\/$/, '');
export const EN_LOCAL = /\/\/localhost[:/]/.test(WEB + '/') && /\/\/localhost[:/]/.test(API + '/');

// El certificado de desarrollo no es de confianza: sin esto fallan fetch y el WebSocket de Node.
if (EN_LOCAL) process.env.NODE_TLS_REJECT_UNAUTHORIZED = '0';

// Las capturas, donde las de driver.mjs (carpeta fuera de git).
export const SHOTS = join(dirname(fileURLToPath(import.meta.url)), '..', '..', 'shots');
mkdirSync(SHOTS, { recursive: true });

// El móvil con el que se prueba todo: 360 px de ancho y en español de España (la
// coma decimal ya rompió una vez la barra de cuenta atrás).
export const MOVIL = {
  ignoreHTTPSErrors: true, locale: 'es-ES',
  viewport: { width: 360, height: 740 }, deviceScaleFactor: 2, isMobile: true, hasTouch: true,
};

// La petición con la que la web crea una partida, para las pruebas que la retocan por el camino.
export const RUTA_CREAR = new RegExp(`${new URL(API).host.replace(/\./g, '\\.')}/api/games$`);

export const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
// Para esperas de Playwright que pueden no cumplirse: true o false en vez de una excepción.
export const espera = (p) => p.then(() => true, () => false);
export const limpio = (t) => (t ?? '').trim().replace(/\s+/g, ' ');

let fallos = 0;
export function ok(cond, msg) {
  console.log(`  ${cond ? 'OK   ' : 'FALLO'} ${msg}`);
  if (!cond) fallos++;
}
// En el catch de la prueba: una excepción cuenta como fallo y no corta el resumen.
export function fallar(e) {
  console.log(`FALLO: ${String(e?.message ?? e).slice(0, 300)}`);
  fallos++;
}
// Última línea de cada prueba: el resumen que lee todas.mjs y el código de salida.
export function terminar() {
  console.log(fallos === 0 ? '== TODO OK ==' : `== ${fallos} FALLO(S) ==`);
  // Sin process.exit inmediato: en Windows, salir con una conexión de fetch aún
  // cerrándose aborta Node con un "Assertion failed" y un código de salida que no es
  // el de la prueba. Se deja terminar solo; el temporizador es por si algo se queda colgado.
  process.exitCode = fallos === 0 ? 0 : 1;
  setTimeout(() => process.exit(process.exitCode), 5000).unref();
}

// Los controles pintados que no llegan a 44 px, lo que ocupa un dedo: botones, enlaces,
// campos y casillas. Un campo de texto o un desplegable solo se mide de alto (el ancho lo
// da el formulario); una casilla se toca por su etiqueta, así que se mide la etiqueta.
// Fuera queda lo que no se toca: lo oculto a los lectores de pantalla (el abanico del
// lobby) y las etiquetas que solo nombran un campo.
export const controlesCortos = (page) => page.evaluate(() => {
  const vistos = new Map();
  for (const e of document.querySelectorAll('button, a[href], input, select, textarea, label, summary, [role="button"]')) {
    if (e.closest('[aria-hidden="true"]') || e.classList.contains('sr-only')) continue;
    if (e.tagName === 'LABEL' && !e.querySelector('input')) continue;
    if (e.tagName === 'INPUT' && e.closest('label')) continue;
    const r = e.getBoundingClientRect();
    if (r.width === 0 || r.height === 0) continue;
    const campo = /^(INPUT|SELECT|TEXTAREA)$/.test(e.tagName);
    const ancho = Math.round(r.width * 10) / 10, alto = Math.round(r.height * 10) / 10;
    if (alto >= 44 && (campo || ancho >= 44)) continue;
    const clase = (e.className || '').toString().trim().split(/\s+/).filter(Boolean).join('.');
    const nombre = (e.textContent || e.placeholder || e.getAttribute('aria-label') || '').trim().replace(/\s+/g, ' ').slice(0, 24);
    const clave = `${e.tagName.toLowerCase()}${clase ? '.' + clase : ''}`;
    if (!vistos.has(clave)) vistos.set(clave, `${clave} «${nombre}» ${ancho}×${alto} px`);
  }
  return [...vistos.values()].sort();
});

// Para las pruebas que no deben correr contra producción: llenan el lobby de mesas
// de prueba, tardan minutos con mesas abiertas o cortan la red del navegador.
export function soloLocal() {
  if (EN_LOCAL) return;
  console.log(`Esta prueba es solo para los servidores de desarrollo (TRIKER_WEB=${WEB}).`);
  process.exit(2);
}
