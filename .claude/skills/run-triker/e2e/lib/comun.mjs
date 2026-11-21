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

// Para las pruebas que no deben correr contra producción: llenan el lobby de mesas
// de prueba, tardan minutos con mesas abiertas o cortan la red del navegador.
export function soloLocal() {
  if (EN_LOCAL) return;
  console.log(`Esta prueba es solo para los servidores de desarrollo (TRIKER_WEB=${WEB}).`);
  process.exit(2);
}
