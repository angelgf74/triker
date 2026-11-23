// Comprobación en local del enlace «Privacidad de los anuncios» del lobby, con
// el puente de la app Android simulado: cuándo se ve, que mide 44 px y que abre
// el formulario. Solo contra localhost.
import { join } from 'node:path';
import { chromium, WEB, MOVIL, SHOTS, ok, fallar, terminar, soloLocal } from './lib/comun.mjs';

soloLocal();

async function lobby(browser, puente) {
  const ctx = await browser.newContext({ ...MOVIL });
  const page = await ctx.newPage();
  const errores = [];
  page.on('pageerror', (e) => { if (!/ServiceWorker/.test(e.message)) errores.push(e.message); });
  if (puente) await page.addInitScript(puente);
  await page.goto(WEB + '/', { waitUntil: 'load' });
  await page.waitForSelector('.lobby .game-title', { timeout: 90_000 });
  await page.waitForSelector('.loading', { state: 'detached', timeout: 10_000 }).catch(() => {});
  return { page, errores };
}
const visible = (page) => page.locator('.lobby-footer').isVisible();

const browser = await chromium.launch({ channel: 'chrome', headless: true });
try {
  console.log('== A. Navegador normal (sin app) ==');
  const a = await lobby(browser, null);
  ok(await a.page.locator('.lobby-footer').count() === 1 && !await visible(a.page), 'el enlace no se ve');
  ok(a.errores.length === 0, `sin errores de página ${JSON.stringify(a.errores)}`);

  console.log('== B. App anterior a la 1.4.0 (puente sin los métodos nuevos) ==');
  const b = await lobby(browser, () => { window.AndroidAds = { showInterstitial() {} }; });
  ok(!await visible(b.page), 'el enlace no se ve');
  ok(b.errores.length === 0, `sin errores de página ${JSON.stringify(b.errores)}`);

  console.log('== C. App 1.4.0 ==');
  const c = await lobby(browser, () => {
    window.__obligatorias = false; window.__abierto = 0;
    window.AndroidAds = {
      showInterstitial() {},
      privacyOptionsRequired() { return window.__obligatorias; },
      showPrivacyOptions() { window.__abierto++; },
    };
  });
  ok(!await visible(c.page), 'mientras el consentimiento no es obligatorio (o aún no se sabe), no se ve');
  // Lo que hace la app cuando la UMP responde: avisa a la web.
  await c.page.evaluate(() => { window.__obligatorias = true; window.TrikerAds.refreshPrivacyLink(); });
  ok(await visible(c.page), 'cuando la app avisa de que es obligatorio, aparece');
  // Fuera de la app está oculto y diseno-movil no llega a medirlo: se mide aquí.
  const caja = await c.page.locator('.lobby-footer .link-button').boundingBox();
  ok(caja.width >= 44 && caja.height >= 44, `se toca con el dedo: al menos 44×44 px (${caja.width.toFixed(1)}×${caja.height.toFixed(1)})`);
  await c.page.fill('#nombre-jugador', 'Ana');   // fuerza a Blazor a repintar el lobby
  await c.page.click('.btn-refresh');
  await c.page.waitForTimeout(800);
  ok(await visible(c.page), 'y sigue ahí cuando Blazor repinta');
  await c.page.screenshot({ path: join(SHOTS, 'p01-enlace-privacidad.png'), fullPage: true });
  await c.page.click('.lobby-footer .link-button');
  await c.page.waitForTimeout(300);
  ok(await c.page.evaluate(() => window.__abierto) === 1, 'al pulsarlo pide a la app el formulario de opciones');
  const scroll = await c.page.evaluate(() => document.documentElement.scrollWidth - innerWidth);
  ok(scroll <= 0, `sin scroll horizontal a 360 px (${scroll} px)`);

  console.log('== D. App 1.4.0 donde ya se sabía que es obligatorio (segunda sesión) ==');
  const d = await lobby(browser, () => {
    window.AndroidAds = { showInterstitial() {}, privacyOptionsRequired() { return true; }, showPrivacyOptions() {} };
  });
  ok(await d.page.locator('.lobby-footer').waitFor({ state: 'visible', timeout: 5000 }).then(() => true, () => false), 'el enlace se ve al abrir el lobby, sin esperar aviso');
} catch (e) {
  fallar(e);
} finally {
  await browser.close();
}
terminar();
