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
| `api` | Creates a game, opens a SignalR connection, adds 2 bots, starts, discards 2 cards, prints the revealed hands + winner, sends a reaction, continues into round 2. Pure backend — no browser. ~10 s. |
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

There are no automated tests in this repository. `driver.mjs api` + `ui` are
the smoke suite.

## Gotchas

- **`<base href="/triker/">` is hardcoded in `Triker.Web/wwwroot/index.html`,
  but the dev server serves from `/`.** Opening `https://localhost:7164` gives
  a blank page and a wall of 404s (`/triker/_framework/...`). `deploy/deploy.sh`
  rewrites the tag to `/` on the server, so nobody notices in production. Fix
  locally with `-- --pathbase=/triker` and browse `https://localhost:7164/triker/`.
  Don't edit `index.html` — it's tracked and the deploy script depends on it.

- **`HandsRevealed` never arrives over REST.** Round results go only to the
  SignalR group named after the gameId (`GameBroadcaster.SendHandsRevealed`),
  which you join by connecting to `/gamehub?gameId=…&playerId=…`. Likewise
  `BroadcastPersonalized` skips players with no `ConnectionId`. A REST-only
  client sees `status: "Revealing"` and nothing else — `players[].play` is
  populated only for *your own* hand and only while status is `Playing`.
  Connect the hub *before* discarding or the reveal is missed entirely.

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
