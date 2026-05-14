using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Triker.Api.Hubs;
using Triker.Api.Services;
using Triker.Api.State;
using Triker.Shared.Dtos;
using Triker.Shared.Models;

namespace Triker.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GamesController(GameService gameService, IHubContext<GameHub> hub) : ControllerBase
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

        // Notify all connected players of the new player list
        await BroadcastPersonalized(gameId, game);

        return new JoinResponse { PlayerId = player.Id, GameState = gameService.ToDto(game, player.Id) };
    }

    [HttpPost("{gameId}/addbot")]
    public async Task<ActionResult> AddBot(Guid gameId, [FromQuery] Guid playerId)
    {
        var game = gameService.AddBot(gameId, playerId);
        if (game == null) return BadRequest("No se puede añadir un bot");
        await BroadcastPersonalized(gameId, game);
        return Ok();
    }

    [HttpPost("{gameId}/start")]
    public async Task<ActionResult> Start(Guid gameId, [FromQuery] Guid playerId)
    {
        if (!gameService.StartGame(gameId, playerId))
            return BadRequest("No se puede iniciar la partida");

        await ProcessBotsAndBroadcast(gameId);
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
        if (!gameService.SubmitDiscard(gameId, playerId, req.CardIds))
            return BadRequest("No se puede descartar ahora");

        await ProcessBotsAndBroadcast(gameId);
        return Ok();
    }

    [HttpPost("{gameId}/continue")]
    public async Task<ActionResult> Continue(Guid gameId, [FromQuery] Guid playerId, [FromBody] ContinueRequest req)
    {
        if (!gameService.SetContinue(gameId, playerId, req.WantsToContinue))
            return BadRequest();

        await ProcessBotsAndBroadcast(gameId);
        return Ok();
    }

    private async Task ProcessBotsAndBroadcast(Guid gameId)
    {
        var game = gameService.GetGame(gameId)!;
        bool anyBotPlayed = false;

        while (game.Status == GameStatus.Playing && game.CurrentTurnPlayer?.IsBot == true)
        {
            // Mostrar "turno de BotX" antes de que juegue
            await BroadcastPersonalized(gameId, game);
            await Task.Delay(1200);

            gameService.ProcessBotTurn(gameId);
            game = gameService.GetGame(gameId)!;
            anyBotPlayed = true;

            // Mostrar cuántas cartas descartó antes de pasar al siguiente
            await BroadcastPersonalized(gameId, game);

            if (game.Status == GameStatus.Playing && game.CurrentTurnPlayer?.IsBot == true)
                await Task.Delay(900);
        }

        if (game.Status == GameStatus.Revealing)
        {
            if (anyBotPlayed) await Task.Delay(600);
            var results = gameService.GetRevealedHands(gameId);
            await hub.Clients.Group(gameId.ToString()).SendAsync("HandsRevealed", results);
            game = gameService.GetGame(gameId)!;
        }

        await BroadcastPersonalized(gameId, game);
    }

    private async Task BroadcastPersonalized(Guid gameId, GameState game)
    {
        foreach (var player in game.Players)
        {
            if (player.ConnectionId != null)
            {
                var dto = gameService.ToDto(game, player.Id);
                await hub.Clients.Client(player.ConnectionId).SendAsync("GameState", dto);
            }
        }
    }
}
