// Los rivales, sentados a la mesa: con hasta 8 van arriba como asientos (avatar, nombre
// y estado) en una o dos filas, y tú debajo; con 9 o más no caben sin echar tu mano
// de la pantalla, y se quedan en lista bajo tu zona. Los paneles de historial y sesión
// van debajo de la mesa, para que tu mano esté arriba en todas las rondas.
// Lo que se mide a 360×740: que en tu turno el botón de jugar siga en la primera pantalla.
// Solo contra localhost: crea mesas de 9 y de 10 jugadores.
import { join } from 'node:path';
import { chromium, WEB, MOVIL, SHOTS, RUTA_CREAR, ok, fallar, terminar, soloLocal } from './lib/comun.mjs';

soloLocal();

const browser = await chromium.launch({ channel: 'chrome', headless: true });
async function mesa(bots) {
  const ctx = await browser.newContext({ ...MOVIL });
  const page = await ctx.newPage();
  page.on('pageerror', (e) => { if (!/ServiceWorker/.test(e.message)) console.log(`  [pageerror] ${e.message.slice(0, 200)}`); });
  if (bots) await page.route(RUTA_CREAR, async (route) => {
    if (route.request().method() !== 'POST') return route.continue();
    await route.continue({ postData: JSON.stringify({ ...route.request().postDataJSON(), bots }) });
  });
  await page.goto(WEB + '/', { waitUntil: 'load' });
  await page.waitForSelector('.lobby .game-title', { timeout: 90_000 });
  await page.fill('#nombre-jugador', 'Ana');
  await page.click('.btn-play-now');
  await page.waitForSelector('.turn-indicator.my-turn', { timeout: 20_000 });
  await page.waitForFunction(() => document.querySelectorAll('.hand-cards .card.dealing').length === 0, null, { timeout: 10_000 });
  return page;
}
// Dónde queda cada cosa en la pantalla.
const medir = (page) => page.evaluate(() => {
  const caja = (sel) => { const e = document.querySelector(sel); if (!e) return null; const r = e.getBoundingClientRect(); return { arriba: Math.round(r.top), abajo: Math.round(r.bottom) }; };
  const rivales = [...document.querySelectorAll('.other-player')].map((e) => { const r = e.getBoundingClientRect(); return { y: Math.round(r.top), x: Math.round(r.left), w: Math.round(r.width) }; });
  return {
    asientos: !!document.querySelector('.other-players.asientos'),
    filas: new Set(rivales.map((r) => r.y)).size, rivales: rivales.length,
    anchoMin: Math.min(...rivales.map((r) => r.w)),
    lista: caja('.other-players'), zona: caja('.my-area'), boton: caja('.btn-pass'), paneles: caja('.info-row'),
    avatares: document.querySelectorAll('.other-player .op-avatar').length,
    robots: document.querySelectorAll('.other-player .op-avatar svg.icono').length,
    etiquetasBot: [...document.querySelectorAll('.other-player .tag-bot')].filter((e) => e.getBoundingClientRect().width > 4).length,
    desborde: document.documentElement.scrollWidth - innerWidth,
  };
});
const salir = async (page) => {
  await page.click('.btn-leave');
  await page.click('.leave-confirm .btn-danger');
  await page.waitForSelector('.lobby .game-title', { timeout: 15_000 });
  await page.context().close();
};

try {
  console.log('== A. Mesa de 4: tres asientos en una fila ==');
  {
    const page = await mesa();
    const m = await medir(page);
    ok(m.asientos && m.rivales === 3 && m.filas === 1, `los tres rivales van en asientos, en una fila (${m.rivales} en ${m.filas})`);
    ok(m.lista.abajo <= m.zona.arriba, `encima de tu zona, como al otro lado de la mesa (acaban en ${m.lista.abajo}, tu zona empieza en ${m.zona.arriba})`);
    ok(m.avatares === 3 && m.robots === 3 && m.etiquetasBot === 0, `cada uno con su avatar; los bots, con un robot en vez de la etiqueta (${m.robots} robots, ${m.etiquetasBot} etiquetas a la vista)`);
    ok(m.boton.abajo <= 740 && m.desborde <= 0, `y en tu turno todo cabe en la primera pantalla (el botón acaba en ${m.boton.abajo} de 740)`);
    await page.screenshot({ path: join(SHOTS, 'asientos-01-cuatro.png') });

    // El turno se ve pasar por los asientos mientras juegan los bots.
    await page.click('.btn-pass');
    const visto = await page.waitForSelector('.other-players.asientos .other-player.active-turn .turn-badge', { timeout: 10_000 }).then(() => true, () => false);
    ok(visto, 'cuando juega un rival, su asiento se marca con «su turno»');
    await page.screenshot({ path: join(SHOTS, 'asientos-02-turno-de-otro.png') });

    console.log('== B. Segunda ronda: los paneles, debajo de la mesa ==');
    await page.waitForSelector('.results-view .result-entry', { timeout: 40_000 });
    await page.click('.continue-buttons .btn-primary');
    await page.waitForSelector('.round-badge:has-text("Ronda 2")', { timeout: 20_000 });
    await page.waitForSelector('.info-row', { timeout: 10_000 });
    await page.waitForFunction(() => document.querySelectorAll('.hand-cards .card.dealing').length === 0, null, { timeout: 10_000 });
    await page.waitForTimeout(500);
    const r2 = await medir(page);
    ok(r2.paneles.arriba >= r2.zona.abajo, `el historial y la sesión van debajo de tu zona (empiezan en ${r2.paneles.arriba}, tu zona acaba en ${r2.zona.abajo})`);
    ok(r2.zona.abajo <= 740, `y tu zona entera sigue en la primera pantalla (acaba en ${r2.zona.abajo} de 740)`);
    await page.screenshot({ path: join(SHOTS, 'asientos-03-ronda2.png'), fullPage: true });
    await salir(page);
  }

  console.log('== C. Mesa de 9: ocho asientos en dos filas ==');
  {
    const page = await mesa(8);
    const m = await medir(page);
    ok(m.asientos && m.rivales === 8 && m.filas === 2, `ocho rivales, en asientos y en dos filas (${m.rivales} en ${m.filas})`);
    ok(m.anchoMin >= 64, `sin estrecharse hasta no leerse (el más estrecho mide ${m.anchoMin} px)`);
    ok(m.boton.abajo <= 740 && m.desborde <= 0, `y el botón de jugar sigue en la primera pantalla (acaba en ${m.boton.abajo} de 740)`);
    await page.screenshot({ path: join(SHOTS, 'asientos-04-nueve.png') });
    await salir(page);
  }

  console.log('== D. Mesa de 10: ya no caben, lista debajo ==');
  {
    const page = await mesa(9);
    const m = await medir(page);
    ok(!m.asientos && m.rivales === 9, `con nueve rivales no hay asientos (${m.rivales} rivales)`);
    ok(m.lista.arriba >= m.zona.abajo, `van en lista, debajo de tu zona (empieza en ${m.lista.arriba}, tu zona acaba en ${m.zona.abajo})`);
    ok(m.boton.abajo <= 740, `y tu mano sigue arriba (el botón acaba en ${m.boton.abajo} de 740)`);
    await salir(page);
  }
} catch (e) {
  fallar(e);
} finally {
  await browser.close();
}
terminar();
