using Microsoft.AspNetCore.SignalR;
using Triker.Api.Services;

namespace Triker.Api.Hubs;

public class GameHub(GameService gameService, GameBroadcaster broadcaster) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var query = Context.GetHttpContext()?.Request.Query;
        if (query != null && Guid.TryParse(query["gameId"], out var gameId))
        {
            if (Guid.TryParse(query["playerId"], out var playerId))
            {
                gameService.UpdateConnectionId(gameId, playerId, Context.ConnectionId);
                await Groups.AddToGroupAsync(Context.ConnectionId, gameId.ToString());

                var game = gameService.GetGame(gameId);
                if (game != null)
                    await Clients.Caller.SendAsync("GameState", gameService.ToDto(game, playerId));
            }
            else if (Guid.TryParse(query["spectatorId"], out var spectatorId))
            {
                gameService.UpdateSpectatorConnectionId(gameId, spectatorId, Context.ConnectionId);
                await Groups.AddToGroupAsync(Context.ConnectionId, gameId.ToString());

                var game = gameService.GetGame(gameId);
                if (game != null)
                    await Clients.Caller.SendAsync("GameState", gameService.ToDto(game, forPlayerId: null, isSpectator: true));
            }
            else
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, "lobby");
                await Clients.Caller.SendAsync("GamesUpdated", gameService.ListGames());
            }
        }
        else
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "lobby");
            await Clients.Caller.SendAsync("GamesUpdated", gameService.ListGames());
        }
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var gameId = gameService.RemoveSpectatorByConnection(Context.ConnectionId);
        if (gameId.HasValue)
            await broadcaster.BroadcastState(gameId.Value);
        await base.OnDisconnectedAsync(exception);
    }
}
