// Corte de red de verdad para las pruebas: la conexión del hub pasa por Playwright
// (routeWebSocket), que puede cerrarla y rechazar las nuevas; las peticiones a la
// API se abortan mientras dura el corte.
//
// context.setOffline no vale: no toca un WebSocket ya abierto, y la página sigue
// recibiendo como si nada.
//
//   soloHub: el corte afecta solo al hub (negociación y WebSocket), no al resto de
//            la API. Sirve para hacer fallar la primera conexión del hub con la mesa
//            ya cargada por REST.
//   cortado: empezar ya sin red.
//
// Ojo al cortar: el hub conecta DESPUÉS de pintar la mesa. Cortar nada más aparecer
// el turno prueba el fallo de la primera conexión, no una reconexión.
import { API } from './comun.mjs';

export async function conCorte(ctx, { soloHub = false, cortado = false } = {}) {
  const host = new URL(API).host;
  const vivos = new Set();
  await ctx.routeWebSocket(/\/gamehub/, (ws) => {
    if (cortado) { ws.close({ code: 1001, reason: 'sin red' }); return; }
    ws.connectToServer();
    vivos.add(ws);
    ws.onClose(() => vivos.delete(ws));
  });
  const deLaApi = (url) => url.host === host && (!soloHub || url.pathname.startsWith('/gamehub'));
  await ctx.route(deLaApi, (r) => (cortado ? r.abort('internetdisconnected') : r.continue()));
  return {
    async cortar() {
      cortado = true;
      for (const ws of [...vivos]) await ws.close({ code: 1001, reason: 'sin red' }).catch(() => {});
      vivos.clear();
    },
    volver() { cortado = false; },
  };
}
