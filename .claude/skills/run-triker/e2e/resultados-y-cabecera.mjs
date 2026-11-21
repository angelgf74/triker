// Resultados y cabecera a 360 px: las marcas de grupo no usan colores de carta, en
// mesas grandes solo van abiertas las tres primeras manos y la tuya (la de 16 se
// consigue retocando la petición de «Jugar contra bots»), y la cabecera va sin el
// contador del mazo. Vale contra producción: crea dos mesas y sale de ellas.
import { join } from 'node:path';
import { chromium, WEB, MOVIL, SHOTS, RUTA_CREAR, ok, fallar, terminar } from './lib/comun.mjs';

const PRE = process.env.SHOT_PRE ?? 'resultados-';
const ALTO = 740;
const altoPagina = (page) => page.evaluate(() => document.documentElement.scrollHeight);
// Colores de carta que no pueden servir de marca de grupo (son un atributo que puntúa).
const DE_CARTA = ['#3b82f6', '#f59e0b', '#a855f7', '#22c55e', '#ef4444', '#06b6d4', '#ec4899'];

const browser = await chromium.launch({ channel: 'chrome', headless: true });
async function resultados(bots) {
  const ctx = await browser.newContext({ ...MOVIL });
  const page = await ctx.newPage();
  page.on('pageerror', (e) => { if (!/ServiceWorker/.test(e.message)) console.log(`  [pageerror] ${e.message.slice(0, 160)}`); });
  if (bots) {
    await page.route(RUTA_CREAR, async (route) => {
      if (route.request().method() !== 'POST') return route.continue();
      await route.continue({ postData: JSON.stringify({ ...route.request().postDataJSON(), bots }) });
    });
  }
  await page.goto(WEB + '/', { waitUntil: 'load' });
  await page.waitForSelector('.lobby .game-title', { timeout: 90_000 });
  await page.fill('#nombre-jugador', 'Prueba');
  await page.click('.btn-play-now');
  await page.waitForSelector('.turn-indicator.my-turn', { timeout: 20_000 });
  const cabecera = await page.locator('.game-header').evaluate((e) => ({ mazoVisible: !!e.querySelector('.deck-count')?.offsetParent, titulo: e.querySelector('.game-title-small').scrollWidth <= e.querySelector('.game-title-small').clientWidth }));
  await page.click('.btn-pass');
  await page.waitForSelector('.results-view .result-entry', { timeout: 60_000 });
  await page.waitForTimeout(600);
  return { page, cabecera };
}
const salir = async (page) => {
  await page.click('button:has-text("Salir de la mesa")', { timeout: 5000 });
  await page.waitForSelector('.lobby .game-title', { timeout: 15_000 });
  await page.context().close();
};

try {
  console.log('== A. Mesa de 4: cabecera y marcas de grupo ==');
  {
    const { page, cabecera } = await resultados();
    ok(!cabecera.mazoVisible, 'en móvil la cabecera no enseña el contador del mazo');
    ok(cabecera.titulo, 'y el nombre de la mesa cabe entero');
    const marcas = await page.locator('.rc-slot.rc-grouped').evaluateAll((els) => [...new Set(els.map((e) => getComputedStyle(e).getPropertyValue('--gc').trim().toLowerCase()))]);
    ok(marcas.length > 0 && marcas.every((m) => !['#3b82f6', '#f59e0b', '#a855f7', '#22c55e', '#ef4444', '#06b6d4', '#ec4899'].includes(m)), `las marcas de grupo no usan colores de carta: ${marcas.join(', ') || 'ninguna marca en esta ronda'}`);
    ok(await page.locator('.result-entry.result-collapsed').count() === 0, 'con 4 jugadores todas las manos van abiertas');
    await page.locator('.results-list').screenshot({ path: join(SHOTS, `${PRE}grupos.png`) });
    await salir(page);
  }

  console.log('== B. Mesa de 16: resultados plegados ==');
  {
    const { page } = await resultados(15);
    const total = await page.locator('.result-entry').count();
    const plegadas = await page.locator('.result-entry.result-collapsed').count();
    const abiertas = total - plegadas;
    ok(total === 16, `están los 16 jugadores (${total})`);
    ok(abiertas >= 3 && abiertas <= 4, `abiertas solo las tres primeras y la tuya (${abiertas} abiertas, ${plegadas} plegadas)`);
    ok(await page.locator('.result-entry.my-result.result-collapsed').count() === 0, 'tu mano nunca va plegada');
    const linea = (await page.locator('.result-entry.result-collapsed').first().textContent()).trim().replace(/\s+/g, ' ');
    ok(/\d+º|🥇|🥈|🥉/.test(linea) && /\d+\/16/.test(linea), `cada línea plegada dice puesto, nombre y jugada: «${linea}»`);
    // Con la jugada de nombre más largo, quien encoge es la jugada, no el nombre del
    // jugador: «Del…» no dice quién es, y el puesto en la escala sigue a la vista.
    // Se escribe en la primera fila plegada el peor caso, que al azar no siempre sale.
    const justa = await page.locator('.result-entry.result-collapsed').first().evaluate((fila) => {
      const nombre = fila.querySelector('.result-player-name');
      const jugada = fila.querySelector('.rc-hand-name') ?? [...fila.querySelector('.rc-hand').childNodes].find((n) => n.nodeType === 3 && n.textContent.trim());
      nombre.textContent = 'Vulcano';
      jugada.textContent = 'Doble Pareja Fuerte';
      const escala = fila.querySelector('.grp-ladder').getBoundingClientRect(), caja = fila.getBoundingClientRect();
      return { nombreEntero: nombre.scrollWidth <= nombre.clientWidth, escalaDentro: escala.right <= caja.right && escala.width > 20, alto: Math.round(caja.height) };
    });
    ok(justa.nombreEntero, 'en una fila plegada, con la jugada más larga el nombre del jugador se lee entero');
    ok(justa.escalaDentro && justa.alto <= 56, `y el puesto en la escala sigue dentro, en una sola línea (alto ${justa.alto} px)`);
    await page.locator('.result-entry.result-collapsed').first().screenshot({ path: join(SHOTS, `${PRE}fila-plegada.png`) });
    const alto = await altoPagina(page);
    ok(alto < 2600, `la pantalla mide ${alto} px (${(alto / ALTO).toFixed(1)} pantallas; eran 3.657)`);
    await page.screenshot({ path: join(SHOTS, `${PRE}resultados16.png`), fullPage: true });
    await page.locator('.result-entry.result-collapsed').first().click();
    await page.waitForTimeout(300);
    ok(await page.locator('.result-entry.result-collapsed').count() === plegadas - 1, 'al tocar una línea se abre esa mano');
    ok(await page.locator('.result-entry:not(.result-collapsed)').nth(3).locator('.card').count() === 5 || await page.locator('.result-entry:not(.result-collapsed) .card').count() === (abiertas + 1) * 5, 'con sus 5 cartas');
    await page.click('.btn-open-all');
    await page.waitForTimeout(300);
    ok(await page.locator('.result-entry.result-collapsed').count() === 0 && await page.locator('.result-entry .card').count() === 80, '«Ver todas las manos» las abre todas');
    await salir(page);
  }
} catch (e) {
  fallar(e);
} finally {
  await browser.close();
}
terminar();
