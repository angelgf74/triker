using Microsoft.AspNetCore.SignalR;
using Triker.Api.Hubs;
using Triker.Api.State;
using Triker.Shared.Dtos;

namespace Triker.Api.Services;

public class GameBroadcaster(GameService gameService, IHubContext<GameHub> hub)
{
    public async Task BroadcastState(Guid gameId)
    {
        var game = gameService.GetGame(gameId);
        if (game != null)
            await BroadcastPersonalized(game);
    }

    public async Task BroadcastPersonalized(GameState game)
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

    public async Task SendHandsRevealed(Guid gameId, List<HandResultDto> results)
        => await hub.Clients.Group(gameId.ToString()).SendAsync("HandsRevealed", results);

    public async Task SendAutoLeft(string connectionId)
        => await hub.Clients.Client(connectionId).SendAsync("AutoLeft", true);
}
