// Vista previa al compartir un enlace (WhatsApp, Telegram...): lo que lee quien la
// genera, que no ejecuta JavaScript. Mira el HTML tal cual llega, pidiendo un enlace
// de invitación, y que la imagen se sirve. Vale contra producción.
//
// No comprueba cómo la enseña cada app: cada una recorta la imagen a su manera y
// guarda la vista previa en caché.
import { WEB, ok, terminar } from './lib/comun.mjs';

const html = await (await fetch(`${WEB}/?join=00000000-0000-0000-0000-000000000001`)).text();
const meta = (clave) => new RegExp(`<meta\\s+(?:property|name)="${clave}"\\s+content="([^"]*)"`, 'i').exec(html)?.[1] ?? null;

ok((meta('description') ?? '').length >= 50, `descripción: «${meta('description')}»`);
ok(!!meta('og:title'), `og:title: «${meta('og:title')}»`);
ok((meta('og:description') ?? '').length >= 50, `og:description: «${meta('og:description')}»`);
ok(meta('og:type') === 'website', `og:type: ${meta('og:type')}`);
ok(/^https:\/\/[^/]+\/$/.test(meta('og:url') ?? ''), `og:url es una dirección completa: ${meta('og:url')}`);
const img = meta('og:image');
ok(/^https:\/\/[^/]+\/.+\.png$/.test(img ?? ''), `og:image es una dirección completa a un PNG: ${img}`);
ok(meta('og:image:width') === '1024' && meta('og:image:height') === '500', `con su tamaño declarado: ${meta('og:image:width')}x${meta('og:image:height')}`);
ok(meta('twitter:card') === 'summary_large_image', `twitter:card: ${meta('twitter:card')}`);

if (img) {
  // La etiqueta apunta siempre a producción; la imagen se pide a la web que se está probando.
  const r = await fetch(`${WEB}${new URL(img).pathname}`);
  const b = Buffer.from(await r.arrayBuffer());
  ok(r.status === 200 && /image\/png/.test(r.headers.get('content-type') ?? ''), `la imagen se sirve: ${r.status} ${r.headers.get('content-type')}`);
  ok(b.length > 10_000 && b.readUInt32BE(16) === 1024 && b.readUInt32BE(20) === 500, `y mide ${b.length > 24 ? b.readUInt32BE(16) + 'x' + b.readUInt32BE(20) : '?'} (${Math.round(b.length / 1024)} KB)`);
}
terminar();
