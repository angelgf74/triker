using Microsoft.AspNetCore.SignalR;
using Triker.Api.Services;

namespace Triker.Api.Hubs;

public class GameHub(GameService gameService) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var query = Context.GetHttpContext()?.Request.Query;
        if (query != null
            && Guid.TryParse(query["gameId"], out var gameId)
            && Guid.TryParse(query["playerId"], out var playerId))
        {
            gameService.UpdateConnectionId(gameId, playerId, Context.ConnectionId);
            await Groups.AddToGroupAsync(Context.ConnectionId, gameId.ToString());

            var game = gameService.GetGame(gameId);
            if (game != null)
                await Clients.Caller.SendAsync("GameState", gameService.ToDto(game, playerId));
        }
        else
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "lobby");
            await Clients.Caller.SendAsync("GamesUpdated", gameService.ListGames());
        }
        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
        => base.OnDisconnectedAsync(exception);
}
