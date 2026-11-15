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
        foreach (var (connectionId, state) in gameService.PersonalizedStates(game))
            await hub.Clients.Client(connectionId).SendAsync("GameState", state);
    }

    // El grupo de la partida solo tiene conexiones que pasaron por
    // GameHub.Entrar con su token: jugadores y espectadores de esa partida.
    public async Task SendHandsRevealed(Guid gameId, List<HandResultDto> results)
        => await hub.Clients.Group(gameId.ToString()).SendAsync("HandsRevealed", results);

    public async Task SendAutoLeft(string connectionId)
        => await hub.Clients.Client(connectionId).SendAsync("AutoLeft", true);

    public async Task SendTurnTimedOut(string connectionId)
        => await hub.Clients.Client(connectionId).SendAsync("TurnTimedOut", true);

    public async Task BroadcastLobbyUpdate()
        => await hub.Clients.Group("lobby").SendAsync("GamesUpdated", gameService.ListGames());

    public async Task SendReaction(Guid gameId, ChatReactionDto reaction)
        => await hub.Clients.Group(gameId.ToString()).SendAsync("Reaction", reaction);
}
