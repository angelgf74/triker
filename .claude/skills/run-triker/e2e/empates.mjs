// Empates en la pantalla de resultados. Las manos no se pueden forzar en la app
// real, así que se juega una ronda de verdad y se retoca el estado que la página
// recibe por REST al recargar (con el hub cortado, para que no lo pise el real).
// Solo contra localhost.
import { join } from 'node:path';
import { chromium, WEB, MOVIL, SHOTS, ok, limpio, fallar, terminar, soloLocal } from './lib/comun.mjs';

soloLocal();

import { conCorte } from './lib/corte.mjs';


// Retoca los resultados: `arriba` (ids) comparten la mejor jugada; `abajo` (ids)
// se quedan con la peor. El puesto se recalcula como lo hace la API.
function retocar(estado, arriba, abajo) {
  const r = estado.results;
  const nivel = new Map();                       // jugada -> orden (0 = la mejor), según venía ordenado
  r.forEach((x, i) => { if (!nivel.has(x.handRank)) nivel.set(x.handRank, i); });
  const mejor = { handRank: r[0].handRank, handName: r[0].handName };
  const peor = { handRank: r[r.length - 1].handRank, handName: r[r.length - 1].handName };
  for (const x of r) {
    if (arriba.includes(x.playerId)) Object.assign(x, mejor);
    else if (abajo.includes(x.playerId)) Object.assign(x, peor);
  }
  for (const x of r) x.rank = r.filter((o) => nivel.get(o.handRank) < nivel.get(x.handRank)).length + 1;
  r.sort((a, b) => a.rank - b.rank);
  return mejor.handRank !== peor.handRank;       // false: todos tenían la misma jugada, no sirve
}

const browser = await chromium.launch({ channel: 'chrome', headless: true });
async function resultadosCon(preparar) {
  for (let intento = 0; intento < 4; intento++) {
    const ctx = await browser.newContext({ ...MOVIL });
    const red = await conCorte(ctx, { soloHub: true });
    const page = await ctx.newPage();
    page.on('pageerror', (e) => { if (!/ServiceWorker/.test(e.message)) console.log(`  [pageerror] ${e.message.slice(0, 160)}`); });
    await page.goto(WEB + '/', { waitUntil: 'load' });
    await page.waitForSelector('.lobby .game-title', { timeout: 90_000 });
    await page.fill('#nombre-jugador', 'Ana');
    await page.click('.btn-play-now');
    await page.waitForSelector('.turn-indicator.my-turn', { timeout: 20_000 });
    await page.click('.btn-pass');
    await page.waitForSelector('.results-view .result-entry', { timeout: 40_000 });

    let info = null;
    await page.route(/\/api\/games\/[0-9a-f-]{36}\?/, async (route) => {
      if (route.request().method() !== 'GET') return route.continue();
      const res = await route.fetch();
      const estado = await res.json();
      if (estado.results?.length) info = preparar(estado);
      await route.fulfill({ response: res, json: estado });
    });
    await red.cortar();
    await page.reload({ waitUntil: 'load' });
    await page.waitForSelector('.results-view .result-entry', { timeout: 90_000 });
    if (info?.vale) return { page, info };
    await ctx.close();                            // todos con la misma jugada: otra ronda
  }
  throw new Error('no salió una ronda que sirviera para montar el empate');
}

try {
  console.log('== A. Empatas en cabeza con otro jugador ==');
  let { page, info } = await resultadosCon((e) => {
    const yo = e.myPlayerId, otro = e.results.find((r) => r.playerId !== yo);
    const vale = retocar(e, [yo, otro.playerId], e.results.filter((r) => r.playerId !== yo && r.playerId !== otro.playerId).map((r) => r.playerId));
    return { vale, otro: otro.playerName, jugada: e.results[0].handName };
  });
  let resumen = limpio(await page.locator('.round-summary').textContent());
  console.log(`  resumen: «${resumen}»`);
  ok(/1º de 4/.test(resumen), 'tu puesto es 1º de 4');
  ok(new RegExp(`Ganaste la ronda junto a ${info.otro}`).test(resumen), `dice que ganaste junto a ${info.otro}, no como si hubieras ganado en solitario ni como si hubiera ganado otro`);
  const medallas = (await page.locator('.result-entry .rank-medal').allTextContents()).map(limpio);
  ok(medallas[0] === '1º' && medallas[1] === '1º', `los dos empatados son primeros: ${medallas.join(' ')}`);
  ok(medallas[2] === '3º' && medallas[3] === '3º', 'y los siguientes son terceros: no hay segundo');
  ok(await page.locator('.result-entry.rank-1').count() === 2, 'los dos empatados van resaltados como ganadores');
  await page.screenshot({ path: join(SHOTS, 'm01-empate-ganas.png') });
  await page.context().close();

  console.log('== B. Empatan otros dos en cabeza y tú no ==');
  ({ page, info } = await resultadosCon((e) => {
    const yo = e.myPlayerId, otros = e.results.filter((r) => r.playerId !== yo);
    const vale = retocar(e, [otros[0].playerId, otros[1].playerId], [yo, otros[2].playerId]);
    return { vale, a: otros[0].playerName, b: otros[1].playerName, jugada: e.results[0].handName };
  }));
  resumen = limpio(await page.locator('.round-summary').textContent());
  console.log(`  resumen: «${resumen}»`);
  ok(/3º de 4/.test(resumen), 'tu puesto es 3º de 4');
  ok(new RegExp(`Ganaron (${info.a} y ${info.b}|${info.b} y ${info.a}) con ${info.jugada}`).test(resumen), `nombra a los dos ganadores en plural: ${info.a} y ${info.b}`);
  ok(!/Ganaste/.test(resumen), 'y no te da por ganadora');
  await page.screenshot({ path: join(SHOTS, 'm02-empate-otros.png') });
  await page.context().close();
} catch (e) {
  fallar(e);
} finally {
  await browser.close();
}
terminar();
