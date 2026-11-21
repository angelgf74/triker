# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Run API (https://localhost:7077)
dotnet run --project Triker.Api --launch-profile https

# Run Web frontend (https://localhost:7164)
dotnet run --project Triker.Web --launch-profile https

# Build entire solution
dotnet build

# Run the test suite
dotnet test Triker.Tests
```

Tests (`Triker.Tests`, xUnit) cover the game rules: `ScoringService` (the 16 hand
ranks, the group scoring formula, and the fact that rank — not points — decides
the winner), `BotService` (roster integrity, discard validity) and `DeckService`.
They build hands from the rule definitions rather than from the implementation,
so they fail when the rules change. `GameServiceTests` and `LimitesTests` cover
the game lifecycle: seat tokens, results in the state, leaving the table,
concurrent requests, restore after a restart and input limits (they run without
PostgreSQL: saves fail and are logged, as in the API without a database). There
is no coverage of the HTTP/SignalR wiring or the frontend; for those,
`.claude/skills/run-triker` drives the real app.

## Architecture

Three-project .NET 10 solution: a Blazor WebAssembly frontend (`Triker.Web`), an ASP.NET Core API with SignalR (`Triker.Api`), and a shared class library (`Triker.Shared`).

**Communication channels:**
- **HTTP REST** (`/api/games`): game creation, joining, turn submission, state queries
- **SignalR WebSocket** (`/gamehub`): real-time state broadcasts pushed to all clients in a game

**Frontend → Backend flow:** `Triker.Web/Services/ApiService` makes HTTP calls; `HubService` manages the SignalR connection and fires events that Blazor components subscribe to. `SessionService` holds the current player/game session.

**Backend state:** The in-memory `GameState` in `Triker.Api` is the source of truth; each game is also mirrored to PostgreSQL as a JSON snapshot (see Key Design Decisions). `GameService` owns the game lifecycle and state-to-DTO conversion; every read or change of a game happens inside `lock (game.SyncLock)` (HTTP requests, timers and the hub arrive on different threads). `ScoringService` evaluates hands and scores. `InactivityService` drives the turn timer — it auto-plays bot moves and broadcasts updates to all connected clients via `GameHub`.

**Identity:** player and spectator ids are public (every client sees them in the state). What proves a seat is yours is its secret token, returned only by create/join/spectate (`JoinResponse.PlayerToken`, `SpectateResponse.SpectatorToken`) and never put in a DTO. REST calls send it in the `X-Triker-Token` header (`ApiHeaders.Token`); a wrong or missing token answers 404, same as a missing game. The hub is joined with `GameHub.Entrar(gameId, id, token, espectador)` — the connection URL only carries `?gameId=` — and the client calls it again after every automatic reconnect. The web stores seats in `localStorage` (`SessionService`), never in the URL. Bots have no token, so nobody can act for them from outside.

## Domain Model

Cards have three independent attributes, each with 8 possible values:
- **Letter**: T, R, I, K, E, A, G, F
- **Figure**: ●, ■, ★, ▲, ♥, ♣, ♦, ♠
- **Color**: Red, Green, Blue, Cyan, Magenta, Black, Orange, Brown

The number of values per attribute is `GameState.N` (default 8); `DeckService.GenerateDeck(n)` takes the first `n` values of each enum, so the full deck has `N³` cards (512 with N=8). Each turn, players receive 5 cards, discard some, draw replacements. Groups of matching cards score as `(N-1)² × 2^A` where N = group size and A = number of matching attributes.

There are 16 hand ranks (`HandRank` enum, values 0–15), ordered worst to best from "Sin jugada" (no two cards share any attribute) up to "Retriker Fuerte". The round winner is decided **solely by `HandRank` order**, not by raw points — `TotalScore` is not used for ranking between players.

## Key Design Decisions

- Game state lives in-process in memory as the source of truth, but each game is also persisted to PostgreSQL as a JSON snapshot (`jsonb`) via EF Core — `GamePersistenceService` serializes `GameState` (enums as strings via `JsonStringEnumConverter`) into the `game_snapshots` table (`AppDbContext`), keyed by game id. Snapshots are written on every state change and reloaded on API startup (`LoadAllAsync` / `EnsureCreatedAsync`), so active games survive restarts.
- Snapshot writes are ordered per game (`GamePersistenceService`): the JSON is serialized while the caller holds the game lock, older versions are skipped, and a deleted game is never written again. On startup `GameService.RestoreGames` brings back every non-expired game, including those between rounds, and `InactivityService.ResumeRestoredGames` re-arms timers and bots. Games saved before tokens existed are discarded: nobody could identify in them.
- While a round is `Finished` its results travel in the state (`GameStateDto.Results`), not only in the `HandsRevealed` event, so a reload on the results screen gets them back. Leaving the table removes the player at once; if fewer than 2 players remain, the table goes back to the waiting room and the first human inherits the creator role.
- Between rounds every human decides whether to go on. `GameState.ContinueDeadline` says until when: `ContinueTimeout` (5 min) from the reveal, cut to `ContinueGrace` (60 s) as soon as one human wants to continue, so an absent player no longer holds the table for 5 minutes. Later decisions never extend it; leaving or a finished game do not shorten it. At the deadline the undecided are removed (`InactivityService.StartFinishTimer` follows the state's deadline and is re-armed whenever it may have changed). It travels in `GameStateDto.ContinueDeadline` and the results screen shows it during the last minute. After a restart it starts over. The web also watches it while an ad is open: the continue is sent 8 s before the deadline even if the ad is still up, since being removed for watching an ad is worse than starting the round behind it.
- A player can leave in any phase (`POST /api/games/{id}/leave`, `GameService.LeaveGame`). In the waiting room or between rounds they are removed at once. Mid-round they are only flagged (`HasLeft`) and stay in `Players` until the reveal: the turn is a counter over that list (`CurrentTurnIndex`), so removing them would change whose turn it is. Their seat stops being valid, their turn is skipped and their hand does not count. A table with no humans left is closed.
- "Jugar contra bots" in the lobby is one request: `CreateGameRequest.Bots` makes `GameService.CreateGame` seat that many bots (clamped to the table size) and deal at once, so the game never shows the waiting room and the creator plays first. The controller then does what `/start` does (turn timer and broadcast).
- Closing the tab tells the API nothing; all it sees is the hub connection dropping (`GameHub.OnDisconnectedAsync` → `GameService.MarkPlayerDisconnected`). `GameState.EmptySince` records since when no human player is connected (not persisted: it restarts with the API). A waiting room in that state stops being listed after `EmptyRoomGrace` (2 min; `GameSummaryDto.Unattended`, which the lobby filters out except for your own room, so invite links still find it) and is closed after `EmptyRoomTtl` (30 min). The long margin is deliberate: someone who shares the invite and locks their phone is also "disconnected". `GameCleanupService` runs every minute and pushes the lobby list when it changes. Games in play are left to the turn and end-of-round timers.
- Input limits live in `Limites` (names 20/30 chars, 500 open games, 30 spectators per game, 16 KB bodies) and rate limits in `RateLimits` (per IP and minute: 10 creates, 30 join/spectate, 600 overall). The IP is the real client's only because `UseForwardedHeaders` trusts the nginx in front (loopback); remove it and every request shares 127.0.0.1's limit.
- `Triker.Shared` contains only DTOs and enums used as the API contract; internal game state (`GameState`, `PlayerState`) lives only in `Triker.Api/State/`.
- `BotService` scores each possible discard combination and picks the best move; it uses the same `ScoringService` as player evaluation.
- The Web project has a `PostPublish.ps1` that fingerprints CSS filenames for cache-busting after `dotnet publish`. This runs automatically on publish.
- The published `service-worker.js` is the development one, which caches nothing: the csproj declares no `ServiceWorker` item, so `service-worker.published.js` is shipped but never used. That is fine for a game that needs the API anyway, and it means a deploy reaches every client on reload. Enabling it would need the `index.html` hash fixed after `PostPublish.ps1` and the `srv` base-href step, or its precache fails.
- The Web `wwwroot/appsettings.json` sets `ApiBaseUrl`; `appsettings.Production.json` overrides this for deployment.

## Deployment

With `srv` (skill `servidor`), from the repo root: `srv.json` publishes the Blazor web at
`triker-app.angelgf.com.es` and the API at `triker-api.angelgf.com.es` (health `/salud`, which
reads `game_snapshots`: 200, or 503 without the database), running as `webapps` with the
`triker` database. There are no secrets: the connection string (Unix socket, peer auth as
`webapps`) lives in `Triker.Api/appsettings.json`. There are no EF migrations either: the API
creates its table on startup (`EnsureCreated`).

    srv estado                    # what is published, service and health
    srv subir                     # dry run; srv subir --publicar to publish
    srv volver                    # back to the previous release (dry run first)
    srv logs api

- `<base href="/triker/">` stays in `index.html` for development; `deploy/base-en-la-raiz.mjs`
  (a `pasos` step in `srv.json`) rewrites it to `/` in the package, and fails if it can't find it.
- Don't put `Urls` in `appsettings*.json`: it overrides the `ASPNETCORE_URLS` that srv sets.
- The API is built with `InvariantGlobalization` (the pre-srv unit set
  `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`).

## Android app

`android/` is a single-activity Kotlin shell (`MainActivity`) around a WebView that loads the
published web; game changes reach it through `srv`, without a new app version.

- Ads: nothing from AdMob runs until `AdsConsent` (Google UMP) says ads can be requested. The
  consent form is the one configured in the AdMob console for the app; where it is mandatory the
  web shows «Privacidad de los anuncios» in the lobby (`js/ads.js`, `window.AndroidAds`).
- The interstitial is asked for when the player decides on the results screen, never at the
  reveal. On «Siguiente ronda» the web waits for the ad to close before sending the continue
  (`TrikerAds.interstitialAndWait`), or the turn clock would run behind the ad. It needs no
  callback from the app: the ad opens in its own activity, so the page gets `visibilitychange`
  (hidden about 50 ms after the request, visible on close).
- AdMob ids are per build type (`src/debug` has Google's test ids, `src/release` the real ones)
  and debug builds install as `com.angelgf.triker.debug`, next to the Play one.
- The debug WebView accepts remote debugging: `adb forward tcp:9333
  localabstract:webview_devtools_remote_<pid>` and drive it over CDP. That is how offline start,
  renderer crashes and the consent form were checked on the emulator.
- Native fallbacks: an error screen with «Reintentar» when the page cannot load, and
  `onRenderProcessGone` replaces a dead WebView instead of letting the app close.

## Publishing to Google Play

Same flow as every Android project, with `gplay` (skill `google-play`), from `android/`:

    gplay version --menor         # or --parche, --mayor, x.y.z
    gplay compilar                # signed and verified AAB in android/release/
    gplay subir --canal internal  # dry run; add --publicar to publish

`gplay compilar` builds the release unsigned and signs it: the keystore lives in
`~/.google-play/keystores/` and its password in the Windows Credential Manager. There are no
passwords or signing scripts in the repo. Config in `android/playstore/gplay.json`, release
notes in `android/playstore/listing/<lang>/changelogs/<versionCode>.txt`, store listing in
`android/playstore/listing/` (`gplay ficha descargar` / `publicar`) and its source material in
`android/playstore/fuentes/`. versionCode = MAJOR*10000 + MINOR*100 + PATCH (1.4.0 -> 10400).
