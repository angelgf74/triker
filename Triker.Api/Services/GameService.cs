using System.Collections.Concurrent;
using Triker.Api.State;
using Triker.Shared.Dtos;
using Triker.Shared.Models;

namespace Triker.Api.Services;

// Ciclo de vida de las partidas. Toda lectura o cambio de una partida va dentro
// de lock (game.SyncLock): llegan a la vez peticiones HTTP, temporizadores y el
// hub, y sin cerrojo dos "continuar" simultaneos repartian dos rondas seguidas.
// El cerrojo es reentrante (Monitor), asi que un metodo que ya lo tiene puede
// llamar a otro que lo vuelve a pedir.
//
// Los snapshots se piden dentro del cerrojo: GamePersistenceService serializa
// en el acto y escribe despues, asi que el JSON nunca mezcla dos estados.
public class GameService(
    DeckService deckService,
    ScoringService scoringService,
    BotService botService,
    GamePersistenceService persistence,
    ILogger<GameService> logger)
{
    private const int HandSize = 5;

    public static readonly TimeSpan FinishedGameTtl = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan ActiveGameTtl   = TimeSpan.FromHours(2);
    private static readonly TimeSpan SpectatorGoneTtl = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<Guid, GameState> _games = new();

    public List<GameSummaryDto> ListGames()
    {
        var list = new List<GameSummaryDto>();
        foreach (var g in _games.Values)
        {
            lock (g.SyncLock)
            {
                if (g.Status == GameStatus.Waiting || g.Status == GameStatus.Playing)
                    list.Add(ToSummary(g));
            }
        }
        return list;
    }

    // Null si ya hay demasiadas partidas abiertas (el controlador responde 503).
    public (GameState game, PlayerState player)? CreateGame(string gameName, string playerName, int targetWins = 0, bool isPrivate = false)
    {
        if (_games.Count >= Limites.PartidasMaximas) return null;

        var game = new GameState
        {
            Name = gameName,
            TargetWins = Math.Clamp(targetWins, 0, Limites.MetaMaxima),
            IsPrivate = isPrivate,
            AccessCode = isPrivate ? Limites.NuevoCodigo() : null
        };
        var player = new PlayerState { Name = playerName, Token = Limites.NuevoToken() };
        game.CreatorId = player.Id;
        game.Players.Add(player);

        lock (game.SyncLock)
        {
            _games[game.Id] = game;
            _ = persistence.SaveAsync(game);
        }
        logger.LogInformation("Game created: {GameId} '{GameName}' by '{Player}' (target={Target}, private={Private})",
            game.Id, gameName, playerName, game.TargetWins == 0 ? "∞" : game.TargetWins.ToString(), isPrivate);
        return (game, player);
    }

    public enum JoinResult { Ok, NotFound, NotWaiting, Full, WrongCode }

    public (JoinResult result, GameState? game, PlayerState? player) JoinGame(Guid gameId, string playerName, string? accessCode = null)
    {
        if (!_games.TryGetValue(gameId, out var game)) return (JoinResult.NotFound, null, null);

        lock (game.SyncLock)
        {
            if (game.Status != GameStatus.Waiting) return (JoinResult.NotWaiting, null, null);
            if (game.Players.Count >= game.MaxPlayers) return (JoinResult.Full, null, null);
            if (game.IsPrivate && !string.Equals(game.AccessCode, accessCode?.Trim(), StringComparison.Ordinal))
                return (JoinResult.WrongCode, null, null);

            var player = new PlayerState { Name = playerName, Token = Limites.NuevoToken() };
            game.Players.Add(player);
            game.LastActivityAt = DateTimeOffset.UtcNow;
            logger.LogInformation("Player '{Player}' joined game {GameId} ({Count}/{Max})",
                playerName, gameId, game.Players.Count, game.MaxPlayers);

            if (game.Players.Count >= game.MaxPlayers)
                StartGameLocked(game);
            else
                _ = persistence.SaveAsync(game);

            return (JoinResult.Ok, game, player);
        }
    }

    public bool StartGame(Guid gameId, Guid requesterId)
    {
        if (!_games.TryGetValue(gameId, out var game)) return false;
        lock (game.SyncLock)
        {
            if (game.CreatorId != requesterId) return false;
            if (game.Status != GameStatus.Waiting || game.Players.Count < 2) return false;
            StartGameLocked(game);
            return true;
        }
    }

    public GameState? GetGame(Guid gameId)
        => _games.GetValueOrDefault(gameId);

    public IReadOnlyList<GameState> AllGames() => [.. _games.Values];

    // ── Identidad ───────────────────────────────────────────────────────────
    // El id es publico; el token, secreto. Los bots no tienen token y nadie
    // puede actuar por ellos desde fuera.

    public bool IsPlayer(GameState game, Guid playerId, string? token)
    {
        lock (game.SyncLock)
        {
            var p = game.Players.FirstOrDefault(x => x.Id == playerId);
            return p is { IsBot: false, HasLeft: false } && Limites.MismoToken(p.Token, token);
        }
    }

    public bool IsSpectator(GameState game, Guid spectatorId, string? token)
    {
        lock (game.SyncLock)
        {
            var s = game.Spectators.FirstOrDefault(x => x.Id == spectatorId);
            return s != null && Limites.MismoToken(s.Token, token);
        }
    }

    public bool ConnectPlayer(GameState game, Guid playerId, string? token, string connectionId)
    {
        lock (game.SyncLock)
        {
            if (!IsPlayer(game, playerId, token)) return false;
            game.Players.First(p => p.Id == playerId).ConnectionId = connectionId;
            return true;
        }
    }

    public bool ConnectSpectator(GameState game, Guid spectatorId, string? token, string connectionId)
    {
        lock (game.SyncLock)
        {
            if (!IsSpectator(game, spectatorId, token)) return false;
            var s = game.Spectators.First(x => x.Id == spectatorId);
            s.ConnectionId = connectionId;
            s.DisconnectedAt = null;
            return true;
        }
    }

    // ── Juego ───────────────────────────────────────────────────────────────

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
            // Quien salio a mitad de ronda no juega: su turno se pasa solo.
            while (!RoundCompleteLocked(game) && game.CurrentTurnPlayer is { HasLeft: true } gone)
            {
                gone.TurnStatus = PlayerTurnStatus.Ready;
                game.CurrentTurnIndex++;
            }
            game.LastActivityAt = DateTimeOffset.UtcNow;

            logger.LogDebug("Player '{Player}' discarded {Count} cards in game {GameId}",
                player.Name, player.Discarded.Count, gameId);

            if (RoundCompleteLocked(game))
            {
                game.Status = GameStatus.Revealing;
                logger.LogInformation("Game {GameId} round {Round} revealing hands", gameId, game.RoundNumber);
            }

            _ = persistence.SaveAsync(game);
            return true;
        }
    }

    // Reparte medallas y deja la partida en Finished. Solo actua en Revealing:
    // si dos hilos llegan a la vez, el segundo recibe null y no vuelve a contar
    // las medallas de la ronda.
    public List<HandResultDto>? GetRevealedHands(Guid gameId)
    {
        if (!_games.TryGetValue(gameId, out var game)) return null;

        lock (game.SyncLock)
        {
            if (game.Status != GameStatus.Revealing) return null;

            // Quien salio a mitad de ronda sale ahora de la lista: su mano no
            // cuenta ni se lleva medalla.
            if (game.Players.RemoveAll(p => p.HasLeft) > 0)
                InheritCreatorLocked(game);

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

            // Los bots siempre siguen: asi el "x/y quieren continuar" cuenta
            // solo lo que falta decidir, que es lo de los humanos.
            foreach (var bot in game.Players.Where(p => p.IsBot))
                bot.WantsToContinue = true;

            game.LastResults = results;
            game.Status = GameStatus.Finished;
            game.LastActivityAt = DateTimeOffset.UtcNow;

            if (game.TargetWins > 0 && game.Players.Any(p => p.Wins >= game.TargetWins))
            {
                game.GameOver = true;
                logger.LogInformation("Game {GameId} over — target {Target} wins reached", gameId, game.TargetWins);
            }

            logger.LogInformation("Game {GameId} round {Round} finished. Winner: '{Winner}' ({Hand})",
                gameId, game.RoundNumber, results[0].PlayerName, results[0].HandName);

            _ = persistence.SaveAsync(game);
            return results;
        }
    }

    // Seguir o dejar la mesa al acabar la ronda. Dejarla saca al jugador en el
    // acto: antes solo se marcaba y los demas esperaban los 5 minutos del
    // temporizador de fin de ronda.
    public bool SetContinue(Guid gameId, Guid playerId, bool wantsToContinue)
    {
        if (!_games.TryGetValue(gameId, out var game)) return false;

        lock (game.SyncLock)
        {
            var player = game.Players.FirstOrDefault(p => p.Id == playerId);
            if (player == null || player.IsBot || game.Status != GameStatus.Finished) return false;

            game.LastActivityAt = DateTimeOffset.UtcNow;

            if (!wantsToContinue)
            {
                logger.LogInformation("Player '{Player}' left game {GameId}", player.Name, gameId);
                RemoveFromFinishedLocked(game, player);
                return true;
            }

            player.WantsToContinue = true;
            AfterContinueDecisionLocked(game);
            return true;
        }
    }

    // El temporizador de fin de ronda saca de una vez a todos los humanos que
    // no han decidido y devuelve quienes eran, para avisarles. De uno en uno
    // no vale: al sacar al primero la mesa podia volver a la sala de espera y
    // el resto se quedaba dentro, avisado ya de que le habian sacado.
    public List<(Guid Id, string? ConnectionId)> RemoveUndecidedPlayers(Guid gameId)
    {
        if (!_games.TryGetValue(gameId, out var game)) return [];

        lock (game.SyncLock)
        {
            if (game.Status != GameStatus.Finished) return [];
            var undecided = game.Players.Where(p => !p.IsBot && !p.WantsToContinue).ToList();
            if (undecided.Count == 0) return [];

            logger.LogInformation("Removing {Count} undecided player(s) from game {GameId}", undecided.Count, gameId);
            RemoveFromFinishedLocked(game, undecided);
            return undecided.Select(p => (p.Id, p.ConnectionId)).ToList();
        }
    }

    private void RemoveFromFinishedLocked(GameState game, PlayerState player)
        => RemoveFromFinishedLocked(game, [player]);

    private void RemoveFromFinishedLocked(GameState game, List<PlayerState> leaving)
    {
        foreach (var p in leaving) game.Players.Remove(p);
        game.LastActivityAt = DateTimeOffset.UtcNow;

        if (CloseIfNoHumansLocked(game)) return;
        InheritCreatorLocked(game);
        AfterContinueDecisionLocked(game);
    }

    private static bool RoundCompleteLocked(GameState game)
        => game.CurrentTurnIndex >= game.StartPlayerIndex + game.Players.Count;

    // Sin humanos (los que han salido a mitad de ronda no cuentan) no hay
    // partida: se cierra y se borra su snapshot.
    private bool CloseIfNoHumansLocked(GameState game)
    {
        if (game.Players.Any(p => !p.IsBot && !p.HasLeft)) return false;

        _games.TryRemove(game.Id, out _);
        _ = persistence.DeleteAsync(game.Id);
        logger.LogInformation("Game {GameId} closed (no human players remaining)", game.Id);
        return true;
    }

    // El creador es quien puede añadir bots e iniciar si la mesa vuelve a la
    // sala de espera; si ya no esta, hereda el puesto el primer humano.
    private static void InheritCreatorLocked(GameState game)
    {
        if (game.Players.Any(p => p.Id == game.CreatorId && !p.HasLeft)) return;
        var heir = game.Players.FirstOrDefault(p => !p.IsBot && !p.HasLeft);
        if (heir != null) game.CreatorId = heir.Id;
    }

    private void AfterContinueDecisionLocked(GameState game)
    {
        if (game.GameOver)
        {
            _ = persistence.SaveAsync(game);
            return;
        }

        // Con un solo jugador no hay partida: vuelve a la sala de espera, donde
        // puede invitar a alguien o añadir bots. Antes repartia una ronda en
        // solitario que ganaba siempre.
        if (game.Players.Count < 2)
        {
            BackToWaitingLocked(game);
            return;
        }

        if (game.Players.All(p => p.WantsToContinue))
            StartNewRoundLocked(game);
        else
            _ = persistence.SaveAsync(game);
    }

    private void BackToWaitingLocked(GameState game)
    {
        game.Status = GameStatus.Waiting;
        // La ronda siguiente lleva numero nuevo: RoundNumber es tambien el
        // numero de rondas jugadas con que se calculan los porcentajes.
        game.RoundNumber++;
        game.LastResults = null;
        game.CurrentTurnIndex = 0;
        game.StartPlayerIndex = 0;
        game.TurnDeadline = null;
        foreach (var p in game.Players)
        {
            p.Hand = [];
            p.Discarded = [];
            p.TurnStatus = PlayerTurnStatus.Thinking;
            p.WantsToContinue = false;
            p.TurnTimedOut = false;
        }
        logger.LogInformation("Game {GameId} back to waiting room (not enough players)", game.Id);
        _ = persistence.SaveAsync(game);
    }

    // Salir de la mesa en cualquier momento.
    //   - Sala de espera o ronda terminada: sale de la lista en el acto.
    //   - A mitad de ronda: queda marcado (HasLeft) y sigue en la lista hasta
    //     revelar. El turno es un contador sobre esa lista (CurrentTurnIndex)
    //     y quitarle cambiaria a quien le toca. Su asiento deja de valer, su
    //     turno se pasa solo y su mano no cuenta.
    public bool LeaveGame(Guid gameId, Guid playerId)
    {
        if (!_games.TryGetValue(gameId, out var game)) return false;

        lock (game.SyncLock)
        {
            var player = game.Players.FirstOrDefault(p => p.Id == playerId);
            if (player is null or { IsBot: true } or { HasLeft: true }) return false;

            logger.LogInformation("Player '{Player}' left game {GameId} ({Status})", player.Name, gameId, game.Status);
            game.LastActivityAt = DateTimeOffset.UtcNow;

            switch (game.Status)
            {
                case GameStatus.Waiting:
                    game.Players.Remove(player);
                    if (CloseIfNoHumansLocked(game)) return true;
                    InheritCreatorLocked(game);
                    _ = persistence.SaveAsync(game);
                    return true;

                case GameStatus.Finished:
                    RemoveFromFinishedLocked(game, player);
                    return true;

                default: // Playing o Revealing
                    player.HasLeft = true;
                    player.ConnectionId = null;   // ya no recibe el estado (lleva su mano)
                    if (CloseIfNoHumansLocked(game)) return true;
                    if (game.Status == GameStatus.Playing && game.CurrentTurnPlayer?.Id == playerId)
                        SubmitDiscard(gameId, playerId, []);   // pasa y guarda
                    else
                        _ = persistence.SaveAsync(game);
                    return true;
            }
        }
    }

    public GameState? AddBot(Guid gameId, Guid requesterId)
    {
        if (!_games.TryGetValue(gameId, out var game)) return null;

        lock (game.SyncLock)
        {
            if (game.CreatorId != requesterId) return null;
            if (game.Status != GameStatus.Waiting) return null;
            if (game.Players.Count >= game.MaxPlayers) return null;

            var (name, personality) = botService.PickBot(game.Players.Select(p => p.Name));
            game.Players.Add(new PlayerState { Name = name, IsBot = true, Personality = personality });
            game.LastActivityAt = DateTimeOffset.UtcNow;
            logger.LogDebug("Bot '{Name}' ({Personality}) added to game {GameId}", name, personality, gameId);
            _ = persistence.SaveAsync(game);
            return game;
        }
    }

    public bool ProcessBotTurn(Guid gameId)
    {
        if (!_games.TryGetValue(gameId, out var game)) return false;

        lock (game.SyncLock)
        {
            var player = game.CurrentTurnPlayer;
            if (player == null || !player.IsBot || game.Status != GameStatus.Playing) return false;

            // Si la estrategia falla, el bot pasa: mejor eso que dejar la mesa
            // parada para siempre en su turno.
            List<Guid> discardIds;
            try { discardIds = botService.ChooseDiscards(player.Hand, player.Personality); }
            catch (Exception ex)
            {
                logger.LogError(ex, "Bot '{Bot}' failed to choose discards in game {GameId}; passing", player.Name, gameId);
                discardIds = [];
            }
            return SubmitDiscard(gameId, player.Id, discardIds);
        }
    }

    // ── Espectadores ────────────────────────────────────────────────────────

    public enum SpectateResult { Ok, NotFound, GameOver, WrongCode, Full }

    public (SpectateResult result, GameState? game, SpectatorState? spectator) AddSpectator(Guid gameId, string name, string? accessCode)
    {
        if (!_games.TryGetValue(gameId, out var game)) return (SpectateResult.NotFound, null, null);

        lock (game.SyncLock)
        {
            if (game.Status == GameStatus.Finished && game.GameOver) return (SpectateResult.GameOver, null, null);
            if (game.IsPrivate && !string.Equals(game.AccessCode, accessCode?.Trim(), StringComparison.Ordinal))
                return (SpectateResult.WrongCode, null, null);
            if (game.Spectators.Count >= Limites.EspectadoresMaximos) return (SpectateResult.Full, null, null);

            var spectator = new SpectatorState { Name = name, Token = Limites.NuevoToken() };
            game.Spectators.Add(spectator);
            game.LastActivityAt = DateTimeOffset.UtcNow;
            logger.LogInformation("Spectator '{Name}' joined game {GameId} (total spectators: {Count})",
                spectator.Name, gameId, game.Spectators.Count);
            return (SpectateResult.Ok, game, spectator);
        }
    }

    public bool RemoveSpectator(Guid gameId, Guid spectatorId)
    {
        if (!_games.TryGetValue(gameId, out var game)) return false;

        lock (game.SyncLock)
        {
            var s = game.Spectators.FirstOrDefault(x => x.Id == spectatorId);
            if (s == null) return false;
            game.Spectators.Remove(s);
            logger.LogInformation("Spectator '{Name}' left game {GameId}", s.Name, gameId);
            return true;
        }
    }

    // Solo si la conexion que se cae es la que tiene apuntada: con la
    // reconexion automatica la nueva puede registrarse antes de que el
    // servidor note que la vieja se cerro.
    public Guid? MarkSpectatorDisconnected(string connectionId)
    {
        foreach (var game in _games.Values)
        {
            lock (game.SyncLock)
            {
                var s = game.Spectators.FirstOrDefault(x => x.ConnectionId == connectionId);
                if (s == null) continue;
                s.ConnectionId = null;
                s.DisconnectedAt = DateTimeOffset.UtcNow;
                return game.Id;
            }
        }
        return null;
    }

    // ── Reacciones ──────────────────────────────────────────────────────────

    public static readonly HashSet<string> AllowedReactionEmojis = new() { "👍", "❤️", "😂", "😮", "😢", "🎉" };
    private static readonly TimeSpan ReactionCooldown = TimeSpan.FromMilliseconds(1500);

    public ChatReactionDto? SendReaction(Guid gameId, Guid playerId, string emoji)
    {
        if (!_games.TryGetValue(gameId, out var game)) return null;
        if (!AllowedReactionEmojis.Contains(emoji)) return null;

        lock (game.SyncLock)
        {
            var player = game.Players.FirstOrDefault(p => p.Id == playerId);
            if (player == null) return null;

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
    }

    // ── Limpieza y persistencia ─────────────────────────────────────────────

    private static bool IsStale(GameState g, DateTimeOffset now)
        => g.Status == GameStatus.Finished
            ? (now - g.LastActivityAt) > FinishedGameTtl
            : (now - g.LastActivityAt) > ActiveGameTtl;

    public int CleanupStaleGames()
    {
        var now = DateTimeOffset.UtcNow;
        var stale = new List<Guid>();

        foreach (var g in _games.Values)
        {
            lock (g.SyncLock)
            {
                if (IsStale(g, now)) { stale.Add(g.Id); continue; }
                g.Spectators.RemoveAll(s => s.ConnectionId == null
                    && s.DisconnectedAt is { } at && now - at > SpectatorGoneTtl);
            }
        }

        foreach (var id in stale)
            _games.TryRemove(id, out _);

        if (stale.Count > 0)
            _ = persistence.DeleteManyAsync(stale);

        return stale.Count;
    }

    // Devuelve las partidas restauradas para que InactivityService retome sus
    // temporizadores y bots (ver Program.cs).
    public async Task<IReadOnlyList<GameState>> LoadFromPersistenceAsync()
    {
        var games = await persistence.LoadAllAsync();
        var (restored, discarded) = RestoreGames(games, DateTimeOffset.UtcNow);

        if (discarded.Count > 0)
            await persistence.DeleteManyAsync(discarded);

        if (restored.Count > 0 || discarded.Count > 0)
            logger.LogInformation("Restored {Restored} game(s) from persistence, discarded {Discarded}",
                restored.Count, discarded.Count);
        return restored;
    }

    // Se restauran tambien las partidas en Finished: antes se tiraban, y cada
    // despliegue cerraba las mesas que estaban entre dos rondas. Se descartan
    // (y se borran de la base) las caducadas y las guardadas antes de que los
    // jugadores tuvieran token, en las que ya nadie podria identificarse.
    public (List<GameState> restored, List<Guid> discarded) RestoreGames(IEnumerable<GameState> games, DateTimeOffset now)
    {
        var restored = new List<GameState>();
        var discarded = new List<Guid>();

        foreach (var game in games)
        {
            var legacy = game.Players.Any(p => !p.IsBot && string.IsNullOrEmpty(p.Token));
            if (IsStale(game, now) || legacy)
            {
                discarded.Add(game.Id);
                continue;
            }

            // Las conexiones no sobreviven al reinicio: los clientes vuelven
            // a entrar con GameHub.Entrar. Los espectadores, a pedir sitio.
            foreach (var p in game.Players) p.ConnectionId = null;
            game.Spectators.Clear();
            // InactivityService vuelve a armar el temporizador.
            game.TurnDeadline = null;

            _games[game.Id] = game;
            restored.Add(game);
        }

        return (restored, discarded);
    }

    // ── DTOs ────────────────────────────────────────────────────────────────

    public GameStateDto ToDto(GameState game, Guid? forPlayerId = null, bool isSpectator = false)
    {
        lock (game.SyncLock)
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
                Spectators = game.Spectators
                    .Where(s => s.ConnectionId != null)
                    .Select(s => new SpectatorDto { Id = s.Id, Name = s.Name }).ToList(),
                IAmSpectator = isSpectator,
                Results = game.Status == GameStatus.Finished ? game.LastResults : null,
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
                    TurnTimedOut = p.TurnTimedOut,
                    HasLeft = p.HasLeft,
                    Hand = (forPlayerId == p.Id || game.Status == GameStatus.Revealing || game.Status == GameStatus.Finished)
                        ? p.Hand.Select(c => new CardDto { Id = c.Id, Letter = c.Letter, Figure = c.Figure, Color = c.Color }).ToList()
                        : [],
                    Play = (forPlayerId == p.Id && game.Status == GameStatus.Playing && p.Hand.Count > 0)
                        ? scoringService.EvaluateHand(p.Id, p.Name, p.Hand)
                        : null
                }).ToList()
            };
        }
    }

    // Un estado por conexion, armado dentro del cerrojo: recorrer
    // game.Players fuera de el podia romper si alguien entraba o salia a la vez.
    public List<(string connectionId, GameStateDto state)> PersonalizedStates(GameState game)
    {
        lock (game.SyncLock)
        {
            var list = new List<(string, GameStateDto)>();
            foreach (var p in game.Players)
                if (p.ConnectionId != null)
                    list.Add((p.ConnectionId, ToDto(game, p.Id)));

            var watching = game.Spectators.Where(s => s.ConnectionId != null).ToList();
            if (watching.Count > 0)
            {
                var dto = ToDto(game, forPlayerId: null, isSpectator: true);
                foreach (var s in watching)
                    list.Add((s.ConnectionId!, dto));
            }
            return list;
        }
    }

    public string? ConnectionOf(GameState game, Guid playerId)
    {
        lock (game.SyncLock)
            return game.Players.FirstOrDefault(p => p.Id == playerId)?.ConnectionId;
    }

    private void StartGameLocked(GameState game)
    {
        game.Deck = deckService.GenerateDeck(game.N);
        game.DiscardPile = [];
        game.LastResults = null;

        foreach (var player in game.Players)
        {
            var (drawn, remaining) = deckService.Draw(game.Deck, game.DiscardPile, HandSize);
            player.Hand = drawn;
            game.Deck = remaining;
            player.TurnStatus = PlayerTurnStatus.Thinking;
            player.Discarded = [];
            player.WantsToContinue = false;
            player.TurnTimedOut = false;
        }

        game.CurrentTurnIndex = game.StartPlayerIndex;
        game.Status = GameStatus.Playing;
        game.LastActivityAt = DateTimeOffset.UtcNow;

        logger.LogInformation("Game {GameId} '{GameName}' round {Round} started with {Count} players",
            game.Id, game.Name, game.RoundNumber, game.Players.Count);

        _ = persistence.SaveAsync(game);
    }

    private void StartNewRoundLocked(GameState game)
    {
        game.StartPlayerIndex = (game.StartPlayerIndex + 1) % game.Players.Count;
        game.RoundNumber++;
        StartGameLocked(game);
    }

    private static GameSummaryDto ToSummary(GameState g) => new()
    {
        Id = g.Id,
        Name = g.Name,
        Status = g.Status,
        PlayerCount = g.Players.Count,
        MaxPlayers = g.MaxPlayers,
        CreatorName = g.Players.FirstOrDefault(p => p.Id == g.CreatorId)?.Name ?? g.Players.FirstOrDefault()?.Name ?? "",
        IsPrivate = g.IsPrivate
    };
}
