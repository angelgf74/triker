using System.Collections.Concurrent;
using Triker.Api.State;
using Triker.Shared.Dtos;
using Triker.Shared.Models;

namespace Triker.Api.Services;

public class GameService(DeckService deckService, ScoringService scoringService, BotService botService)
{
    private readonly ConcurrentDictionary<Guid, GameState> _games = new();

    public List<GameSummaryDto> ListGames()
        => _games.Values
            .Where(g => g.Status == GameStatus.Waiting || g.Status == GameStatus.Playing)
            .Select(ToSummary)
            .ToList();

    public (GameState game, PlayerState player) CreateGame(string gameName, string playerName)
    {
        var game = new GameState { Name = gameName };
        var player = new PlayerState { Name = playerName };
        game.CreatorId = player.Id;
        game.Players.Add(player);
        _games[game.Id] = game;
        return (game, player);
    }

    public (GameState game, PlayerState player)? JoinGame(Guid gameId, string playerName)
    {
        if (!_games.TryGetValue(gameId, out var game)) return null;
        if (game.Status != GameStatus.Waiting) return null;
        if (game.Players.Count >= game.MaxPlayers) return null;

        var player = new PlayerState { Name = playerName };
        game.Players.Add(player);

        if (game.Players.Count >= game.MaxPlayers)
            StartGame(game);

        return (game, player);
    }

    public bool StartGame(Guid gameId, Guid requesterId)
    {
        if (!_games.TryGetValue(gameId, out var game)) return false;
        if (game.CreatorId != requesterId) return false;
        if (game.Status != GameStatus.Waiting || game.Players.Count < 2) return false;
        StartGame(game);
        return true;
    }

    public GameState? GetGame(Guid gameId)
        => _games.GetValueOrDefault(gameId);

    public PlayerState? GetPlayer(Guid gameId, Guid playerId)
        => _games.GetValueOrDefault(gameId)?.Players.FirstOrDefault(p => p.Id == playerId);

    public bool SubmitDiscard(Guid gameId, Guid playerId, List<Guid> cardIds)
    {
        if (!_games.TryGetValue(gameId, out var game)) return false;
        var player = game.Players.FirstOrDefault(p => p.Id == playerId);
        if (player == null || game.Status != GameStatus.Playing) return false;
        if (game.CurrentTurnPlayer?.Id != playerId) return false;
        if (player.TurnStatus == PlayerTurnStatus.Ready) return false;

        // Remove discarded cards from hand
        player.Discarded = player.Hand.Where(c => cardIds.Contains(c.Id)).ToList();
        player.Hand = player.Hand.Where(c => !cardIds.Contains(c.Id)).ToList();

        // Draw replacement cards
        var (drawn, remaining) = deckService.Draw(game.Deck, game.DiscardPile, player.Discarded.Count);
        player.Hand.AddRange(drawn);
        game.Deck = remaining;
        game.DiscardPile.AddRange(player.Discarded);

        player.TurnStatus = PlayerTurnStatus.Ready;

        // Advance to next player
        game.CurrentTurnIndex++;

        // If all players have had their turn (full rotation from start player), reveal
        if (game.CurrentTurnIndex >= game.StartPlayerIndex + game.Players.Count)
        {
            game.Status = GameStatus.Revealing;
        }

        return true;
    }

    public List<HandResultDto>? GetRevealedHands(Guid gameId)
    {
        if (!_games.TryGetValue(gameId, out var game)) return null;
        if (game.Status != GameStatus.Revealing && game.Status != GameStatus.Finished) return null;

        var results = game.Players
            .Select(p => scoringService.EvaluateHand(p.Id, p.Name, p.Hand))
            .OrderByDescending(r => r.HandRank)
            .ToList();

        for (int i = 0; i < results.Count; i++)
            results[i].Rank = i + 1;

        // Actualizar contadores acumulados usando la misma lógica de posición real (sin empates)
        foreach (var result in results)
        {
            var player = game.Players.FirstOrDefault(p => p.Id == result.PlayerId);
            if (player == null) continue;
            int pos = results.Count(r => r.HandRank > result.HandRank) + 1;
            if (pos == 1) player.Wins++;
            else if (pos == 2) player.Seconds++;
            else if (pos == 3) player.Thirds++;
        }

        game.Status = GameStatus.Finished;
        return results;
    }

    public bool SetContinue(Guid gameId, Guid playerId, bool wantsToContinue)
    {
        if (!_games.TryGetValue(gameId, out var game)) return false;
        var player = game.Players.FirstOrDefault(p => p.Id == playerId);
        if (player == null || game.Status != GameStatus.Finished) return false;

        player.WantsToContinue = wantsToContinue;

        if (!wantsToContinue) return true;

        foreach (var bot in game.Players.Where(p => p.IsBot))
            bot.WantsToContinue = true;

        if (game.Players.All(p => p.WantsToContinue))
            StartNewRound(game);

        return true;
    }

    public GameState? AddBot(Guid gameId, Guid requesterId)
    {
        if (!_games.TryGetValue(gameId, out var game)) return null;
        if (game.CreatorId != requesterId) return null;
        if (game.Status != GameStatus.Waiting) return null;
        if (game.Players.Count >= game.MaxPlayers) return null;

        var (name, personality) = botService.PickBot(game.Players.Select(p => p.Name));
        game.Players.Add(new PlayerState { Name = name, IsBot = true, Personality = personality });
        return game;
    }

    public bool RemovePlayerFromFinishedGame(Guid gameId, Guid playerId)
    {
        if (!_games.TryGetValue(gameId, out var game)) return false;
        if (game.Status != GameStatus.Finished) return false;

        var player = game.Players.FirstOrDefault(p => p.Id == playerId);
        if (player == null) return false;

        game.Players.Remove(player);

        // Close the game when no human players remain
        if (!game.Players.Any(p => !p.IsBot))
        {
            _games.TryRemove(gameId, out _);
            return true;
        }

        // Start a new round if all remaining players are ready to continue
        if (game.Players.All(p => p.WantsToContinue))
            StartNewRound(game);

        return true;
    }

    public bool ProcessBotTurn(Guid gameId)
    {
        if (!_games.TryGetValue(gameId, out var game)) return false;
        var player = game.CurrentTurnPlayer;
        if (player == null || !player.IsBot || game.Status != GameStatus.Playing) return false;

        var discardIds = botService.ChooseDiscards(player.Hand, player.Personality);
        return SubmitDiscard(gameId, player.Id, discardIds);
    }

    public void UpdateConnectionId(Guid gameId, Guid playerId, string connectionId)
    {
        var player = GetPlayer(gameId, playerId);
        if (player != null) player.ConnectionId = connectionId;
    }

    public GameStateDto ToDto(GameState game, Guid? forPlayerId = null)
    {
        return new GameStateDto
        {
            Id = game.Id,
            Name = game.Name,
            Status = game.Status,
            DeckSize = game.Deck.Count,
            CurrentTurnIndex = game.CurrentTurnIndex % Math.Max(1, game.Players.Count),
            CreatorId = game.CreatorId,
            N = game.N,
            RoundNumber = game.RoundNumber,
            MyPlayerId = forPlayerId,
            TurnDeadline = game.TurnDeadline,
            Players = game.Players.Select(p => new PlayerDto
            {
                Id = p.Id,
                Name = p.Name,
                HandSize = p.Hand.Count,
                DiscardCount = p.Discarded.Count,
                TurnStatus = p.TurnStatus,
                IsCurrentTurn = game.Status == GameStatus.Playing && game.CurrentTurnPlayer?.Id == p.Id,
                WantsToContinue = p.WantsToContinue,
                IsBot = p.IsBot,
                Wins = p.Wins,
                Seconds = p.Seconds,
                Thirds = p.Thirds,
                // Only reveal own hand (or during/after Revealing)
                Hand = (forPlayerId == p.Id || game.Status == GameStatus.Revealing || game.Status == GameStatus.Finished)
                    ? p.Hand.Select(c => new CardDto { Id = c.Id, Letter = c.Letter, Figure = c.Figure, Color = c.Color }).ToList()
                    : []
            }).ToList()
        };
    }

    private void StartGame(GameState game)
    {
        game.Deck = deckService.GenerateDeck(game.N);
        game.DiscardPile = [];

        foreach (var player in game.Players)
        {
            var (drawn, remaining) = deckService.Draw(game.Deck, game.DiscardPile, game.N);
            player.Hand = drawn;
            game.Deck = remaining;
            player.TurnStatus = PlayerTurnStatus.Thinking;
            player.Discarded = [];
            player.WantsToContinue = false;
        }

        game.CurrentTurnIndex = game.StartPlayerIndex;
        game.Status = GameStatus.Playing;
    }

    private void StartNewRound(GameState game)
    {
        game.StartPlayerIndex = (game.StartPlayerIndex + 1) % game.Players.Count;
        game.RoundNumber++;
        StartGame(game);
    }

    private static GameSummaryDto ToSummary(GameState g) => new()
    {
        Id = g.Id,
        Name = g.Name,
        Status = g.Status,
        PlayerCount = g.Players.Count,
        MaxPlayers = g.MaxPlayers,
        CreatorName = g.Players.FirstOrDefault()?.Name ?? ""
    };
}
