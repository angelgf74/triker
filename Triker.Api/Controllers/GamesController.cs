using Microsoft.AspNetCore.Mvc;
using Triker.Api.Services;
using Triker.Shared.Dtos;

namespace Triker.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GamesController(GameService gameService, InactivityService inactivity) : ControllerBase
{
    [HttpGet]
    public ActionResult<List<GameSummaryDto>> List()
        => gameService.ListGames();

    [HttpPost]
    public ActionResult<JoinResponse> Create([FromBody] CreateGameRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.PlayerName) || string.IsNullOrWhiteSpace(req.GameName))
            return BadRequest("Nombre requerido");

        var (game, player) = gameService.CreateGame(req.GameName, req.PlayerName);
        return new JoinResponse { PlayerId = player.Id, GameState = gameService.ToDto(game, player.Id) };
    }

    [HttpPost("{gameId}/join")]
    public async Task<ActionResult<JoinResponse>> Join(Guid gameId, [FromBody] JoinGameRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.PlayerName))
            return BadRequest("Nombre requerido");

        var result = gameService.JoinGame(gameId, req.PlayerName);
        if (result == null) return BadRequest("No se puede unir a la partida");

        var (game, player) = result.Value;
        await inactivity.BroadcastState(gameId);

        return new JoinResponse { PlayerId = player.Id, GameState = gameService.ToDto(game, player.Id) };
    }

    [HttpPost("{gameId}/addbot")]
    public async Task<ActionResult> AddBot(Guid gameId, [FromQuery] Guid playerId)
    {
        var game = gameService.AddBot(gameId, playerId);
        if (game == null) return BadRequest("No se puede añadir un bot");
        await inactivity.BroadcastState(gameId);
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
}
