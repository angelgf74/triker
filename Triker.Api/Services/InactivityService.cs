using System.Collections.Concurrent;
using Triker.Api.State;
using Triker.Shared.Models;

namespace Triker.Api.Services;

public class InactivityService(GameService gameService, GameBroadcaster broadcaster)
{
    private static readonly TimeSpan TurnTimeout   = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan FinishTimeout = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _turnTimers   = new();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _finishTimers = new();

    // ── Timer management ────────────────────────────────────────────────────

    public void StartTurnTimer(Guid gameId, Guid playerId)
    {
        CancelTurnTimer(gameId);
        var cts = new CancellationTokenSource();
        _turnTimers[gameId] = cts;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TurnTimeout, cts.Token);
                _turnTimers.TryRemove(gameId, out _);
                await HandleTurnTimeout(gameId, playerId);
            }
            catch (OperationCanceledException) { }
            finally { cts.Dispose(); }
        });
    }

    public void CancelTurnTimer(Guid gameId)
    {
        if (_turnTimers.TryRemove(gameId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
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
            finally { cts.Dispose(); }
        });
    }

    public void CancelFinishTimer(Guid gameId)
    {
        if (_finishTimers.TryRemove(gameId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    // ── Timeout handlers ────────────────────────────────────────────────────

    private async Task HandleTurnTimeout(Guid gameId, Guid playerId)
    {
        var game = gameService.GetGame(gameId);
        if (game == null || game.Status != GameStatus.Playing) return;
        if (game.CurrentTurnPlayer?.Id != playerId) return;

        // Auto-pass: discard nothing, keep all cards
        if (!gameService.SubmitDiscard(gameId, playerId, []))
            return;

        await ProcessBotsAndBroadcast(gameId);
    }

    private async Task HandleFinishTimeout(Guid gameId)
    {
        var game = gameService.GetGame(gameId);
        if (game == null || game.Status != GameStatus.Finished) return;

        // Gather undecided humans before modifying the list
        var undecided = game.Players
            .Where(p => !p.IsBot && !p.WantsToContinue)
            .ToList();

        foreach (var player in undecided)
        {
            if (player.ConnectionId != null)
                await broadcaster.SendAutoLeft(player.ConnectionId);

            gameService.RemovePlayerFromFinishedGame(gameId, player.Id);
        }

        game = gameService.GetGame(gameId);
        if (game == null) return; // All humans left → game already closed

        await ProcessBotsAndBroadcast(gameId);
    }

    // ── Game orchestration (bots + broadcast) ───────────────────────────────

    public async Task ProcessBotsAndBroadcast(Guid gameId)
    {
        var game = gameService.GetGame(gameId);
        if (game == null) return;

        // If a new round just started, cancel any lingering finish timer
        if (game.Status == GameStatus.Playing)
            CancelFinishTimer(gameId);

        bool anyBotPlayed = false;

        while (game.Status == GameStatus.Playing && game.CurrentTurnPlayer?.IsBot == true)
        {
            await broadcaster.BroadcastPersonalized(game);
            await Task.Delay(1200);

            gameService.ProcessBotTurn(gameId);
            game = gameService.GetGame(gameId)!;
            anyBotPlayed = true;

            await broadcaster.BroadcastPersonalized(game);

            if (game.Status == GameStatus.Playing && game.CurrentTurnPlayer?.IsBot == true)
                await Task.Delay(900);
        }

        if (game.Status == GameStatus.Revealing)
        {
            if (anyBotPlayed) await Task.Delay(600);
            var results = gameService.GetRevealedHands(gameId);
            await broadcaster.SendHandsRevealed(gameId, results!);
            game = gameService.GetGame(gameId)!;
            StartFinishTimer(gameId);
        }

        game = gameService.GetGame(gameId);
        if (game == null) return;

        await broadcaster.BroadcastPersonalized(game);

        // Start turn timer for the next human player
        if (game.Status == GameStatus.Playing && game.CurrentTurnPlayer?.IsBot == false)
            StartTurnTimer(gameId, game.CurrentTurnPlayer.Id);
    }

    // Convenience broadcast without bot processing (used for Waiting-phase events)
    public async Task BroadcastState(Guid gameId)
        => await broadcaster.BroadcastState(gameId);
}
