// Ritmo de los bots (BotPacing): cuánto tarda una ronda desde la jugada del
// humano hasta que se revela, medido por la API. La petición del descarte no
// responde hasta que acaban los bots, así que su duración es la de la tanda.
//
//   node ritmo-de-los-bots.mjs [bots,bots,...] [min-max,min-max,...]
//
// Sin argumentos mide 1, 3, 4, 7 y 15 bots contra los tiempos esperados. Vale
// contra producción (crea una mesa por medida y sale de ella).
import { API, ok, terminar } from './lib/comun.mjs';

const BOTS = (process.argv[2] ?? '1,3,4,7,15').split(',').map(Number);
const RANGOS = (process.argv[3] ?? (process.argv[2] ? '' : '1.6-2.4,5.5-6.8,6.0-7.3,7.0-8.8,9.4-11.5'))
  .split(',').filter(Boolean).map((r) => r.split('-').map(Number));

for (const [i, bots] of BOTS.entries()) {
  const crear = await fetch(`${API}/api/games`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ playerName: 'Prueba', gameName: `Ritmo ${bots}`, bots }),
  });
  if (!crear.ok) { ok(false, `crear con ${bots} bots: ${crear.status} ${await crear.text()}`); continue; }
  const j = await crear.json();
  const id = j.gameState.id, cab = { 'Content-Type': 'application/json', 'X-Triker-Token': j.playerToken };
  const jugadores = j.gameState.players.length;

  const t0 = Date.now();
  const d = await fetch(`${API}/api/games/${id}/discard?playerId=${j.playerId}`, { method: 'POST', headers: cab, body: JSON.stringify({ cardIds: [] }) });
  const seg = (Date.now() - t0) / 1000;
  const estado = await (await fetch(`${API}/api/games/${id}?playerId=${j.playerId}`, { headers: cab })).json();

  const rango = RANGOS[i];
  const enRango = !rango || (seg >= rango[0] && seg <= rango[1]);
  ok(d.ok && estado.status === 'Finished' && estado.results?.length === jugadores && enRango,
     `${bots} bots (${jugadores} en la mesa): ${seg.toFixed(1)} s desde tu jugada hasta los resultados${rango ? ` (esperado entre ${rango[0]} y ${rango[1]})` : ''}`);

  await fetch(`${API}/api/games/${id}/leave?playerId=${j.playerId}`, { method: 'POST', headers: cab });
}
const abiertas = (await (await fetch(`${API}/api/games`)).json()).filter((g) => /^Ritmo /.test(g.name));
ok(abiertas.length === 0, 'no queda ninguna mesa de prueba abierta');
terminar();
