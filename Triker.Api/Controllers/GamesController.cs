using Microsoft.AspNetCore.Mvc;
using Triker.Api.Services;
using Triker.Shared.Dtos;

namespace Triker.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GamesController(GameService gameService, InactivityService inactivity, GameBroadcaster broadcaster) : ControllerBase
{
    [HttpGet]
    public ActionResult<List<GameSummaryDto>> List()
        => gameService.ListGames();

    [HttpPost]
    public async Task<ActionResult<JoinResponse>> Create([FromBody] CreateGameRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.PlayerName) || string.IsNullOrWhiteSpace(req.GameName))
            return BadRequest("Nombre requerido");

        var (game, player) = gameService.CreateGame(req.GameName, req.PlayerName, req.TargetWins, req.IsPrivate);
        await broadcaster.BroadcastLobbyUpdate();
        return new JoinResponse { PlayerId = player.Id, GameState = gameService.ToDto(game, player.Id) };
    }

    [HttpPost("{gameId}/join")]
    public async Task<ActionResult<JoinResponse>> Join(Guid gameId, [FromBody] JoinGameRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.PlayerName))
            return BadRequest("Nombre requerido");

        var (result, game, player) = gameService.JoinGame(gameId, req.PlayerName, req.AccessCode);
        switch (result)
        {
            case GameService.JoinResult.Ok: break;
            case GameService.JoinResult.WrongCode: return StatusCode(403, "Código incorrecto");
            case GameService.JoinResult.Full:      return BadRequest("Partida llena");
            case GameService.JoinResult.NotWaiting:return BadRequest("Partida ya iniciada");
            default:                               return BadRequest("No se puede unir a la partida");
        }

        await inactivity.BroadcastState(gameId);
        await broadcaster.BroadcastLobbyUpdate();

        return new JoinResponse { PlayerId = player!.Id, GameState = gameService.ToDto(game!, player.Id) };
    }

    [HttpPost("{gameId}/addbot")]
    public async Task<ActionResult> AddBot(Guid gameId, [FromQuery] Guid playerId)
    {
        var game = gameService.AddBot(gameId, playerId);
        if (game == null) return BadRequest("No se puede añadir un bot");
        await inactivity.BroadcastState(gameId);
        await broadcaster.BroadcastLobbyUpdate();
        return Ok();
    }

    [HttpPost("{gameId}/start")]
    public async Task<ActionResult> Start(Guid gameId, [FromQuery] Guid playerId)
    {
        if (!gameService.StartGame(gameId, playerId))
            return BadRequest("No se puede iniciar la partida");

        await inactivity.ProcessBotsAndBroadcast(gameId);
        return Ok();
    }

    [HttpGet("{gameId}")]
    public ActionResult<GameStateDto> GetState(Guid gameId, [FromQuery] Guid playerId)
    {
        var game = gameService.GetGame(gameId);
        if (game == null) return NotFound();
        return gameService.ToDto(game, playerId);
    }

    [HttpPost("{gameId}/discard")]
    public async Task<ActionResult> Discard(Guid gameId, [FromQuery] Guid playerId, [FromBody] DiscardRequest req)
    {
        // Cancel the inactivity timer before accepting the player's move
        inactivity.CancelTurnTimer(gameId);

        if (!gameService.SubmitDiscard(gameId, playerId, req.CardIds))
            return BadRequest("No se puede descartar ahora");

        await inactivity.ProcessBotsAndBroadcast(gameId);
        return Ok();
    }

    [HttpPost("{gameId}/continue")]
    public async Task<ActionResult> Continue(Guid gameId, [FromQuery] Guid playerId, [FromBody] ContinueRequest req)
    {
        if (!gameService.SetContinue(gameId, playerId, req.WantsToContinue))
            return BadRequest();

        await inactivity.ProcessBotsAndBroadcast(gameId);
        return Ok();
    }

    [HttpPost("{gameId}/react")]
    public async Task<ActionResult> React(Guid gameId, [FromQuery] Guid playerId, [FromBody] SendReactionRequest req)
    {
        var reaction = gameService.SendReaction(gameId, playerId, req.Emoji ?? "");
        if (reaction == null) return Ok(); // Silenciar errores (rate-limit, emoji inválido…) para no spamear al cliente
        await broadcaster.SendReaction(gameId, reaction);
        return Ok();
    }

    [HttpPost("{gameId}/spectate")]
    public async Task<ActionResult<SpectateResponse>> Spectate(Guid gameId, [FromBody] SpectateGameRequest req)
    {
        var (result, game, spectator) = gameService.AddSpectator(gameId, req.PlayerName ?? "", req.AccessCode);
        switch (result)
        {
            case GameService.SpectateResult.Ok: break;
            case GameService.SpectateResult.WrongCode: return StatusCode(403, "Código incorrecto");
            case GameService.SpectateResult.GameOver:  return BadRequest("La partida ha terminado");
            default:                                   return BadRequest("No se puede ver la partida");
        }

        await broadcaster.BroadcastState(gameId);

        return new SpectateResponse
        {
            SpectatorId = spectator!.Id,
            GameState = gameService.ToDto(game!, forPlayerId: null, isSpectator: true)
        };
    }

    [HttpPost("{gameId}/spectate/leave")]
    public async Task<ActionResult> LeaveSpectate(Guid gameId, [FromQuery] Guid spectatorId)
    {
        if (!gameService.RemoveSpectator(gameId, spectatorId)) return Ok();
        await broadcaster.BroadcastState(gameId);
        return Ok();
    }
}
