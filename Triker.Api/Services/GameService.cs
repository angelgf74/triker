using System.Collections.Concurrent;
using Triker.Api.State;
using Triker.Shared.Dtos;
using Triker.Shared.Models;

namespace Triker.Api.Services;

public class GameService(
    DeckService deckService,
    ScoringService scoringService,
    BotService botService,
    ILogger<GameService> logger)
{
    private static readonly TimeSpan FinishedGameTtl = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan ActiveGameTtl   = TimeSpan.FromHours(2);

    private readonly ConcurrentDictionary<Guid, GameState> _games = new();

    public List<GameSummaryDto> ListGames()
        => _games.Values
            .Where(g => g.Status == GameStatus.Waiting || g.Status == GameStatus.Playing)
            .Select(ToSummary)
            .ToList();

    public (GameState game, PlayerState player) CreateGame(string gameName, string playerName, int targetWins = 0, bool isPrivate = false)
    {
        var game = new GameState
        {
            Name = gameName,
            TargetWins = Math.Max(0, targetWins),
            IsPrivate = isPrivate,
            AccessCode = isPrivate ? Random.Shared.Next(1000, 10000).ToString() : null
        };
        var player = new PlayerState { Name = playerName };
        game.CreatorId = player.Id;
        game.Players.Add(player);
        _games[game.Id] = game;
        logger.LogInformation("Game created: {GameId} '{GameName}' by '{Player}' (target={Target}, private={Private})",
            game.Id, gameName, playerName, game.TargetWins == 0 ? "∞" : game.TargetWins.ToString(), isPrivate);
        return (game, player);
    }

    public enum JoinResult { Ok, NotFound, NotWaiting, Full, WrongCode }

    public (JoinResult result, GameState? game, PlayerState? player) JoinGame(Guid gameId, string playerName, string? accessCode = null)
    {
        if (!_games.TryGetValue(gameId, out var game)) return (JoinResult.NotFound, null, null);
        if (game.Status != GameStatus.Waiting) return (JoinResult.NotWaiting, null, null);
        if (game.Players.Count >= game.MaxPlayers) return (JoinResult.Full, null, null);
        if (game.IsPrivate && !string.Equals(game.AccessCode, accessCode?.Trim(), StringComparison.Ordinal))
            return (JoinResult.WrongCode, null, null);

        var player = new PlayerState { Name = playerName };
        game.Players.Add(player);
        game.LastActivityAt = DateTimeOffset.UtcNow;
        logger.LogInformation("Player '{Player}' joined game {GameId} ({Count}/{Max})",
            playerName, gameId, game.Players.Count, game.MaxPlayers);

        if (game.Players.Count >= game.MaxPlayers)
            StartGame(game);

        return (JoinResult.Ok, game, player);
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

        lock (game.SyncLock)
        {
            var player = game.Players.FirstOrDefault(p => p.Id == playerId);
            if (player == null || game.Status != GameStatus.Playing) return false;
            if (game.CurrentTurnPlayer?.Id != playerId) return false;
            if (player.TurnStatus == PlayerTurnStatus.Ready) return false;

            player.Discarded = player.Hand.Where(c => cardIds.Contains(c.Id)).ToList();
            player.Hand      = player.Hand.Where(c => !cardIds.Contains(c.Id)).ToList();

            var (drawn, remaining) = deckService.Draw(game.Deck, game.DiscardPile, player.Discarded.Count);
            player.Hand.AddRange(drawn);
            game.Deck = remaining;
            game.DiscardPile.AddRange(player.Discarded);

            player.TurnStatus = PlayerTurnStatus.Ready;
            game.CurrentTurnIndex++;
            game.LastActivityAt = DateTimeOffset.UtcNow;

            logger.LogDebug("Player '{Player}' discarded {Count} cards in game {GameId}",
                player.Name, player.Discarded.Count, gameId);

            if (game.CurrentTurnIndex >= game.StartPlayerIndex + game.Players.Count)
            {
                game.Status = GameStatus.Revealing;
                logger.LogInformation("Game {GameId} round {Round} revealing hands", gameId, game.RoundNumber);
            }

            return true;
        }
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
        game.LastActivityAt = DateTimeOffset.UtcNow;

        if (game.TargetWins > 0 && game.Players.Any(p => p.Wins >= game.TargetWins))
        {
            game.GameOver = true;
            logger.LogInformation("Game {GameId} over — target {Target} wins reached", gameId, game.TargetWins);
        }

        logger.LogInformation("Game {GameId} round {Round} finished. Winner: '{Winner}' ({Hand})",
            gameId, game.RoundNumber, results[0].PlayerName, results[0].HandName);

        return results;
    }

    public bool SetContinue(Guid gameId, Guid playerId, bool wantsToContinue)
    {
        if (!_games.TryGetValue(gameId, out var game)) return false;
        var player = game.Players.FirstOrDefault(p => p.Id == playerId);
        if (player == null || game.Status != GameStatus.Finished) return false;

        player.WantsToContinue = wantsToContinue;
        game.LastActivityAt = DateTimeOffset.UtcNow;

        if (!wantsToContinue)
        {
            logger.LogInformation("Player '{Player}' left game {GameId}", player.Name, gameId);
            return true;
        }

        foreach (var bot in game.Players.Where(p => p.IsBot))
            bot.WantsToContinue = true;

        if (!game.GameOver && game.Players.All(p => p.WantsToContinue))
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
        logger.LogDebug("Bot '{Name}' ({Personality}) added to game {GameId}", name, personality, gameId);
        return game;
    }

    public bool RemovePlayerFromFinishedGame(Guid gameId, Guid playerId)
    {
        if (!_games.TryGetValue(gameId, out var game)) return false;
        if (game.Status != GameStatus.Finished) return false;

        var player = game.Players.FirstOrDefault(p => p.Id == playerId);
        if (player == null) return false;

        game.Players.Remove(player);
        game.LastActivityAt = DateTimeOffset.UtcNow;

        if (!game.Players.Any(p => !p.IsBot))
        {
            _games.TryRemove(gameId, out _);
            logger.LogInformation("Game {GameId} closed (no human players remaining)", gameId);
            return true;
        }

        if (!game.GameOver && game.Players.All(p => p.WantsToContinue))
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

    public enum SpectateResult { Ok, NotFound, GameOver, WrongCode }

    public (SpectateResult result, GameState? game, SpectatorState? spectator) AddSpectator(Guid gameId, string name, string? accessCode)
    {
        if (!_games.TryGetValue(gameId, out var game)) return (SpectateResult.NotFound, null, null);
        if (game.Status == GameStatus.Finished && game.GameOver) return (SpectateResult.GameOver, null, null);
        if (game.IsPrivate && !string.Equals(game.AccessCode, accessCode?.Trim(), StringComparison.Ordinal))
            return (SpectateResult.WrongCode, null, null);

        var spectator = new SpectatorState { Name = string.IsNullOrWhiteSpace(name) ? "Espectador" : name };
        game.Spectators.Add(spectator);
        game.LastActivityAt = DateTimeOffset.UtcNow;
        logger.LogInformation("Spectator '{Name}' joined game {GameId} (total spectators: {Count})",
            spectator.Name, gameId, game.Spectators.Count);
        return (SpectateResult.Ok, game, spectator);
    }

    public SpectatorState? GetSpectator(Guid gameId, Guid spectatorId)
        => _games.GetValueOrDefault(gameId)?.Spectators.FirstOrDefault(s => s.Id == spectatorId);

    public void UpdateSpectatorConnectionId(Guid gameId, Guid spectatorId, string connectionId)
    {
        var s = GetSpectator(gameId, spectatorId);
        if (s != null) s.ConnectionId = connectionId;
    }

    public bool RemoveSpectator(Guid gameId, Guid spectatorId)
    {
        if (!_games.TryGetValue(gameId, out var game)) return false;
        var s = game.Spectators.FirstOrDefault(x => x.Id == spectatorId);
        if (s == null) return false;
        game.Spectators.Remove(s);
        logger.LogInformation("Spectator '{Name}' left game {GameId}", s.Name, gameId);
        return true;
    }

    public Guid? RemoveSpectatorByConnection(string connectionId)
    {
        foreach (var game in _games.Values)
        {
            var s = game.Spectators.FirstOrDefault(x => x.ConnectionId == connectionId);
            if (s != null)
            {
                game.Spectators.Remove(s);
                logger.LogInformation("Spectator '{Name}' disconnected from game {GameId}", s.Name, game.Id);
                return game.Id;
            }
        }
        return null;
    }

    public static readonly HashSet<string> AllowedReactionEmojis = new() { "👍", "❤️", "😂", "😮", "😢", "🎉" };
    private static readonly TimeSpan ReactionCooldown = TimeSpan.FromMilliseconds(1500);

    public ChatReactionDto? SendReaction(Guid gameId, Guid playerId, string emoji)
    {
        if (!_games.TryGetValue(gameId, out var game)) return null;
        var player = game.Players.FirstOrDefault(p => p.Id == playerId);
        if (player == null) return null;
        if (!AllowedReactionEmojis.Contains(emoji)) return null;

        var now = DateTimeOffset.UtcNow;
        if (now - player.LastReactionAt < ReactionCooldown) return null;
        player.LastReactionAt = now;

        return new ChatReactionDto
        {
            PlayerId = player.Id,
            PlayerName = player.Name,
            Emoji = emoji
        };
    }

    public int CleanupStaleGames()
    {
        var now = DateTimeOffset.UtcNow;
        var stale = _games.Values
            .Where(g => g.Status == GameStatus.Finished
                ? (now - g.LastActivityAt) > FinishedGameTtl
                : (now - g.LastActivityAt) > ActiveGameTtl)
            .Select(g => g.Id)
            .ToList();

        foreach (var id in stale)
            _games.TryRemove(id, out _);

        return stale.Count;
    }

    public GameStateDto ToDto(GameState game, Guid? forPlayerId = null, bool isSpectator = false)
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
            TargetWins = game.TargetWins,
            GameOver = game.GameOver,
            IsPrivate = game.IsPrivate,
            AccessCode = forPlayerId.HasValue && game.Players.Any(p => p.Id == forPlayerId.Value)
                ? game.AccessCode
                : null,
            Spectators = game.Spectators.Select(s => new SpectatorDto { Id = s.Id, Name = s.Name }).ToList(),
            IAmSpectator = isSpectator,
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
        game.LastActivityAt = DateTimeOffset.UtcNow;

        logger.LogInformation("Game {GameId} '{GameName}' round {Round} started with {Count} players",
            game.Id, game.Name, game.RoundNumber, game.Players.Count);
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
        CreatorName = g.Players.FirstOrDefault()?.Name ?? "",
        IsPrivate = g.IsPrivate
    };
}
