// Maneja el WebView de la app de depuración por su puerto de depuración remota.
// Solo la compilación de depuración lo abre (com.angelgf.triker.debug); la de Play, no.
//
//   adb forward tcp:9333 localabstract:webview_devtools_remote_<pid de la app>
//   node cdp.mjs 9333 eval "<javascript>"     evalúa en la página y escribe el resultado
//   node cdp.mjs 9333 crash                   mata el proceso de la página (onRenderProcessGone)
//
// emulador.sh hace el forward con `conecta`.
const [puerto, orden, arg] = process.argv.slice(2);
if (!puerto || !['eval', 'crash'].includes(orden)) {
  console.log('Uso: node cdp.mjs <puerto> eval "<js>" | crash');
  process.exit(2);
}
const destinos = await (await fetch(`http://localhost:${puerto}/json`)).json();
const pagina = destinos.find((d) => d.type === 'page' && /triker/.test(d.url));
if (!pagina) { console.log('sin página de Triker:', destinos.map((d) => d.url.slice(0, 60))); process.exit(2); }

const ws = new WebSocket(pagina.webSocketDebuggerUrl);
await new Promise((bien, mal) => { ws.onopen = bien; ws.onerror = mal; });
let id = 0;
const enviar = (method, params = {}) => new Promise((bien) => {
  const yo = ++id;
  const alRecibir = (e) => { const m = JSON.parse(e.data); if (m.id === yo) { ws.removeEventListener('message', alRecibir); bien(m); } };
  ws.addEventListener('message', alRecibir);
  ws.send(JSON.stringify({ id: yo, method, params }));
});

if (orden === 'crash') {
  ws.send(JSON.stringify({ id: ++id, method: 'Page.crash' }));
  await new Promise((r) => setTimeout(r, 1500));
  console.log('Page.crash enviado a', pagina.url);
} else {
  const r = await enviar('Runtime.evaluate', { expression: arg, awaitPromise: true, returnByValue: true });
  console.log(JSON.stringify(r.result?.result?.value ?? r.result?.exceptionDetails?.text ?? r));
}
ws.close();
process.exit(0);
