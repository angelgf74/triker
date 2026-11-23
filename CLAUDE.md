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
PostgreSQL: saves fail and are logged, as in the API without a database). The HTTP/SignalR
wiring and the frontend are covered by driving the real app in headless Chrome:
`.claude/skills/run-triker/e2e/` has one file per behaviour and `todas.mjs` runs them
(see that skill). `e2e/humo.mjs` is the check to run against production after publishing,
and `e2e/politica-de-seguridad.mjs` the one to run before publishing anything that makes
the page load or call something new.

## Architecture

Three-project .NET 10 solution: a Blazor WebAssembly frontend (`Triker.Web`), an ASP.NET Core API with SignalR (`Triker.Api`), and a shared class library (`Triker.Shared`).

**Communication channels:**
- **HTTP REST** (`/api/games`): game creation, joining, turn submission, state queries
- **SignalR WebSocket** (`/gamehub`): real-time state broadcasts pushed to all clients in a game

**Frontend → Backend flow:** `Triker.Web/Services/ApiService` makes HTTP calls; `HubService` manages the SignalR connection and fires events that Blazor components subscribe to. `SessionService` holds the current player/game session.

**Backend state:** The in-memory `GameState` in `Triker.Api` is the source of truth; each game is also mirrored to PostgreSQL as a JSON snapshot (see Key Design Decisions). `GameService` owns the game lifecycle and state-to-DTO conversion; every read or change of a game happens inside `lock (game.SyncLock)` (HTTP requests, timers and the hub arrive on different threads). `ScoringService` evaluates hands and scores. `InactivityService` drives the turn timer — it auto-plays bot moves and broadcasts updates to all connected clients via `GameHub`. Its timers and bot loop run on their own threads, so it never reads or writes `GameState` itself: it asks `GameService` (`TurnOf`, `SetTurnDeadline`, `PassOnTimeout`), which does it under the lock, and asks again after every wait.

**Identity:** player and spectator ids are public (every client sees them in the state). What proves a seat is yours is its secret token, returned only by create/join/spectate (`JoinResponse.PlayerToken`, `SpectateResponse.SpectatorToken`) and never put in a DTO. REST calls send it in the `X-Triker-Token` header (`ApiHeaders.Token`); a wrong or missing token answers 404, same as a missing game. The hub is joined with `GameHub.Entrar(gameId, id, token, espectador)` — the connection URL only carries `?gameId=` — and the client calls it again after every automatic reconnect. The web never gives up reconnecting (`ReintentoSinFin`: a few quick tries, then every 5 s; SignalR's default stops after 42 s and left the player on «Conexión perdida. Recargar»), and it also retries a first connection that fails, which automatic reconnect does not cover. `Entrar` and the lobby's `OnConnectedAsync` answer with the current state, so a reconnected client is up to date without a reload. The web stores seats in `localStorage` (`SessionService`), never in the URL. Bots have no token, so nobody can act for them from outside.

## Domain Model

Cards have three independent attributes, each with 8 possible values:
- **Letter**: T, R, I, K, E, A, G, F
- **Figure**: ●, ■, ★, ▲, ♥, ♣, ♦, ♠
- **Color**: Red, Green, Blue, Cyan, Magenta, Black, Orange, Brown

The number of values per attribute is `GameState.N` (default 8); `DeckService.GenerateDeck(n)` takes the first `n` values of each enum, so the full deck has `N³` cards (512 with N=8). Each turn, players receive 5 cards, discard some, draw replacements. Groups of matching cards score as `(N-1)² × 2^A` where N = group size and A = number of matching attributes.

There are 16 hand ranks (`HandRank` enum, values 0–15), ordered worst to best from "Sin jugada" (no two cards share any attribute) up to "Retriker Fuerte". The round winner is decided **solely by `HandRank` order**, not by raw points — `TotalScore` is not used for ranking between players. So players with the same hand tie, and ties are common: a position is "how many have a better hand, plus one" (1st, 1st, 3rd), both in `HandResultDto.Rank` and in the medals, and several players can win a round (or the game) at once.

## Key Design Decisions

- Game state lives in-process in memory as the source of truth, but each game is also persisted to PostgreSQL as a JSON snapshot (`jsonb`) via EF Core — `GamePersistenceService` serializes `GameState` (enums as strings via `JsonStringEnumConverter`) into the `game_snapshots` table (`AppDbContext`), keyed by game id. Snapshots are written on every state change and reloaded on API startup (`LoadAllAsync` / `EnsureCreatedAsync`), so active games survive restarts.
- Snapshot writes are ordered per game (`GamePersistenceService`): the JSON is serialized while the caller holds the game lock, older versions are skipped, and a deleted game is never written again. On startup `GameService.RestoreGames` brings back every non-expired game, including those between rounds, and `InactivityService.ResumeRestoredGames` re-arms timers and bots. Games saved before tokens existed are discarded: nobody could identify in them.
- While a round is `Finished` its results travel in the state (`GameStateDto.Results`), not only in the `HandsRevealed` event, so a reload on the results screen gets them back. Leaving the table removes the player at once; if fewer than 2 players remain, the table goes back to the waiting room and the first human inherits the creator role.
- Between rounds every human decides whether to go on. `GameState.ContinueDeadline` says until when: `ContinueTimeout` (5 min) from the reveal, cut to `ContinueGrace` (60 s) as soon as one human wants to continue, so an absent player no longer holds the table for 5 minutes. Later decisions never extend it; leaving or a finished game do not shorten it. At the deadline the undecided are removed (`InactivityService.StartFinishTimer` follows the state's deadline and is re-armed whenever it may have changed). It travels in `GameStateDto.ContinueDeadline` and the results screen shows it during the last minute. After a restart it starts over. The web also watches it while an ad is open: the continue is sent 8 s before the deadline even if the ad is still up, since being removed for watching an ad is worse than starting the round behind it.
- A player can leave in any phase (`POST /api/games/{id}/leave`, `GameService.LeaveGame`). In the waiting room or between rounds they are removed at once. Mid-round they are only flagged (`HasLeft`) and stay in `Players` until the reveal: the turn is a counter over that list (`CurrentTurnIndex`), so removing them would change whose turn it is. Their seat stops being valid, their turn is skipped and their hand does not count. A table with no humans left is closed.
- "Jugar contra bots" in the lobby is one request: `CreateGameRequest.Bots` makes `GameService.CreateGame` seat that many bots (clamped to the table size) and deal at once, so the game never shows the waiting room and the creator plays first. The controller then does what `/start` does (turn timer and broadcast).
- Closing the tab tells the API nothing; all it sees is the hub connection dropping (`GameHub.OnDisconnectedAsync` → `GameService.MarkPlayerDisconnected`). `GameState.EmptySince` records since when no human player is connected (not persisted: it restarts with the API). A waiting room in that state stops being listed after `EmptyRoomGrace` (2 min; `GameSummaryDto.Unattended`, which the lobby filters out except for your own room, so invite links still find it) and is closed after `EmptyRoomTtl` (30 min). The long margin is deliberate: someone who shares the invite and locks their phone is also "disconnected". `GameCleanupService` runs every minute and pushes the lobby list when it changes. Games in play are left to the turn and end-of-round timers.
- Input limits live in `Limites` (names 20/30 chars, 500 open games, 40 open games created from one IP, 30 spectators per game, 16 KB bodies) and rate limits in `RateLimits` (per IP and minute: 10 creates, 30 join/spectate, 600 overall). The per-IP cap on open games exists because the rate limit alone let one or two IPs fill the global 500. `GameState.CreatorIp` is memory only (`[JsonIgnore]`, in no DTO), so the count restarts with the API. The IP is the real client's only because `UseForwardedHeaders` trusts the nginx in front (loopback); remove it and every request shares 127.0.0.1's limit.
- `Triker.Shared` contains only DTOs and enums used as the API contract; internal game state (`GameState`, `PlayerState`) lives only in `Triker.Api/State/`.
- `BotService` does not search discards: it evaluates the hand once with the same `ScoringService` as the players and applies its personality. Balanced keeps the cards that are in a group and discards the rest; Conservative passes with a decent hand and otherwise discards at most 2; Aggressive keeps only its strongest group and sometimes redraws everything.
- Bot pacing (`BotPacing`): each bot "thinks" 1.2 s with 0.9 s between bots while up to 3 play in a row — that pause is what lets the player follow them. From 4 on, the whole run gets a total that grows slowly (0.3 s per extra bot), so a full table (15 bots) takes about 10 s instead of 31. The pace is fixed when the run starts, from `TurnInfo.BotsInARow`. The discard request still waits for the bots to finish.
- The Web project has a `PostPublish.ps1` that fingerprints CSS filenames for cache-busting after `dotnet publish`. This runs automatically on publish.
- The published `service-worker.js` is the development one, which caches nothing: the csproj declares no `ServiceWorker` item, so `service-worker.published.js` is shipped but never used. That is fine for a game that needs the API anyway, and it means a deploy reaches every client on reload. Enabling it would need the `index.html` hash fixed after `PostPublish.ps1` and the `srv` base-href step, or its precache fails.
- Colour: the base tokens live in `:root` of `css/app.css` and gold (`--acento`) is the only accent — the main action and what is yours or wins. Secondary controls are neutral and nothing outside the cards is purple (it read as the cards' magenta, an attribute that scores); `e2e/un-solo-acento.mjs` checks it. `--fondo` (`#1a1a2e`) is also the Android shell's background (`android/.../values/colors.xml`), the icon's and the manifest's: changing it needs an app release.
- The table: your zone (`.my-area`) is a felt of its own colour (`--tapete`) that on a phone runs edge to edge, which is what lets the five cards grow. Under your play goes the ladder of the 16 hands (`.escala-jugada`, filled from worst to best; the names of both ends are copied from `ScoringService`, the client has no list). One action button, always primary and always in the same place: «Pasar» with nothing selected, «Descartar N» otherwise. Card hover effects live under `@media (hover: hover)`: on a phone `:hover` sticks to the last thing touched. `e2e/mesa.mjs` checks all of it.
- Results screen: the headline (`.rs-titular`, the screen's `h2`) says how you did («Ganaste la ronda…» / «Ganó X con Y»), with the table name and round above it in small type. Places are numbers («1º»), never the 🥇🥈🥉 emojis in the rows. Gold says two separate things there: the ring is your row (`.my-result`), the number is who wins (`.rank-1 .rank-medal`). `e2e/resultados-de-un-vistazo.mjs` checks that they agree with each other.
- Lobby: under the logo, a decorative fan of five cards on the felt colour (`.lobby-abanico`, real `CardView`s spelling T-R-I-K-E, `aria-hidden` and untouchable) shows a newcomer what a Triker card is. It must not push «Jugar contra bots» off the first screen of a phone; `e2e/lobby.mjs` measures it.
- Typeface: Nunito Sans for the whole app, self-hosted as one variable file (`wwwroot/fonts/nunito-sans-latin.woff2`, weights 400–900, Latin subset, 31 KB; licence in `fonts/NOTICE.txt`). Never from Google Fonts: the published CSP only allows `font-src 'self'`. `index.html` preloads it so the text does not change face mid-load. What is not in the subset (the card figures) still comes from the system font. `e2e/fuente-propia.mjs` checks it. `privacidad.html` is fixed HTML that does not load `app.css`: it repeats the `@font-face` and the base colours in its own `<style>`, and its own CSP needs `font-src 'self'` for it — change them there too when they change in `app.css` (the same test compares both). `PostPublish.ps1` must not fingerprint fonts: it only rewrites `index.html`, so the preload would ask for one URL and the CSS for another and the font would download twice — it happened on the first publish and only shows in the published package, where `e2e/politica-de-seguridad.mjs` now counts the font requests.
- The shared image of a hand (`wwwroot/js/cardImage.js`, drawn on a `<canvas>`) follows the app's look: flat `--fondo`, cards on the `--tapete`, the logo drawn letter by letter with its mirrored K, Nunito Sans, and the same group marks as the results. `render` is async because a canvas does not wait for a web font. No emojis in it: a canvas paints them with the system's. Bump its `?v=` in `index.html` when it changes. `e2e/imagen-de-compartir.mjs` generates one and reads its pixels. Neither the image nor the share text mention points (they decide nothing): both say «jugada nº 5 de 16» (`ResultsView.EnLaEscala`) — «jugada nº», not «puesto», which next to a name reads as the player's place.
- Rivals (`OtherPlayersView`) have two layouts from the same markup. As seats (`Asientos`, up to `MaxAsientos` = 8 rivals): above your zone, each with an avatar (bots get a robot instead of the «Bot» tag), one row up to 4 and two up to 8. With 9 or more they do not fit without pushing the play button off a 360×740 phone, so they stay as the list below your zone. The history and session panels (`.info-row`) go under the table for the same reason. A rival who has not played yet says «espera» (it used to say «pensando…» for everybody, also on your turn). `e2e/asientos.mjs` measures all of it.
- Interface icons are the `Icono` component (`<Icono Nombre="candado" />`, inline line drawings that take the colour and size of the text around them), not emojis: the system paints an emoji, so it changes between phones and ignores the text colour. A button that only holds an icon needs its own `color` (the browser default is black). Medals are the `Medalla` component (`<Medalla Puesto="1" />`): a gold, silver or bronze disc with its number inside, because colour alone did not tell them apart; it reads as «Primeros puestos» unless `Etiqueta` says otherwise. Places in a list are plain numbers («1º»). Emojis stay only where they are content: reactions and the share/invite texts. `e2e/iconos.mjs` scans the `.razor` sources (comments included) and the painted screens.
- Text contrast: a grey that passes on the page background does not on a panel (`#888` gives 4.9:1 on `--fondo`, 4:1 on `--panel`), so secondary text uses `--texto-suave`, and `#d5dee8` on the felt. `e2e/contraste-del-texto.mjs` measures every painted text against what is really behind it, a gradient by each of its colours and keeping the worst: the main screens, the help and suggestion dialogs, what a spectator sees, the table's three top banners (drawn with `GameRoom.razor`'s markup), both invites, the lobby notices and the failure screens (game gone, no seat, page not found, API unreachable — cut in the test's browser only, so it still runs against production). Cards are excluded, the help's example ones too: their colour is a game attribute. The plain greys left in the CSS pass where they are painted (`#aaa` also on a panel; `#888` only on `--fondo`, except the caret of a folded hand in the results: a sign, not text, where 3:1 is enough and it gives 4:1). Not in its route: the champion banner, the private room's code and «Volver a tu partida».
- Touch size: everything you tap measures at least 44 px, a finger's width — buttons, fields and the label of a checkbox (`min-height: 44px`; padding and font alone left the buttons at 41 px and «Compartir» at 29). A small button inside a row that must not grow (refresh in the lobby, close in the history panel, the two in the top banners) gets the 44 px and a negative margin that takes back the excess; «Recargar» draws its outline on a `::before` so the box can be 44 px without the banner covering more of the header. `e2e/diseno-movil.mjs` measures every control (`controlesCortos`) in the lobby, both dialogs, the create form, the waiting room, the results and round 2, and `e2e/cuenta-atras-y-turno-agotado.mjs` the timeout banner. The play chip that unfolds its groups (`PlayBadge`) is a `<button>` with `aria-expanded` only when there is something to unfold, built the same way (44 px box, the 26 px pill on a `::before`, the focus ring on the pill); `e2e/desplegar-la-jugada.mjs` checks it. `controlesCortos` only sees real controls, so anything tappable is a `<button>`, a link or a `role="button"` (the cards), never a bare `<span>` with `@onclick`.
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
- The published `index.html` carries a Content-Security-Policy that the dev one does not:
  `deploy/csp.mjs` (second `pasos` step) writes it as a `<meta>` once the package is built,
  because Blazor's inline `<script type="importmap">` changes on every build and needs its hash.
  It reads the API from `appsettings.Production.json` and the allowed third-party script origins
  from `index.html` itself, and refuses to publish if it finds an inline script or handler: add
  JS as a file under `wwwroot/js/` (`js/arranque.js` runs before Blazor). The suggestions
  mailbox origin is a constant there and must match `SugerenciasModal.razor`. Anything new the
  page loads or calls from another origin must be added to that policy, or it fails silently in
  production only.
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
- App Links: an invite link (`https://triker-app.angelgf.com.es/?join=…`) opens the app when it is
  installed. The manifest claims only the root path (so `privacidad.html` stays in the browser)
  and Android verifies it against `Triker.Web/wwwroot/.well-known/assetlinks.json`, which lists
  the SHA-256 of Play's app signing key (Play Console → Firma de aplicaciones) and of the upload
  key (`playstore/gplay.json`), so a locally signed release verifies too. `MainActivity` is
  `singleTask`: with the app open the link arrives in `onNewIntent`. Debug builds
  (`.debug` package) are not in that file and never verify; test their handling with
  `adb shell am start -a android.intent.action.VIEW -d <link> com.angelgf.triker.debug`.
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
