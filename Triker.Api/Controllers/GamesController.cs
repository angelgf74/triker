using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Triker.Api.Services;
using Triker.Api.State;
using Triker.Shared.Dtos;
using Triker.Shared.Models;

namespace Triker.Api.Controllers;

// Identidad: el playerId/spectatorId de la query dice que asiento; el token de
// la cabecera X-Triker-Token prueba que es de quien llama. Un asiento que no es
// tuyo responde 404, igual que una partida que no existe.
[ApiController]
[Route("api/[controller]")]
public class GamesController(GameService gameService, InactivityService inactivity, GameBroadcaster broadcaster) : ControllerBase
{
    private string? Token => Request.Headers[ApiHeaders.Token].FirstOrDefault();

    private GameState? GameForPlayer(Guid gameId, Guid playerId)
    {
        var game = gameService.GetGame(gameId);
        return game != null && gameService.IsPlayer(game, playerId, Token) ? game : null;
    }

    [HttpGet]
    public ActionResult<List<GameSummaryDto>> List()
        => gameService.ListGames();

    [HttpPost]
    [EnableRateLimiting(RateLimits.Crear)]
    public async Task<ActionResult<JoinResponse>> Create([FromBody] CreateGameRequest req)
    {
        var playerName = Limites.Nombre(req.PlayerName, Limites.NombreJugador);
        var gameName = Limites.Nombre(req.GameName, Limites.NombrePartida);
        if (playerName == null || gameName == null)
            return BadRequest($"Nombre no válido: hasta {Limites.NombreJugador} caracteres el tuyo y {Limites.NombrePartida} el de la partida");

        var created = gameService.CreateGame(gameName, playerName, req.TargetWins, req.IsPrivate, req.Bots);
        if (created is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "Hay demasiadas partidas abiertas. Prueba dentro de un rato.");
        var (game, player) = created.Value;

        // Creada con bots ya esta en juego: hace falta lo mismo que tras
        // /start (temporizador del turno, que es del creador, y difundir).
        if (game.Status == GameStatus.Playing)
            await inactivity.ProcessBotsAndBroadcast(game.Id);
        else
            await broadcaster.BroadcastLobbyUpdate();
        return new JoinResponse { PlayerId = player.Id, PlayerToken = player.Token!, GameState = gameService.ToDto(game, player.Id) };
    }

    [HttpPost("{gameId}/join")]
    [EnableRateLimiting(RateLimits.Entrar)]
    public async Task<ActionResult<JoinResponse>> Join(Guid gameId, [FromBody] JoinGameRequest req)
    {
        var playerName = Limites.Nombre(req.PlayerName, Limites.NombreJugador);
        if (playerName == null)
            return BadRequest($"Nombre no válido: hasta {Limites.NombreJugador} caracteres");

        var (result, game, player) = gameService.JoinGame(gameId, playerName, req.AccessCode);
        switch (result)
        {
            case GameService.JoinResult.Ok: break;
            case GameService.JoinResult.WrongCode:  return StatusCode(403, "Código incorrecto");
            case GameService.JoinResult.Full:       return BadRequest("Partida llena");
            case GameService.JoinResult.NotWaiting: return BadRequest("Partida ya iniciada");
            case GameService.JoinResult.NotFound:   return NotFound("La partida ya no existe");
            default:                                return BadRequest("No se puede unir a la partida");
        }

        // Si con este jugador se lleno la mesa, la partida arranco sola: hace
        // falta lo mismo que tras /start (temporizador y bots), no solo difundir.
        if (game!.Status == GameStatus.Playing)
            await inactivity.ProcessBotsAndBroadcast(gameId);
        else
            await inactivity.BroadcastState(gameId);
        await broadcaster.BroadcastLobbyUpdate();

        return new JoinResponse { PlayerId = player!.Id, PlayerToken = player.Token!, GameState = gameService.ToDto(game, player.Id) };
    }

    [HttpPost("{gameId}/addbot")]
    public async Task<ActionResult> AddBot(Guid gameId, [FromQuery] Guid playerId)
    {
        if (GameForPlayer(gameId, playerId) == null) return NotFound();
        if (gameService.AddBot(gameId, playerId) == null) return BadRequest("No se puede añadir un bot");
        await inactivity.BroadcastState(gameId);
        await broadcaster.BroadcastLobbyUpdate();
        return Ok();
    }

    [HttpPost("{gameId}/start")]
    public async Task<ActionResult> Start(Guid gameId, [FromQuery] Guid playerId)
    {
        if (GameForPlayer(gameId, playerId) == null) return NotFound();
        if (!gameService.StartGame(gameId, playerId))
            return BadRequest("No se puede iniciar la partida");

        await inactivity.ProcessBotsAndBroadcast(gameId);
        return Ok();
    }

    [HttpGet("{gameId}")]
    public ActionResult<GameStateDto> GetState(Guid gameId, [FromQuery] Guid? playerId, [FromQuery] Guid? spectatorId)
    {
        var game = gameService.GetGame(gameId);
        if (game == null) return NotFound();

        if (playerId is Guid pid && gameService.IsPlayer(game, pid, Token))
            return gameService.ToDto(game, pid);
        if (spectatorId is Guid sid && gameService.IsSpectator(game, sid, Token))
            return gameService.ToDto(game, forPlayerId: null, isSpectator: true);
        return NotFound();
    }

    [HttpPost("{gameId}/discard")]
    public async Task<ActionResult> Discard(Guid gameId, [FromQuery] Guid playerId, [FromBody] DiscardRequest req)
    {
        if (GameForPlayer(gameId, playerId) == null) return NotFound();
        if (!gameService.SubmitDiscard(gameId, playerId, req.CardIds))
            return BadRequest("No se puede descartar ahora");

        // Despues de validar y solo el de este jugador (ver InactivityService).
        inactivity.CancelTurnTimer(gameId, playerId);
        await inactivity.ProcessBotsAndBroadcast(gameId);
        return Ok();
    }

    [HttpPost("{gameId}/continue")]
    public async Task<ActionResult> Continue(Guid gameId, [FromQuery] Guid playerId, [FromBody] ContinueRequest req)
    {
        if (GameForPlayer(gameId, playerId) == null) return NotFound();
        if (!gameService.SetContinue(gameId, playerId, req.WantsToContinue))
            return BadRequest();

        await inactivity.ProcessBotsAndBroadcast(gameId);
        return Ok();
    }

    // Salir de la mesa en cualquier momento (ver GameService.LeaveGame). Responde
    // en cuanto el jugador esta fuera; lo que venga despues (bots, revelar,
    // avisar a los demas) sigue en segundo plano.
    [HttpPost("{gameId}/leave")]
    public ActionResult Leave(Guid gameId, [FromQuery] Guid playerId)
    {
        if (GameForPlayer(gameId, playerId) == null) return NotFound();
        if (!gameService.LeaveGame(gameId, playerId)) return BadRequest("No se puede salir ahora");

        // Si era su turno, el temporizador que le vigilaba ya no hace falta.
        inactivity.CancelTurnTimer(gameId, playerId);
        inactivity.ProcessBotsAndBroadcastInBackground(gameId);
        return Ok();
    }

    [HttpPost("{gameId}/react")]
    public async Task<ActionResult> React(Guid gameId, [FromQuery] Guid playerId, [FromBody] SendReactionRequest req)
    {
        if (GameForPlayer(gameId, playerId) == null) return NotFound();
        var reaction = gameService.SendReaction(gameId, playerId, req.Emoji ?? "");
        if (reaction == null) return Ok(); // Silenciar errores (rate-limit, emoji inválido…) para no spamear al cliente
        await broadcaster.SendReaction(gameId, reaction);
        return Ok();
    }

    [HttpPost("{gameId}/spectate")]
    [EnableRateLimiting(RateLimits.Entrar)]
    public async Task<ActionResult<SpectateResponse>> Spectate(Guid gameId, [FromBody] SpectateGameRequest req)
    {
        var name = string.IsNullOrWhiteSpace(req.PlayerName)
            ? "Espectador"
            : Limites.Nombre(req.PlayerName, Limites.NombreJugador);
        if (name == null)
            return BadRequest($"Nombre no válido: hasta {Limites.NombreJugador} caracteres");

        var (result, game, spectator) = gameService.AddSpectator(gameId, name, req.AccessCode);
        switch (result)
        {
            case GameService.SpectateResult.Ok: break;
            case GameService.SpectateResult.WrongCode: return StatusCode(403, "Código incorrecto");
            case GameService.SpectateResult.GameOver:  return BadRequest("La partida ha terminado");
            case GameService.SpectateResult.Full:      return BadRequest("La partida ya tiene demasiados espectadores");
            case GameService.SpectateResult.NotFound:  return NotFound("La partida ya no existe");
            default:                                   return BadRequest("No se puede ver la partida");
        }

        return new SpectateResponse
        {
            SpectatorId = spectator!.Id,
            SpectatorToken = spectator.Token!,
            GameState = gameService.ToDto(game!, forPlayerId: null, isSpectator: true)
        };
    }

    [HttpPost("{gameId}/spectate/leave")]
    public async Task<ActionResult> LeaveSpectate(Guid gameId, [FromQuery] Guid spectatorId)
    {
        var game = gameService.GetGame(gameId);
        if (game == null || !gameService.IsSpectator(game, spectatorId, Token)) return NotFound();
        if (gameService.RemoveSpectator(gameId, spectatorId))
            await broadcaster.BroadcastState(gameId);
        return Ok();
    }
}
