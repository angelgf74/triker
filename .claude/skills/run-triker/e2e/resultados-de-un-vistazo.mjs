// Los resultados de una ronda, de un vistazo: el titular dice cómo te ha ido (antes
// era «Resultados — nombre de la mesa», que no decía nada y partía en dos líneas),
// los puestos van en número (las medallas 🥇🥈🥉 solo se distinguían por el color) y
// el dorado marca dos cosas distintas sin mezclarlas: el aro, tu fila; el número, quien
// gana. Como contra bots no se sabe quién gana, se comprueba que todo cuadra entre sí.
// Vale contra producción: crea una mesa y sale de ella.
import { join } from 'node:path';
import { chromium, WEB, MOVIL, SHOTS, ok, limpio, fallar, terminar } from './lib/comun.mjs';

const DORADO = 'rgb(240, 192, 64)';

const browser = await chromium.launch({ channel: 'chrome', headless: true });
try {
  const ctx = await browser.newContext({ ...MOVIL });
  const page = await ctx.newPage();
  page.on('pageerror', (e) => { if (!/ServiceWorker/.test(e.message)) console.log(`  [pageerror] ${e.message.slice(0, 160)}`); });
  await page.goto(WEB + '/', { waitUntil: 'load' });
  await page.waitForSelector('.lobby .game-title', { timeout: 90_000 });
  await page.fill('#nombre-jugador', 'Ana');
  await page.click('.btn-play-now');
  await page.waitForSelector('.turn-indicator.my-turn', { timeout: 20_000 });
  await page.click('.btn-pass');
  await page.waitForSelector('.results-view .result-entry', { timeout: 40_000 });
  await page.waitForTimeout(600);

  const filas = await page.locator('.result-entry').evaluateAll((els) => els.map((e) => {
    const puesto = e.querySelector('.rank-medal');
    return {
      mia: e.classList.contains('my-result'),
      puesto: puesto.textContent.trim(),
      colorPuesto: getComputedStyle(puesto).color,
      aro: getComputedStyle(e).boxShadow,
      borde: getComputedStyle(e).borderTopColor,
    };
  }));
  const mia = filas.find((f) => f.mia);
  const gano = mia?.puesto === '1º';

  console.log('== A. Titular ==');
  const titular = limpio(await page.locator('.round-summary h2').textContent().catch(() => ''));
  ok(titular.length > 0, `el resumen lleva un titular: «${titular}»`);
  ok(gano ? /^Ganaste la ronda/.test(titular) : /^(Ganó|Ganaron) .+ con .+/.test(titular), `y dice cómo te ha ido (${gano ? 'ganas' : 'gana otro'})`);
  ok(await page.locator('.round-summary svg.icono').count() === (gano ? 1 : 0), `el trofeo sale solo si ganas (${gano ? 'sale' : 'no sale'})`);
  const cabecera = limpio(await page.locator('.results-header').textContent());
  ok(/Partida de Ana/.test(cabecera) && /Ronda 1/.test(cabecera) && !/Resultados —/.test(cabecera), `la mesa y la ronda van encima, en pequeño: «${cabecera.slice(0, 60)}»`);
  const tuyo = limpio(await page.locator('.rs-mine').textContent());
  ok(new RegExp(`^${mia?.puesto} de 4 `).test(tuyo) && /\d+\/16/.test(tuyo), `debajo, tu puesto y tu jugada: «${tuyo}»`);
  const yTitular = await page.locator('.round-summary h2').evaluate((e) => e.getBoundingClientRect().bottom);
  ok(yTitular < 300, `todo eso sin bajar: el titular acaba a ${Math.round(yTitular)} px del borde de arriba`);

  console.log('== B. Filas ==');
  ok(filas.length === 4 && filas.every((f) => /^\d+º$/.test(f.puesto)), `los puestos van en número: ${filas.map((f) => f.puesto).join(' ')}`);
  ok(filas.every((f) => (f.colorPuesto === DORADO) === (f.puesto === '1º')), `en dorado solo el de quien gana (${filas.map((f) => `${f.puesto} ${f.colorPuesto === DORADO ? 'dorado' : 'neutro'}`).join(', ')})`);
  const conAro = filas.filter((f) => f.aro.includes(DORADO) || f.borde === DORADO || /rgba\(240, 192, 64/.test(f.borde));
  ok(conAro.length === 1 && conAro[0].mia, `el aro dorado lo lleva tu fila y ninguna más (${conAro.length} con aro)`);
  await page.screenshot({ path: join(SHOTS, 'resultados-vistazo.png'), fullPage: true });

  await page.click('button:has-text("Salir de la mesa")');
  await page.waitForSelector('.lobby .game-title', { timeout: 15_000 });
} catch (e) {
  fallar(e);
} finally {
  await browser.close();
}
terminar();
