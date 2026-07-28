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
so they fail when the rules change. There is no coverage of the HTTP/SignalR
layer or the frontend; for those, `.claude/skills/run-triker` drives the real app.

## Architecture

Three-project .NET 10 solution: a Blazor WebAssembly frontend (`Triker.Web`), an ASP.NET Core API with SignalR (`Triker.Api`), and a shared class library (`Triker.Shared`).

**Communication channels:**
- **HTTP REST** (`/api/games`): game creation, joining, turn submission, state queries
- **SignalR WebSocket** (`/gamehub`): real-time state broadcasts pushed to all clients in a game

**Frontend → Backend flow:** `Triker.Web/Services/ApiService` makes HTTP calls; `HubService` manages the SignalR connection and fires events that Blazor components subscribe to. `SessionService` holds the current player/game session.

**Backend state:** The in-memory `GameState` in `Triker.Api` is the source of truth; each game is also mirrored to PostgreSQL as a JSON snapshot (see Key Design Decisions). `GameService` owns the game lifecycle and state-to-DTO conversion. `ScoringService` evaluates hands and scores. `InactivityService` drives the turn timer — it auto-plays bot moves and broadcasts updates to all connected clients via `GameHub`.

## Domain Model

Cards have three independent attributes, each with 8 possible values:
- **Letter**: T, R, I, K, E, A, G, F
- **Figure**: ●, ■, ★, ▲, ♥, ♣, ♦, ♠
- **Color**: Red, Green, Blue, Cyan, Magenta, Black, Orange, Brown

The number of values per attribute is `GameState.N` (default 8); `DeckService.GenerateDeck(n)` takes the first `n` values of each enum, so the full deck has `N³` cards (512 with N=8). Each turn, players receive 5 cards, discard some, draw replacements. Groups of matching cards score as `(N-1)² × 2^A` where N = group size and A = number of matching attributes.

There are 16 hand ranks (`HandRank` enum, values 0–15), ordered worst to best from "Sin jugada" (no two cards share any attribute) up to "Retriker Fuerte". The round winner is decided **solely by `HandRank` order**, not by raw points — `TotalScore` is not used for ranking between players.

## Key Design Decisions

- Game state lives in-process in memory as the source of truth, but each game is also persisted to PostgreSQL as a JSON snapshot (`jsonb`) via EF Core — `GamePersistenceService` serializes `GameState` (enums as strings via `JsonStringEnumConverter`) into the `game_snapshots` table (`AppDbContext`), keyed by game id. Snapshots are written on every state change and reloaded on API startup (`LoadAllAsync` / `EnsureCreatedAsync`), so active games survive restarts.
- `Triker.Shared` contains only DTOs and enums used as the API contract; internal game state (`GameState`, `PlayerState`) lives only in `Triker.Api/State/`.
- `BotService` scores each possible discard combination and picks the best move; it uses the same `ScoringService` as player evaluation.
- The Web project has a `PostPublish.ps1` that fingerprints CSS filenames for cache-busting after `dotnet publish`. This runs automatically on publish.
- The Web `wwwroot/appsettings.json` sets `ApiBaseUrl`; `appsettings.Production.json` overrides this for deployment.

## Deployment

Linux target via `deploy/` scripts (bash + systemd + nginx). The API runs as a systemd service; the Web project is served as static files through nginx.
