// App Links de punta a punta: una sala creada en producción desde el navegador y su
// enlace de invitación abierto en la app del emulador, que tiene que entrar directa.
//
// Antes: emulador arrancado, la app abierta en el lobby y la depuración del WebView en
// el puerto 9333 (`source emulador.sh; conecta`). Va contra producción, que es la web
// que carga la app; crea una sala de prueba y la cierra.
//
//   node invitacion.mjs [paquete]        por defecto com.angelgf.triker.debug
//
// Con la app de depuración el enlace se dirige a ella a propósito (`am start ... <paquete>`):
// Android no la da por verificada, porque su firma no está en assetlinks.json. Que un
// enlace cualquiera abra la app sola se comprueba con una compilación firmada:
//   adb shell pm get-app-links com.angelgf.triker      -> «verified»
//   adb shell am start -a android.intent.action.VIEW -c android.intent.category.BROWSABLE -d <enlace>
import { execFileSync } from 'node:child_process';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';

const AQUI = dirname(fileURLToPath(import.meta.url));
const PAQUETE = process.argv[2] ?? 'com.angelgf.triker.debug';
const DEV = process.env.DEV ?? 'emulator-5554';
const ADB = process.env.ADB ?? join(process.env.LOCALAPPDATA, 'Android/Sdk/platform-tools/adb.exe');
const WEB = 'https://triker-app.angelgf.com.es';
const API = 'https://triker-api.angelgf.com.es';
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
let fallos = 0;
const ok = (cond, msg) => { console.log(`  ${cond ? 'OK   ' : 'FALLO'} ${msg}`); if (!cond) fallos++; };
const adb = (...a) => execFileSync(ADB, ['-s', DEV, ...a]).toString();
const app = (js) => JSON.parse(execFileSync(process.execPath, [join(AQUI, 'cdp.mjs'), '9333', 'eval', js]).toString());

const browser = await chromium.launch({ channel: 'chrome', headless: true });
let id = null;
try {
  const ctx = await browser.newContext({ locale: 'es-ES', viewport: { width: 360, height: 740 }, isMobile: true, hasTouch: true });
  const ana = await ctx.newPage();
  await ana.goto(WEB + '/', { waitUntil: 'load' });
  await ana.waitForSelector('.lobby .game-title', { timeout: 90_000 });
  await ana.fill('#nombre-jugador', 'Prueba A');
  await ana.click('.lobby-actions .btn-new-game');
  await ana.fill('.create-form .name-input', `Prueba ${Date.now() % 100000}`);
  await ana.click('.create-form .btn-primary');
  await ana.waitForSelector('.waiting-room');
  id = ana.url().match(/game\/([0-9a-f-]{36})/)[1];
  const enlace = `${WEB}/?join=${id}`;

  // El invitado ya tiene nombre puesto en la app: con él, la invitación entra directa.
  app(`(()=>{const i=document.querySelector('#nombre-jugador');i.value='Invitado';i.dispatchEvent(new Event('input',{bubbles:true}));i.dispatchEvent(new Event('change',{bubbles:true}));return 'ok'})()`);
  await sleep(1500);

  const salida = adb('shell', `am start -W -a android.intent.action.VIEW -d '${enlace}' ${PAQUETE}`);
  ok(/Status: ok/.test(salida), `el enlace de invitación abre ${PAQUETE}`);
  let dentro = '';
  for (let i = 0; i < 15 && !/waiting-room/.test(dentro); i++) {
    await sleep(1500);
    try { dentro = app(`location.pathname+' '+(document.querySelector('.waiting-room')?'waiting-room':'')+' jugadores='+document.querySelectorAll('.waiting-player').length`); } catch { /* la app está recargando */ }
  }
  ok(dentro.includes(id) && /waiting-room jugadores=2/.test(dentro), `la app entra directa en la sala: ${dentro}`);
  ok(await ana.waitForFunction(() => document.querySelectorAll('.waiting-player').length === 2, null, { timeout: 8000 }).then(() => true, () => false),
     'y quien creó la sala ve llegar al invitado');

  app(`(()=>{document.querySelector('.btn-leave').click();return 'salir'})()`);
  await sleep(1500);
  await ana.click('.btn-leave');
  await ana.waitForSelector('.lobby .game-title', { timeout: 10_000 });
} catch (e) {
  console.log(`FALLO: ${String(e.message).slice(0, 300)}`);
  fallos++;
} finally {
  await browser.close();
}
const abiertas = await (await fetch(`${API}/api/games`)).json();
ok(id && !abiertas.some((g) => g.id === id), 'la sala de prueba no queda abierta');
console.log(fallos === 0 ? '== TODO OK ==' : `== ${fallos} FALLO(S) ==`);
process.exit(fallos === 0 ? 0 : 1);
