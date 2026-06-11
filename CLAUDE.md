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
```

There are no automated tests in this repository.

## Architecture

Three-project .NET 10 solution: a Blazor WebAssembly frontend (`Triker.Web`), an ASP.NET Core API with SignalR (`Triker.Api`), and a shared class library (`Triker.Shared`).

**Communication channels:**
- **HTTP REST** (`/api/games`): game creation, joining, turn submission, state queries
- **SignalR WebSocket** (`/gamehub`): real-time state broadcasts pushed to all clients in a game

**Frontend → Backend flow:** `Triker.Web/Services/ApiService` makes HTTP calls; `HubService` manages the SignalR connection and fires events that Blazor components subscribe to. `SessionService` holds the current player/game session.

**Backend state:** Game state lives entirely in memory in `Triker.Api`. `GameService` owns the game lifecycle and state-to-DTO conversion. `ScoringService` evaluates hands and scores. `InactivityService` drives the turn timer — it auto-plays bot moves and broadcasts updates to all connected clients via `GameHub`.

## Domain Model

Cards have three independent attributes:
- **Letter**: T, R, I, K, E
- **Figure**: ●, ■, ★, ▲, ♥, ♣
- **Color**: Red, Green, Blue, Black, Magenta, Cyan

Each turn, players receive 5 cards, discard some, draw replacements. Groups of matching cards score as `(N-1)² × 2^A` where N = group size and A = number of matching attributes. There are 19 hand ranks from "Sin Jugada" to "Escalera Real".

## Key Design Decisions

- All game state is in-process memory — no database. There is no persistence across API restarts.
- `Triker.Shared` contains only DTOs and enums used as the API contract; internal game state (`GameState`, `PlayerState`) lives only in `Triker.Api/State/`.
- `BotService` scores each possible discard combination and picks the best move; it uses the same `ScoringService` as player evaluation.
- The Web project has a `PostPublish.ps1` that fingerprints CSS filenames for cache-busting after `dotnet publish`. This runs automatically on publish.
- The Web `wwwroot/appsettings.json` sets `ApiBaseUrl`; `appsettings.Production.json` overrides this for deployment.

## Deployment

Linux target via `deploy/` scripts (bash + systemd + nginx). The API runs as a systemd service; the Web project is served as static files through nginx.
