// Fallos menores de la web: sonido de campeón, mensaje del descarte rechazado y
// texto de la ayuda. Solo contra localhost.
import { join } from 'node:path';
import { chromium, WEB, MOVIL, RUTA_CREAR, ok, espera, sleep, limpio, fallar, terminar, soloLocal } from './lib/comun.mjs';

soloLocal();

// Apunta cada sonido que pide la app (window.trikerAudio lo define js/audio.js).
function espiarSonidos() {
  window.__sonidos = [];
  let real;
  Object.defineProperty(window, 'trikerAudio', {
    configurable: true,
    get: () => real,
    set: (v) => {
      for (const n of ['champion', 'winRound', 'loseRound'])
        if (typeof v[n] === 'function') { const f = v[n]; v[n] = (...a) => { window.__sonidos.push(n); return f.apply(v, a); }; }
      real = v;
    },
  });
}

const browser = await chromium.launch({ channel: 'chrome', headless: true });
async function pagina({ meta, bots } = {}) {
  const ctx = await browser.newContext({ ...MOVIL });
  await ctx.addInitScript(espiarSonidos);
  const page = await ctx.newPage();
  page.on('pageerror', (e) => { if (!/ServiceWorker/.test(e.message)) console.log(`  [pageerror] ${e.message.slice(0, 160)}`); });
  if (meta) {
    // La web solo ofrece metas de 3 o más: para acabar la partida en una ronda se
    // retoca la petición de «Jugar contra bots» por el camino.
    await page.route(RUTA_CREAR, async (route) => {
      if (route.request().method() !== 'POST') return route.continue();
      const cuerpo = { ...route.request().postDataJSON(), targetWins: meta, bots };
      await route.continue({ postData: JSON.stringify(cuerpo) });
    });
  }
  await page.goto(WEB + '/', { waitUntil: 'load' });
  await page.waitForSelector('.lobby .game-title', { timeout: 90_000 });
  await page.fill('#nombre-jugador', 'Ana');
  return page;
}

try {
  console.log('== A. Sonido al ganar la partida (meta de 1 victoria, contra 1 bot) ==');
  let visto = false;
  for (let intento = 1; intento <= 10 && !visto; intento++) {
    const page = await pagina({ meta: 1, bots: 1 });
    await page.click('.btn-play-now');
    await page.waitForSelector('.turn-indicator.my-turn', { timeout: 20_000 });
    await page.click('.btn-pass');
    await page.waitForSelector('.results-view .result-entry', { timeout: 40_000 });
    await page.waitForSelector('.champion-banner', { timeout: 5000 });
    await sleep(1200);
    const gane = /Ganaste la ronda/.test(limpio(await page.locator('.round-summary').textContent()));
    const sonidos = await page.evaluate(() => window.__sonidos);
    if (!gane) {
      ok(sonidos.join() === 'loseRound', `   (intento ${intento}: gana el bot; suena la derrota: ${sonidos.join()})`);
    } else {
      visto = true;
      ok(sonidos.join() === 'champion', `ganas la partida y suena el de campeón, una sola vez (ha sonado: ${sonidos.join() || 'nada'})`);
      await page.reload({ waitUntil: 'load' });
      await page.waitForSelector('.champion-banner', { timeout: 90_000 });
      await sleep(1500);
      ok((await page.evaluate(() => window.__sonidos)).length === 0, 'al recargar en los resultados no vuelve a sonar');
    }
    await page.context().close();
  }
  ok(visto, 'en 10 partidas alguna la gana la jugadora');

  console.log('== B. Sonido en una ronda normal (sin meta): no cambia ==');
  {
    const page = await pagina();
    await page.click('.btn-play-now');
    await page.waitForSelector('.turn-indicator.my-turn', { timeout: 20_000 });
    await page.click('.btn-pass');
    await page.waitForSelector('.results-view .result-entry', { timeout: 40_000 });
    await sleep(1200);
    const gane = /Ganaste la ronda/.test(limpio(await page.locator('.round-summary').textContent()));
    const sonidos = (await page.evaluate(() => window.__sonidos)).join();
    ok(sonidos === (gane ? 'winRound' : 'loseRound'), `suena ${gane ? 'la victoria de ronda' : 'la derrota'}, una vez (${sonidos})`);

    console.log('== C. Descarte que el servidor no acepta (el turno ya había pasado) ==');
    await page.click('.continue-buttons .btn-primary');
    await page.waitForSelector('.round-badge:has-text("Ronda 2")', { timeout: 20_000 });
    await page.waitForSelector('.turn-indicator.my-turn', { timeout: 40_000 });
    let modo = 'rechazo';
    await page.route(/\/api\/games\/[0-9a-f-]{36}\/discard/, (route) => {
      if (modo === 'rechazo') return route.fulfill({ status: 400, body: '' });
      if (modo === 'sin-red') return route.abort('internetdisconnected');
      return route.continue();
    });
    await page.click('.btn-pass');
    let aviso = limpio(await page.locator('.error-msg').first().textContent({ timeout: 5000 }).catch(() => ''));
    ok(/turno/i.test(aviso) && !/conexi[oó]n/i.test(aviso), `dice que el turno ya no era suyo, no que revise la conexión: «${aviso}»`);

    console.log('== D. Descarte que no llega (sin red): el aviso de conexión sigue ==');
    modo = 'sin-red';
    await page.click('.btn-pass');
    await sleep(800);
    aviso = limpio(await page.locator('.error-msg').first().textContent({ timeout: 5000 }).catch(() => ''));
    ok(/Comprueba la conexi[oó]n/.test(aviso), `«${aviso}»`);
    modo = 'normal';
    await page.click('.btn-pass');
    ok(await espera(page.waitForSelector('.results-view .result-entry', { timeout: 40_000 })), 'y al tercer intento, con red, el descarte entra');

    console.log('== E. Texto de la ayuda ==');
    await page.click('button:has-text("Salir de la mesa")');
    await page.waitForSelector('.lobby .game-title', { timeout: 10_000 });
    await page.click('.btn-help');
    const ayuda = limpio(await page.locator('.help-modal, .modal, [role=dialog]').first().textContent({ timeout: 5000 }));
    ok(/pulsa Pasar/.test(ayuda) && !/pulsa Descartar sin seleccionar/.test(ayuda), 'para pasar nombra el botón que hay en la mesa: «Pasar»');
    ok(!/mayor puntuaci[oó]n/.test(ayuda), 'no dice que se elige la combinación de mayor puntuación');
    ok(/jugada más alta/.test(ayuda), 'dice que se elige la que da la jugada más alta');
    await page.context().close();
  }
} catch (e) {
  fallar(e);
} finally {
  await browser.close();
}
terminar();
