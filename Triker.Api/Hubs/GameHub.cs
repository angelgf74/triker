using Microsoft.AspNetCore.SignalR;
using Triker.Api.Services;

namespace Triker.Api.Hubs;

// Dos usos:
//   - /gamehub a secas: el lobby, que solo recibe la lista de partidas.
//   - /gamehub?gameId=...: una partida. La conexion no recibe nada hasta que
//     llama a Entrar con su id y su token. Antes bastaba con poner cualquier
//     playerId en la URL para entrar en el grupo (y ver las manos reveladas
//     de una partida privada) o para quitarle la conexion a otro jugador.
//     El token va como argumento y no en la URL para que no acabe en los logs
//     del proxy ni en el historial.
public class GameHub(GameService gameService, GameBroadcaster broadcaster) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var query = Context.GetHttpContext()?.Request.Query;
        if (query is null || !query.ContainsKey("gameId"))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "lobby");
            await Clients.Caller.SendAsync("GamesUpdated", gameService.ListGames());
        }
        await base.OnConnectedAsync();
    }

    // El cliente la llama al conectar y tras cada reconexion automatica (la
    // conexion nueva tiene otro id). Devuelve false si el asiento no es suyo.
    public async Task<bool> Entrar(Guid gameId, Guid id, string token, bool espectador)
    {
        var game = gameService.GetGame(gameId);
        if (game is null) return false;

        var ok = espectador
            ? gameService.ConnectSpectator(game, id, token, Context.ConnectionId)
            : gameService.ConnectPlayer(game, id, token, Context.ConnectionId);
        if (!ok) return false;

        await Groups.AddToGroupAsync(Context.ConnectionId, gameId.ToString());
        await Clients.Caller.SendAsync("GameState", espectador
            ? gameService.ToDto(game, forPlayerId: null, isSpectator: true)
            : gameService.ToDto(game, id));

        // La lista de espectadores solo cuenta a los conectados.
        if (espectador) await broadcaster.BroadcastState(gameId);
        return true;
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var gameId = gameService.MarkSpectatorDisconnected(Context.ConnectionId);
        if (gameId.HasValue)
            await broadcaster.BroadcastState(gameId.Value);
        await base.OnDisconnectedAsync(exception);
    }
}
