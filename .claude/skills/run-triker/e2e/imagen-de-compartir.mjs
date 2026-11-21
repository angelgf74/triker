// La imagen que se comparte con una jugada (js/cardImage.js) va con el aspecto de la
// app: fondo liso, las cartas sobre el tapete, los grupos marcados como en resultados
// (barra bajo la carta; el segundo grupo, en blanco) y sin emojis, que cada sistema
// pinta a su manera. Se genera la imagen de verdad y se leen sus píxeles.
// Vale contra producción: no crea ninguna partida.
import fs from 'node:fs';
import { join } from 'node:path';
import { chromium, WEB, MOVIL, SHOTS, ok, fallar, terminar } from './lib/comun.mjs';

const browser = await chromium.launch({ channel: 'chrome', headless: true });
try {
  const ctx = await browser.newContext({ ...MOVIL });
  const page = await ctx.newPage();
  page.on('pageerror', (e) => { if (!/ServiceWorker/.test(e.message)) console.log(`  [pageerror] ${e.message.slice(0, 160)}`); });
  await page.goto(WEB + '/', { waitUntil: 'load' });
  await page.waitForSelector('.lobby .game-title', { timeout: 90_000 });

  const r = await page.evaluate(async () => {
    // Doble pareja: dos cartas del primer grupo (dorado), dos del segundo (blanco) y una suelta.
    const cartas = [
      { letter: 'E', figure: '▲', color: '#F57C00', groupColor: '#f0c040' },
      { letter: 'T', figure: '●', color: '#F57C00', groupColor: '#f0c040' },
      { letter: 'A', figure: '●', color: '#e53935', groupColor: '#ffffff' },
      { letter: 'A', figure: '★', color: '#e53935', groupColor: '#ffffff' },
      { letter: 'F', figure: '▲', color: '#795548', groupColor: null },
    ];
    const url = await window.TrikerHandImage.render(cartas, 'Doble Pareja Mixta', 'jugada nº 12 de 16', 'Ana');
    const img = new Image();
    await new Promise((bien, mal) => { img.onload = bien; img.onerror = mal; img.src = url; });
    const c = document.createElement('canvas');
    c.width = img.width; c.height = img.height;
    const g = c.getContext('2d', { willReadFrequently: true });
    g.drawImage(img, 0, 0);
    const px = (x, y) => [...g.getImageData(Math.round(x), Math.round(y), 1, 1).data].slice(0, 3);
    const es = (p, [r2, g2, b2], margen = 6) => Math.abs(p[0] - r2) <= margen && Math.abs(p[1] - g2) <= margen && Math.abs(p[2] - b2) <= margen;
    const blanco = (p) => p[0] > 235 && p[1] > 235 && p[2] > 235;
    const estilo = getComputedStyle(document.documentElement);
    const hex = (v) => { const h = estilo.getPropertyValue(v).trim(); return [1, 3, 5].map((i) => parseInt(h.slice(i, i + 2), 16)); };
    const [FONDO, TAPETE, ORO] = [hex('--fondo'), hex('--tapete'), hex('--acento')];

    // Dónde están las cartas: se busca la primera por una columna que la cruza.
    const x1 = img.width * 0.12;
    let arriba = -1, abajo = -1;
    for (let y = 0; y < img.height; y++) if (blanco(px(x1, y))) { if (arriba < 0) arriba = y; abajo = y; }
    // Cartas en la fila de más abajo de su cara, donde solo hay blanco y la letra de la esquina.
    const fila = abajo - 4, tramos = [];
    for (let x = 0, desde = -1; x <= img.width; x++) {
      const b = x < img.width && blanco(px(x, fila));
      if (b && desde < 0) desde = x;
      if (!b && desde >= 0) { if (x - desde > 20) tramos.push([desde, x - 1]); desde = -1; }
    }
    // Trozos de una misma carta partidos por la letra de la esquina: se unen los que casi se tocan.
    const cartasVistas = tramos.reduce((a, t) => { const u = a[a.length - 1]; if (u && t[0] - u[1] < 40 && (t[1] - u[0]) < img.width / 4) u[1] = t[1]; else a.push([...t]); return a; }, []);
    const bajo = (i) => { // el color de la barra que hay justo debajo de la carta i
      const x = (cartasVistas[i][0] + cartasVistas[i][1]) / 2;
      for (let y = abajo + 3; y < abajo + 30; y++) { const p = px(x, y); if (es(p, ORO, 12)) return 'oro'; if (blanco(p)) return 'blanco'; }
      return 'nada';
    };
    const hueco = cartasVistas.length > 1 ? px((cartasVistas[0][1] + cartasVistas[1][0]) / 2, (arriba + abajo) / 2) : [0, 0, 0];
    return {
      tipo: url.slice(0, 22), ancho: img.width, alto: img.height, url,
      esquinas: [px(2, 2), px(img.width - 3, 2), px(2, img.height - 3), px(img.width - 3, img.height - 3)].map((p) => es(p, FONDO)),
      cartas: cartasVistas.length,
      hueco: es(hueco, TAPETE), huecoColor: hueco.join(','),
      barras: cartasVistas.length === 5 ? [0, 1, 2, 3, 4].map(bajo) : [],
    };
  });

  ok(r.tipo === 'data:image/png;base64,' && r.ancho > 600 && r.alto > 300, `se genera un PNG de ${r.ancho}×${r.alto}`);
  ok(r.esquinas.every(Boolean), `el fondo es liso, el de la app, de arriba abajo (${r.esquinas.map((e) => (e ? 'sí' : 'no')).join(' ')})`);
  ok(r.cartas === 5, `con las cinco cartas (${r.cartas} encontradas)`);
  ok(r.hueco, `sobre el tapete de la mesa (entre dos cartas hay rgb ${r.huecoColor})`);
  ok(r.barras.join(' ') === 'oro oro blanco blanco nada', `y cada grupo lleva su barra, como en resultados: ${r.barras.join(', ')}`);
  fs.writeFileSync(join(SHOTS, 'imagen-de-compartir.png'), Buffer.from(r.url.split(',')[1], 'base64'));

  // El código de la imagen no puede llevar emojis: en un <canvas> los pinta el sistema.
  const fuente = await (await ctx.request.get(WEB + '/js/cardImage.js')).text();
  const emojis = [...new Set(fuente.match(/\p{Extended_Pictographic}/gu) ?? [])];
  ok(emojis.length === 0, `sin emojis en el dibujo${emojis.length ? ': ' + emojis.join(' ') : ''}`);
  ok(/Nunito Sans/.test(fuente), 'y con la tipografía de la app');
} catch (e) {
  fallar(e);
} finally {
  await browser.close();
}
terminar();
