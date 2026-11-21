// Lanza las pruebas de e2e/ una detrás de otra contra los servidores de desarrollo
// y resume. Arranca antes la API y la web como dice ../SKILL.md.
//
//   node todas.mjs                todas (unos 25 min: varias dejan vencer plazos de verdad)
//   node todas.mjs --rapidas      solo las que tardan menos de un minuto (unos 6 min)
//   node todas.mjs empates humo   las que se nombren
//
// Entre prueba y prueba espera lo que haga falta: la API solo deja crear 10 partidas
// por minuto e IP (RateLimits), y pasarlas seguidas da fallos que no son de la app.
// Por eso cada una declara aquí cuántas crea.
//
// Fuera de la lista, a mano:
//   tope-por-ip.mjs        crea 40 partidas (6 min) y deja la API llena: reiníciala después.
//   que-carga-la-web.mjs   no es una prueba; es el inventario para la política de seguridad.
import { spawn } from 'node:child_process';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { API, WEB, EN_LOCAL, sleep } from './lib/comun.mjs';

const AQUI = dirname(fileURLToPath(import.meta.url));
// [nombre, partidas que crea, minutos que tarda (0 = menos de uno)]
const PRUEBAS = [
  ['humo', 1, 0],
  ['sin-peticiones-fallidas', 1, 0],
  ['vista-previa-de-enlaces', 0, 0],
  ['fuente-propia', 0, 0],
  ['privacidad-de-anuncios', 0, 0],
  ['jugar-contra-bots', 2, 0],
  ['diseno-movil', 1, 0],
  ['un-solo-acento', 1, 0],
  ['iconos', 1, 0],
  ['resultados-y-cabecera', 2, 0],
  ['nombres-invisibles', 5, 0],
  ['ritmo-de-los-bots', 5, 0],
  ['empates', 3, 0],
  ['anuncio-entre-rondas', 4, 0],
  ['salir-de-la-mesa', 5, 0],
  ['volver-a-tu-partida', 4, 0],
  ['sonido-descarte-y-ayuda', 7, 1],
  ['politica-de-seguridad', 1, 2],
  ['cuenta-atras-y-turno-agotado', 1, 3],
  ['plazo-entre-rondas', 3, 3],
  ['reconexion', 4, 3],
  ['salas-sin-nadie', 2, 4],
];
const POR_MINUTO = 9;   // una menos que el límite, por si la prueba reintenta

const args = process.argv.slice(2);
const pedidas = args.filter((a) => !a.startsWith('--')).map((a) => a.replace(/\.mjs$/, ''));
const desconocidas = pedidas.filter((p) => !PRUEBAS.some(([n]) => n === p));
if (desconocidas.length) { console.log(`No conozco: ${desconocidas.join(', ')}. Están: ${PRUEBAS.map(([n]) => n).join(', ')}`); process.exit(2); }
const lista = PRUEBAS.filter(([n, , min]) => (pedidas.length ? pedidas.includes(n) : !args.includes('--rapidas') || min === 0));

if (!EN_LOCAL) { console.log('todas.mjs es para los servidores de desarrollo. Contra producción, las pruebas de una en una (ver ../SKILL.md).'); process.exit(2); }
for (const [que, url] of [['la API', `${API}/api/games`], ['la web', `${WEB}/`]]) {
  const va = await fetch(url).then((r) => r.ok, () => false);
  if (!va) { console.log(`No responde ${que} (${url}). Arranca los dos servidores como dice ../SKILL.md.`); process.exit(2); }
}

const lanzar = (nombre) => new Promise((fin) => {
  const hijo = spawn(process.execPath, [join(AQUI, `${nombre}.mjs`)], { cwd: AQUI, env: process.env });
  let salida = '';
  hijo.stdout.on('data', (d) => { salida += d; });
  hijo.stderr.on('data', (d) => { salida += d; });
  hijo.on('close', (codigo) => fin({ codigo, salida }));
});

const creadas = [];   // [momento, cuántas]
const resumen = [];
const t0 = Date.now();
for (const [nombre, crea] of lista) {
  // Hueco en el límite de creaciones: se espera a que caduquen del último minuto las
  // que hagan falta, empezando por las más antiguas.
  const ahora = Date.now();
  const recientes = creadas.filter(([t]) => ahora - t < 62_000);
  let sobran = recientes.reduce((a, [, n]) => a + n, 0) + crea - POR_MINUTO;
  let hasta = 0;
  for (const [t, n] of recientes) {
    if (sobran <= 0) break;
    sobran -= n;
    hasta = t + 62_000;
  }
  if (hasta > ahora) {
    process.stdout.write(`   (espero ${Math.ceil((hasta - ahora) / 1000)} s: límite de partidas por minuto)\n`);
    await sleep(hasta - ahora);
  }
  creadas.push([Date.now(), crea]);
  process.stdout.write(`${nombre} ... `);
  const t = Date.now();
  const { codigo, salida } = await lanzar(nombre);
  const bien = codigo === 0 && /== TODO OK ==/.test(salida);
  const cuentas = `${(salida.match(/^\s+OK /gm) ?? []).length} ok, ${(salida.match(/^\s*FALLO/gm) ?? []).length} fallos`;
  console.log(`${bien ? 'OK   ' : 'FALLO'} (${cuentas}, ${Math.round((Date.now() - t) / 1000)} s)`);
  if (!bien) console.log(salida.split('\n').filter((l) => /FALLO|pageerror|Error/.test(l)).map((l) => '      ' + l.trim()).slice(0, 12).join('\n'));
  resumen.push([nombre, bien]);
}
const malas = resumen.filter(([, b]) => !b).map(([n]) => n);
console.log(`\n${resumen.length - malas.length} de ${resumen.length} pruebas en verde en ${Math.round((Date.now() - t0) / 60_000)} min${malas.length ? '. Fallan: ' + malas.join(', ') : ''}`);
process.exit(malas.length ? 1 : 0);
