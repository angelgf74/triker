// El lobby a 360 px: bajo el logo, un abanico de cartas de adorno que enseña qué es una
// carta de Triker (antes la mitad de la pantalla estaba vacía); el nombre con su etiqueta
// encima; y la lista de partidas con los mismos paneles y botones neutros que el resto.
// El abanico no puede empujar «Jugar contra bots» fuera de la primera pantalla.
// Solo contra localhost: crea por la API una mesa que queda a la vista en el lobby.
import { join } from 'node:path';
import { chromium, WEB, API, MOVIL, SHOTS, ok, limpio, fallar, terminar, soloLocal } from './lib/comun.mjs';

soloLocal();

const NEUTRO = 'rgb(69, 73, 119)';   // --linea-fuerte
const MESA = `Lobby ${Date.now() % 100000}`;
const creada = await (await fetch(`${API}/api/games`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ playerName: 'Berto', gameName: MESA }) })).json();

const browser = await chromium.launch({ channel: 'chrome', headless: true });
try {
  const ctx = await browser.newContext({ ...MOVIL });
  const page = await ctx.newPage();
  page.on('pageerror', (e) => { if (!/ServiceWorker/.test(e.message)) console.log(`  [pageerror] ${e.message.slice(0, 160)}`); });
  await page.goto(WEB + '/', { waitUntil: 'load' });
  await page.waitForSelector('.lobby .game-title', { timeout: 90_000 });

  console.log('== A. Abanico ==');
  const abanico = await page.locator('.abanico-cartas').evaluate((e) => ({
    letras: [...e.querySelectorAll('.card .card-corner.top-left .card-letter')].map((l) => l.textContent.trim()).join(''),
    oculto: e.getAttribute('aria-hidden'),
    conFoco: e.querySelectorAll('[tabindex], [role="button"]').length,
    toque: getComputedStyle(e.querySelector('.card')).pointerEvents,
    giros: [...e.querySelectorAll('.card')].map((c) => getComputedStyle(c).transform !== 'none').filter(Boolean).length,
  })).catch(() => null);
  ok(abanico?.letras === 'TRIKE', `cinco cartas que deletrean el juego (${abanico?.letras ?? 'no hay abanico'})`);
  ok(abanico?.giros >= 4, `abiertas en abanico (${abanico?.giros} giradas)`);
  ok(abanico?.oculto === 'true' && abanico?.conFoco === 0 && abanico?.toque === 'none', 'es adorno: ni se lee, ni coge el foco, ni se puede tocar');
  ok(/512 cartas/.test(limpio(await page.locator('.abanico-pie').textContent().catch(() => ''))), 'con su pie: de qué está hecho el mazo');

  console.log('== B. Lo importante sigue en la primera pantalla ==');
  const fin = (sel) => page.locator(sel).evaluate((e) => Math.round(e.getBoundingClientRect().bottom));
  const [jugar, nueva] = [await fin('.btn-play-now'), await fin('.btn-new-game')];
  ok(jugar <= 740 && nueva <= 740, `«Jugar contra bots» y «Nueva partida» se ven sin bajar (acaban a ${jugar} y ${nueva} px de 740)`);
  ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), 'sin scroll horizontal');

  console.log('== C. Tu nombre ==');
  const nombre = await page.evaluate(() => {
    const et = document.querySelector('label[for="nombre-jugador"]'), en = document.querySelector('#nombre-jugador');
    const [a, b] = [et.getBoundingClientRect(), en.getBoundingClientRect()];
    return { texto: et.textContent.trim(), encima: a.bottom <= b.top + 1, alto: Math.round(b.height), ancho: Math.round(b.width) };
  });
  ok(nombre.encima && nombre.texto === 'Tu nombre', `la etiqueta va encima del campo: «${nombre.texto}»`);
  ok(nombre.alto >= 48 && nombre.ancho >= 300, `y el campo ocupa el ancho, con ${nombre.alto} px de alto`);
  await page.click('.btn-play-now');
  await page.waitForSelector('#nombre-aviso');
  await page.waitForTimeout(400);   // el borde cambia con una transición de 0,2 s
  const pide = await page.evaluate(() => {
    const en = document.querySelector('#nombre-jugador'), av = document.querySelector('#nombre-aviso');
    return { borde: getComputedStyle(en).borderTopColor, aviso: av.textContent.trim(), debajo: av.getBoundingClientRect().top >= en.getBoundingClientRect().bottom - 1 };
  });
  ok(Number(pide.borde.match(/\d+/)[0]) > 200 && pide.debajo, `sin nombre, el campo se marca y el aviso sale debajo: «${pide.aviso}» (borde ${pide.borde})`);
  await page.screenshot({ path: join(SHOTS, 'lobby-01.png') });

  console.log('== D. Lista de partidas ==');
  await page.fill('#nombre-jugador', 'Ana');
  await page.click('.btn-refresh');
  const fila = page.locator(`.game-card:has-text("${MESA}")`);
  await fila.waitFor({ timeout: 10_000 });
  const estilo = await fila.evaluate((e) => {
    const de = (sel, p) => getComputedStyle(e.querySelector(sel))[p];
    return { fondo: getComputedStyle(e).backgroundColor, ver: de('.btn-spectate', 'borderTopColor'), verAlto: Math.round(e.querySelector('.btn-spectate').getBoundingClientRect().height),
             unirse: de('.btn-join', 'borderTopColor'), estado: de('.game-status', 'color') };
  });
  const panel = await page.evaluate(() => { const d = document.createElement('i'); d.style.color = 'var(--panel)'; document.body.append(d); const c = getComputedStyle(d).color; d.remove(); return c; });
  ok(estilo.fondo === panel, `cada partida es un panel como los demás (${estilo.fondo})`);
  ok(estilo.unirse === 'rgb(240, 192, 64)' && estilo.ver === NEUTRO, `«Unirse» lleva el acento y «Ver» es neutro (${estilo.unirse} / ${estilo.ver})`);
  ok(estilo.verAlto >= 44, `los dos botones se pueden pulsar con el dedo («Ver» mide ${estilo.verAlto} px)`);
  await page.screenshot({ path: join(SHOTS, 'lobby-02-con-partida.png'), fullPage: true });
} catch (e) {
  fallar(e);
} finally {
  await browser.close();
  await fetch(`${API}/api/games/${creada.gameState.id}/leave?playerId=${creada.playerId}`, { method: 'POST', headers: { 'X-Triker-Token': creada.playerToken } });
}
const quedan = (await (await fetch(`${API}/api/games`)).json()).filter((g) => g.name === MESA);
ok(quedan.length === 0, 'la mesa de prueba no queda abierta');
terminar();
