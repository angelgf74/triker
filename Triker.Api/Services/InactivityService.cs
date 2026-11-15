using System.Collections.Concurrent;
using Triker.Api.State;
using Triker.Shared.Models;

namespace Triker.Api.Services;

public class InactivityService(GameService gameService, GameBroadcaster broadcaster, ILogger<InactivityService> logger)
{
    private static readonly TimeSpan TurnTimeout   = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan FinishTimeout = TimeSpan.FromMinutes(5);

    // El temporizador de turno lleva el jugador al que vigila: solo lo cancela
    // un descarte valido de ese jugador. Antes lo cancelaba cualquier POST a
    // /discard, incluso uno rechazado con un playerId inventado, y la partida
    // se quedaba esperando para siempre.
    private sealed record TurnTimer(CancellationTokenSource Cts, Guid PlayerId);

    private readonly ConcurrentDictionary<Guid, TurnTimer> _turnTimers = new();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _finishTimers = new();

    // ── Timer management ────────────────────────────────────────────────────

    public void StartTurnTimer(Guid gameId, Guid playerId)
    {
        CancelTurnTimer(gameId);
        var game = gameService.GetGame(gameId);
        if (game != null) game.TurnDeadline = DateTimeOffset.UtcNow + TurnTimeout;
        var cts = new CancellationTokenSource();
        var timer = new TurnTimer(cts, playerId);
        _turnTimers[gameId] = timer;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TurnTimeout, cts.Token);
                // Solo si sigue siendo el suyo; si no, ya lo sustituyo otro.
                _turnTimers.TryRemove(new KeyValuePair<Guid, TurnTimer>(gameId, timer));
                await HandleTurnTimeout(gameId, playerId);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { logger.LogError(ex, "Turn timeout failed in game {GameId}", gameId); }
        });
    }

    public void CancelTurnTimer(Guid gameId)
    {
        if (_turnTimers.TryRemove(gameId, out var timer))
            timer.Cts.Cancel();
        var game = gameService.GetGame(gameId);
        if (game != null) game.TurnDeadline = null;
    }

    // Tras un descarte valido: cancela el temporizador solo si era el de ese
    // jugador (si no, es el del siguiente y debe seguir corriendo).
    public void CancelTurnTimer(Guid gameId, Guid playerId)
    {
        if (_turnTimers.TryGetValue(gameId, out var timer) && timer.PlayerId == playerId)
            CancelTurnTimer(gameId);
    }

    public void StartFinishTimer(Guid gameId)
    {
        CancelFinishTimer(gameId);
        var cts = new CancellationTokenSource();
        _finishTimers[gameId] = cts;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(FinishTimeout, cts.Token);
                _finishTimers.TryRemove(gameId, out _);
                await HandleFinishTimeout(gameId);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { logger.LogError(ex, "Finish timeout failed in game {GameId}", gameId); }
        });
    }

    public void CancelFinishTimer(Guid gameId)
    {
        if (_finishTimers.TryRemove(gameId, out var cts))
            cts.Cancel();
    }

    // Tras un reinicio: cada partida restaurada retoma donde estaba. Sin esto
    // una partida en turno de bot quedaba parada hasta caducar (2 horas) y una
    // en turno humano no tenia temporizador.
    public void ResumeRestoredGames(IEnumerable<GameState> games)
    {
        foreach (var game in games)
        {
            switch (game.Status)
            {
                case GameStatus.Playing:
                case GameStatus.Revealing:
                    _ = Task.Run(async () =>
                    {
                        try { await ProcessBotsAndBroadcast(game.Id); }
                        catch (Exception ex) { logger.LogError(ex, "Resume failed for game {GameId}", game.Id); }
                    });
                    break;
                case GameStatus.Finished:
                    StartFinishTimer(game.Id);
                    break;
            }
        }
    }

    // ── Timeout handlers ────────────────────────────────────────────────────

    private async Task HandleTurnTimeout(Guid gameId, Guid playerId)
    {
        var game = gameService.GetGame(gameId);
        if (game == null || game.Status != GameStatus.Playing) return;
        if (game.CurrentTurnPlayer?.Id != playerId) return;

        logger.LogWarning("Turn timeout for player {PlayerId} in game {GameId} — auto-pass", playerId, gameId);

        // La conexion se lee antes de jugar: al pasar el turno avanza y el
        // jugador deja de ser CurrentTurnPlayer.
        var connectionId = gameService.ConnectionOf(game, playerId);

        // Auto-pass: discard nothing, keep all cards
        if (!gameService.SubmitDiscard(gameId, playerId, []))
            return;

        // Queda marcado en el estado, no solo en este aviso: quien se queda sin
        // tiempo suele estar mirando a otro lado, asi que el banner puede
        // perderselo y necesita encontrarlo luego en los resultados.
        lock (game.SyncLock)
        {
            var player = game.Players.FirstOrDefault(p => p.Id == playerId);
            if (player != null) player.TurnTimedOut = true;
        }
        if (connectionId != null) await broadcaster.SendTurnTimedOut(connectionId);

        await ProcessBotsAndBroadcast(gameId);
    }

    private async Task HandleFinishTimeout(Guid gameId)
    {
        var game = gameService.GetGame(gameId);
        if (game == null || game.Status != GameStatus.Finished) return;
        logger.LogInformation("Finish timeout for game {GameId} — removing undecided players", gameId);

        // Primero se saca a todos (de una vez, ver RemoveUndecidedPlayers) y
        // luego se avisa solo a quien de verdad ha salido.
        foreach (var (_, connectionId) in gameService.RemoveUndecidedPlayers(gameId))
            if (connectionId != null)
                await broadcaster.SendAutoLeft(connectionId);

        await ProcessBotsAndBroadcast(gameId);
    }

    // ── Game orchestration (bots + broadcast) ───────────────────────────────

    public async Task ProcessBotsAndBroadcast(Guid gameId)
    {
        var game = gameService.GetGame(gameId);
        if (game == null)
        {
            await broadcaster.BroadcastLobbyUpdate();
            return;
        }

        // If a new round just started (or the table went back to the waiting
        // room), cancel any lingering finish timer
        if (game.Status != GameStatus.Finished)
            CancelFinishTimer(gameId);

        bool anyBotPlayed = false;

        while (game.Status == GameStatus.Playing && game.CurrentTurnPlayer?.IsBot == true)
        {
            var botName = game.CurrentTurnPlayer.Name;
            await broadcaster.BroadcastPersonalized(game);
            await Task.Delay(1200);

            if (!gameService.ProcessBotTurn(gameId))
                break; // otro hilo jugo ese turno o la partida cambio
            anyBotPlayed = true;
            logger.LogDebug("Bot '{Bot}' played in game {GameId}", botName, gameId);

            await broadcaster.BroadcastPersonalized(game);

            if (game.Status == GameStatus.Playing && game.CurrentTurnPlayer?.IsBot == true)
                await Task.Delay(900);
        }

        if (game.Status == GameStatus.Revealing)
        {
            if (anyBotPlayed) await Task.Delay(600);
            // Null si otro hilo ya la revelo: no se repite el evento.
            var results = gameService.GetRevealedHands(gameId);
            if (results != null)
            {
                await broadcaster.SendHandsRevealed(gameId, results);
                StartFinishTimer(gameId);
            }
        }

        game = gameService.GetGame(gameId);
        if (game == null)
        {
            await broadcaster.BroadcastLobbyUpdate();
            return;
        }

        // Start turn timer before broadcasting so TurnDeadline is included in the state the client receives
        if (game.Status == GameStatus.Playing && game.CurrentTurnPlayer is { IsBot: false } human)
            StartTurnTimer(gameId, human.Id);

        await broadcaster.BroadcastPersonalized(game);
        await broadcaster.BroadcastLobbyUpdate();
    }

    // Convenience broadcast without bot processing (used for Waiting-phase events)
    public async Task BroadcastState(Guid gameId)
        => await broadcaster.BroadcastState(gameId);
}
