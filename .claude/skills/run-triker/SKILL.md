---
name: run-triker
description: Build, run, and drive Triker (Blazor WASM + ASP.NET Core SignalR card game). Use when asked to start Triker, run the API or Web frontend, play a game end to end, take a screenshot of the UI, verify a change in the real app, or run the hand simulator.
---

Triker is a .NET 10 solution: `Triker.Api` (REST + SignalR) and `Triker.Web`
(Blazor WebAssembly). Start both dev servers, then drive them with
`.claude/skills/run-triker/driver.mjs` — a Node harness that plays a full
round over HTTP+SignalR (`api`) and clicks through the real UI in headless
Chrome via Playwright (`ui`, writes screenshots).

All paths below are relative to the repo root (`C:\Desarrollo\Proyectos\Triker`).
Commands are PowerShell (the primary shell here); Bash-specific caveats are
called out in Gotchas.

## Prerequisites

Already present on this machine — verified:

```powershell
dotnet --version   # 10.0.301
node --version     # v24.18.0
```

Google Chrome must be installed (`C:\Program Files\Google\Chrome\Application\chrome.exe`).
The driver launches it with Playwright's `channel: 'chrome'`, so no Playwright
browser download is needed.

PostgreSQL is **not** required to run locally — see Gotchas.

## Setup

One-time, installs the driver's own deps (`playwright`, `@microsoft/signalr`)
into `.claude/skills/run-triker/node_modules`, which is gitignored:

```powershell
npm install --prefix .claude/skills/run-triker
```

## Build

```powershell
dotnet build
```

Succeeds with 2 `NU1903` warnings about `Microsoft.OpenApi` 2.0.0 — expected,
not a failure.

## Run (agent path)

### 1. Start both servers (two background shells)

```powershell
dotnet run --project Triker.Api --launch-profile https
```

```powershell
dotnet run --project Triker.Web --no-launch-profile --urls "https://localhost:7164;http://localhost:5065" -- --pathbase=/triker
```

Wait for `Application started.` in both (~10 s each). Verify the frontend is
actually serving its assets — a 200 here is the real readiness check:

```powershell
curl.exe -sk -o NUL -w "fw=%{http_code}`n" https://localhost:7164/triker/_framework/blazor.webassembly.js
```

The API logs a long `NpgsqlException` stack trace at startup and then keeps
going. That is normal (see Gotchas).

### 2. Drive it

```powershell
node .claude/skills/run-triker/driver.mjs api
node .claude/skills/run-triker/driver.mjs ui
node .claude/skills/run-triker/driver.mjs shot . lobby-check
```

| command | what it does |
|---|---|
| `api` | Creates a game, opens a SignalR connection and calls `Entrar`, adds 2 bots, starts, discards 2 cards, prints the revealed hands + winner, checks the results also come in the state, sends a reaction, continues into round 2. Pure backend — no browser. ~10 s. |
| `ui` | Playwright + headless Chrome: lobby → create game → add 2 bots → start → select 2 cards → discard → results screen → round 2. Six screenshots. ~30 s. |
| `shot <route> [name]` | Screenshot any route. Route is relative to `/triker/`, takes **no leading slash**, and is `.` for the lobby (`shot "game/<id>?playerId=<pid>" mygame`). |

Screenshots land in `.claude/skills/run-triker/shots/` (gitignored):
`01-lobby`, `02-waiting-room`, `03-hand-dealt`, `04-cards-selected`,
`05-results`, `06-round-2`. **Open them** — a passing exit code only proves
the selectors matched.

`api` and `ui` exit non-zero with `FAILED: <reason>` on any assertion miss.

Override the endpoints with `TRIKER_API` / `TRIKER_WEB` if you moved ports;
`TRIKER_WEB` must include the `/triker` path segment.

### 3. Stop

```powershell
Get-NetTCPConnection -LocalPort 7077,7164 -State Listen -ErrorAction SilentlyContinue |
  ForEach-Object { Stop-Process -Id $_.OwningProcess -Force }
```

## Direct invocation

Scoring changes (`ScoringService`) show up without a browser: the API's
`GET /api/games/{id}?playerId={pid}` returns `players[me].play` — the live
`ScoringService.EvaluateHand` output for your own hand while status is
`Playing`. The `api` driver prints it as `my current play: …`.

Hand-frequency simulation across deck sizes n=5..8 (its own copy of the
evaluator, not `ScoringService`). It prompts for an iteration count on stdin —
pipe it from **Bash**, not PowerShell (see Gotchas):

```bash
echo 20000 | dotnet run --project Triker.Simulator -c Release
```

Takes a couple of minutes for 20 000 hands and writes
`Triker_Simulacion_n5-8_iter<N>_<timestamp>.xlsx` **into the repo root** —
delete it afterwards (`rm -f Triker_Simulacion_*.xlsx`).

## Run (human path)

```powershell
dotnet run --project Triker.Api --launch-profile https   # https://localhost:7077
dotnet run --project Triker.Web --launch-profile https   # opens a browser tab
```

The Web `https`/`http` launch profiles have `launchBrowser: true`, so each run
pops a real Chrome window at `https://localhost:7164` — which **404s all its
assets** (see Gotchas). Use the agent-path command instead.

## Test

`dotnet test Triker.Tests` covers the rules and the game lifecycle in
`GameService` (tokens, results in state, leaving the table, concurrency,
restore after restart). The HTTP/SignalR wiring and the frontend are covered by
the real app: `driver.mjs api` + `ui` for a quick look, and `e2e/` for behaviour.

### e2e/: the real app, one behaviour per file

Each file in `.claude/skills/run-triker/e2e/` drives the running app in headless
Chrome at 360 px, es-ES, prints `OK`/`FALLO` per check and ends with
`== TODO OK ==` (exit code 1 otherwise). They share `e2e/lib/comun.mjs`.

```powershell
node .claude/skills/run-triker/e2e/todas.mjs --rapidas   # the quick ones, ~6 min
node .claude/skills/run-triker/e2e/todas.mjs             # all of them, ~25 min
node .claude/skills/run-triker/e2e/todas.mjs empates reconexion
node .claude/skills/run-triker/e2e/empates.mjs           # or one directly
```

`todas.mjs` needs both dev servers up and waits between tests so the API's
10-creates-per-minute limit is never hit.

| file | what it pins down |
|---|---|
| `humo` | one bots game through the UI, table closed on leaving. **The check to run against production right after `srv subir --publicar`.** |
| `jugar-contra-bots` | «Jugar contra bots»: one tap, one create request, no waiting room |
| `salir-de-la-mesa`, `volver-a-tu-partida` | leaving in every phase; the lobby taking you back to your seat |
| `salas-sin-nadie` (4 min) | waiting rooms nobody is connected to stop being listed |
| `cuenta-atras-y-turno-agotado` (3 min) | countdown bar in es-ES, timeout banner surviving the reveal, its close button at 44 px |
| `plazo-entre-rondas` (3 min) | the 60 s to decide once someone wants to go on; an ad must not get you removed |
| `reconexion` (3 min) | table and lobby come back alone after a 50 s network cut, and after a failed first connect |
| `anuncio-entre-rondas`, `privacidad-de-anuncios` | interstitial timing and the privacy link, with a fake `window.AndroidAds` |
| `empates` | shared first place in the results summary |
| `sonido-descarte-y-ayuda` | champion sound, rejected discard message, help wording |
| `diseno-movil`, `resultados-y-cabecera` | layout measurements at 360 px, every control at 44 px or more (`controlesCortos` in `lib/comun.mjs`: buttons, fields, checkbox labels) in the lobby, both dialogs, the create form, the waiting room, the results and round 2; results folded on big tables |
| `un-solo-acento` | colour base: gold is the only accent, nothing purple outside the cards, soft text contrast |
| `contraste-del-texto` | every painted text reaches WCAG contrast (4.5:1, 3:1 if large) against the background actually behind it, worst stop of a gradient included, screen by screen: help and suggestion dialogs, a spectator's table and results, the table's three top banners, both invites, the lobby notices and the failure screens (game gone, no seat, page not found, API unreachable, cut in that browser only); cards, the help's example cards, emojis and disabled controls excluded |
| `iconos` | interface icons are line drawings (`Icono`), not emojis: source scan plus what is painted on each screen |
| `mesa` | your turn at 360 px: felt-coloured zone edge to edge, cards at least 62 px wide, the 16-step ladder, one action button that does not move |
| `asientos` | rivals as seats above your zone up to 8 (one or two rows), as a list below it from 9; history and session panels under the table; the play button stays on the first screen at 360x740 |
| `resultados-de-un-vistazo` | results headline says how you did, places are numbers, gold ring only on your row and gold number only on the winners |
| `desplegar-la-jugada` | the play chip that unfolds its groups is a button: 44 px for the finger with the visible pill and its row unchanged, covers no other control, `aria-expanded`, opens and closes with Enter and Space (full table, 15 bots, so some hand has two groups) |
| `lobby` | lobby at 360 px: the decorative fan of cards, name label above its field, game rows as panels, and «Jugar contra bots» still on the first screen |
| `imagen-de-compartir` | the shared image of a hand: generated for real and read pixel by pixel (flat background, felt behind the cards, group bars, no emojis) |
| `nombres-invisibles`, `ritmo-de-los-bots` | API only: invisible characters in names; bot pacing timings |
| `fuente-propia`, `vista-previa-de-enlaces`, `sin-peticiones-fallidas` | no Google Fonts request; Open Graph tags; no 4xx from the site itself |
| `politica-de-seguridad` (2 min) | the published package under its CSP: full walk-through with zero violations, and what it must block |

Not in `todas.mjs`, run by hand: `tope-por-ip` (creates 40 games in 6 min and
leaves the API full: restart it afterwards) and `que-carga-la-web` (not a test:
the inventory of what the page loads, for updating `deploy/csp.mjs`).

**Against production.** Set both variables; only `humo`, `diseno-movil`,
`contraste-del-texto`, `resultados-y-cabecera`, `desplegar-la-jugada`, `fuente-propia`, `vista-previa-de-enlaces`,
`nombres-invisibles`, `ritmo-de-los-bots` and `que-carga-la-web` accept it (the
rest call `soloLocal()` and exit 2). Every one creates public test tables for a
few seconds and leaves them.

```powershell
$env:TRIKER_WEB='https://triker-app.angelgf.com.es'; $env:TRIKER_API='https://triker-api.angelgf.com.es'
node .claude/skills/run-triker/e2e/humo.mjs
```

Things these tests learned the hard way (the helpers already handle them):

- **Leave the table and wait for the lobby before closing the browser.** Closing
  first aborts the leave request and the test table stays open until its timers
  close it (minutes).
- **`context.setOffline` does not cut an open WebSocket.** `lib/corte.mjs` routes
  the hub through Playwright so it can really close it. And the hub connects
  *after* the table is painted: cut right when the turn appears and you are
  testing a failed first connect, not a reconnect.
- **States you cannot produce by playing** (a tie, a 16-player table, a game
  ending in one round) are forced by rewriting the request or the REST state on
  the way (`page.route`), with the hub cut so the real state does not overwrite
  it. That tests the real rendering, not the server's maths; the maths has unit
  tests.
- **The CSP only exists in the published package.** `lib/montar-paquete.mjs`
  builds it the way `srv subir` does and `lib/produccion-local.mjs` serves it
  while the browser believes it is on the production origin, with the API
  diverted to the local one. Nothing reaches the Internet.
- **Code run straight from `page.evaluate` is exempt from the CSP** (the browser
  treats it as the devtools console): `eval` looks allowed. Ask for it from page
  code, inside a `setTimeout`.
- **Headless Chrome has `navigator.share` and cancels it**: sharing returns
  `cancelled` and shows nothing. Test sharing with a fake `window.AndroidShare`.
- **Sounds, ads and the Android bridges are observed, not heard or seen**: the
  tests record which `trikerAudio` function or bridge method the app calls.

### android/: the app on the emulator

`android/emulador.sh` (source it from Git Bash) reads what is on screen and taps by
text over adb; `android/cdp.mjs` evaluates JS in the debug app's WebView;
`android/invitacion.mjs` opens a real invite link in the app (App Links). Their
headers say how. The debug app loads the **production** web, so a web change can
only be checked in the app after publishing it.

## Gotchas

- **`<base href="/triker/">` is hardcoded in `Triker.Web/wwwroot/index.html`,
  but the dev server serves from `/`.** Opening `https://localhost:7164` gives
  a blank page and a wall of 404s (`/triker/_framework/...`). When publishing,
  `srv` runs `deploy/base-en-la-raiz.mjs`, which rewrites the tag to `/` in the
  package, so nobody notices in production. Fix locally with
  `-- --pathbase=/triker` and browse `https://localhost:7164/triker/`.
  Don't edit `index.html`'s base — that step looks for exactly `<base href="/triker/" />`.

- **Every seat needs its token.** Create/join return `playerId` + `playerToken`
  (spectate: `spectatorId` + `spectatorToken`). Per-seat REST calls send it in
  the `X-Triker-Token` header; a missing or wrong token answers **404**, same
  as a game that does not exist. The web keeps seats in `localStorage`
  (`triker_asientos`), so `driver.mjs shot "game/<id>?playerId=<pid>"` from a
  fresh browser shows "No formas parte de esta partida" — drive the game
  through the UI (`ui`) instead.

- **The hub needs `Entrar`, not a URL.** Connect to `/gamehub?gameId=…` and
  invoke `Entrar(gameId, id, token, espectador)`; only then does the
  connection join the game group and get a `ConnectionId`. Round results
  (`HandsRevealed`) go only to that group, and `BroadcastPersonalized` skips
  players with no `ConnectionId`. After an automatic reconnect the client must
  call `Entrar` again. Connect *before* discarding or the reveal event is
  missed — although while the round is `Finished` the results also come in
  `GET /api/games/{id}` (`results`).

- **Rate limits apply to you too.** Per IP and minute: 10 creates, 30
  join/spectate, 600 requests overall (`Triker.Api/RateLimits.cs`). Running
  `ui`, `api` and ad-hoc scripts back to back can earn a 429 on create; wait a
  minute.

- **`HandResultDto` has three rank-ish fields.** `rank` is the 1-based
  placement (winner = 1), `handRank` is the `HandRank` enum as a string,
  `handName` is the Spanish label. There is no `isWinner`.

- **Don't wait on `.my-play-badge` to detect the results screen.** That badge
  is visible for the whole `Playing` phase, so it matches seconds before the
  round ends. Wait for `.results-view .result-entry` instead.

- **Cards are dealt with a staggered CSS animation, so a screenshot taken as
  soon as the DOM has 5 `.card` nodes shows one card and four invisible ones.**
  `HandView` removes the `dealing` class ~0.15 s after the last card lands;
  the driver's `waitDealt()` waits for `.hand-cards .card.dealing` to hit zero.
  Same trap for the `Ronda 2` badge — it flips before the redeal finishes.

- **Bots auto-play inside the request.** `InactivityService.ProcessBotsAndBroadcast`
  sleeps ~1–3 s per bot before `POST /start` and `POST /discard` return, so by
  the time those calls resolve it is usually already your turn. Turn timeout is
  60 s; if you idle past it the bot plays your hand for you.

- **The ASP.NET dev cert is valid but untrusted here.** Kestrel logs
  `The ASP.NET Core developer certificate is not trusted`. The driver works
  around it with `ignoreHTTPSErrors` (Playwright) and
  `NODE_TLS_REJECT_UNAUTHORIZED=0` (fetch + SignalR ws). Service-worker
  registration still fails — Chrome fetches `service-worker.js` outside the
  context's TLS exemption. Harmless; it's the one `[pageerror]` the `ui` run
  always prints.

- **Git Bash mangles `/`-leading arguments.** `--pathbase=/triker` becomes
  `--pathbase=C:/Program Files/Git/triker`, and the dev server dies with
  `The path in 'value' must start with '/'`. Prefix with `MSYS_NO_PATHCONV=1`
  if you must launch from Bash. Same reason `driver.mjs shot` takes routes
  without a leading slash — and why the lobby route is `.` rather than `""`:
  PowerShell 5.1 silently drops an empty quoted argument to a native exe, so
  `shot "" lobby-check` shifts `lobby-check` into the route slot and screenshots
  the NotFound page.

- **Piping stdin to `Triker.Simulator` from PowerShell doesn't work.** Both
  `echo 20000 |` and `"20000" |` produce `Introduce un número entero entre 1 y
  1000000000.` then `No hay entrada disponible. Saliendo.` — PowerShell 5.1's
  native-command pipe doesn't hand the app a plain line it can `int.Parse`.
  Run that one command from the Bash tool.

- **Web `appsettings.json` decides where the frontend calls.** It ships
  `https://localhost:7077`; `appsettings.Production.json` points at the live
  API. The API's CORS allowlist (`Triker.Api/appsettings.json` →
  `AllowedOrigins`) only contains `https://localhost:7164` and
  `http://localhost:5065`, so serving the frontend on any other port breaks
  every request.

## Troubleshooting

- **`waiting for locator('.game-title') to be visible` + repeated 404s and a
  service-worker error**: the dev server was started without `--pathbase=/triker`.
  Restart it with the agent-path command.

- **`Failed to bind to address https://localhost:7164: address already in use`**:
  a previous `dotnet run` survived. Kill it with the Stop snippet above, then
  retry.

- **`Unhandled exception. System.ArgumentException: The path in 'value' must
  start with '/'` from `DevServer.Server.Startup.EnableConfiguredPathbase`**:
  Git Bash path conversion (see Gotchas). Use PowerShell, or
  `MSYS_NO_PATHCONV=1`.

- **API startup dumps `Npgsql.NpgsqlException … Failed to connect to
  /var/run/postgresql`**: expected on Windows. `Program.cs` catches it, logs
  `La API arranca sin persistencia`, and runs with in-memory state only. Games
  simply don't survive an API restart.

- **`FAILED: timed out waiting for HandsRevealed over SignalR`**: another
  player (or bot) hasn't discarded. Check the API log — if a bot threw, the
  round stays in `Playing` forever.
