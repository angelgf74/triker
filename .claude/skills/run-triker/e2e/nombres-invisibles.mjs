// Nombres con caracteres invisibles (Limites.Nombre), contra la API real: los
// que no dejan nada visible se rechazan y los demás entran limpios. Vale contra
// producción: crea dos mesas y sale de ellas.
import { API, ok, terminar } from './lib/comun.mjs';

const crear = async (playerName, gameName = 'Prueba nombres') => {
  const r = await fetch(`${API}/api/games`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ playerName, gameName }) });
  return { status: r.status, cuerpo: r.ok ? await r.json() : await r.text() };
};
const salir = (j) => fetch(`${API}/api/games/${j.gameState.id}/leave?playerId=${j.playerId}`, { method: 'POST', headers: { 'X-Triker-Token': j.playerToken } });

let r = await crear('\u200B\u200B');
ok(r.status === 400, `un nombre de solo espacios de ancho cero se rechaza (${r.status})`);
r = await crear('\u202E');
ok(r.status === 400, `el carácter que invierte el texto, solo, se rechaza (${r.status})`);
r = await crear('Ana', '\u3164');
ok(r.status === 400, `una partida con nombre invisible se rechaza (${r.status})`);

r = await crear('\u202EA\u200Bna');
const nombre = r.cuerpo?.gameState?.players?.[0]?.name;
ok(r.status === 200 && nombre === 'Ana', `con invisibles por medio entra limpio: «${nombre}» (${[...(nombre ?? '')].length} caracteres)`);
if (r.status === 200) await salir(r.cuerpo);

r = await crear('Ángel 🎴');
ok(r.status === 200 && r.cuerpo.gameState.players[0].name === 'Ángel 🎴', `un nombre normal con tilde y emoji no cambia: «${r.cuerpo?.gameState?.players?.[0]?.name}» (${r.status})`);
if (r.status === 200) await salir(r.cuerpo);

const abiertas = (await (await fetch(`${API}/api/games`)).json()).filter((g) => g.name === 'Prueba nombres');
ok(abiertas.length === 0, 'no queda ninguna mesa de prueba abierta');
terminar();
