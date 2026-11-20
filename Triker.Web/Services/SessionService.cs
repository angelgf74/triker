using System.Text.Json;
using Microsoft.JSInterop;

namespace Triker.Web.Services;

// Un asiento es lo que te deja actuar en una partida: su id (publico) y el
// token secreto que devolvio la API al crear, unirse o ver. Se guardan en
// localStorage, no en la URL: la URL va al historial, a la analitica y a
// quien te la pida para "ver la partida", y quien tuviera el token tendria tu
// sitio en la mesa.
public record Asiento(Guid GameId, Guid Id, string Token, bool Espectador);

public class SessionService(IJSRuntime js)
{
    private const string AsientosKey = "triker_asientos";
    // Las partidas caducan en horas; con esto basta para varias a la vez sin
    // que la lista crezca para siempre.
    private const int MaxAsientos = 30;

    public async Task<string?> GetPlayerName()
        => await js.InvokeAsync<string?>("localStorage.getItem", "triker_player_name");

    public async Task SetPlayerName(string name)
        => await js.InvokeVoidAsync("localStorage.setItem", "triker_player_name", name);

    public async Task GuardarAsiento(Asiento asiento)
    {
        var lista = await Leer();
        lista.RemoveAll(a => a.GameId == asiento.GameId && a.Id == asiento.Id);
        lista.Add(asiento);
        if (lista.Count > MaxAsientos) lista.RemoveRange(0, lista.Count - MaxAsientos);
        await Escribir(lista);
    }

    // Con id: ese asiento. Sin id (enlace a la partida sin ?playerId): el de
    // jugador que tengas en ella o, si solo la mirabas, el de espectador.
    public async Task<Asiento?> BuscarAsiento(Guid gameId, Guid? id, bool espectador)
    {
        var lista = await Leer();
        if (id is Guid i)
            return lista.LastOrDefault(a => a.GameId == gameId && a.Id == i && a.Espectador == espectador);
        return lista.LastOrDefault(a => a.GameId == gameId && !a.Espectador)
            ?? lista.LastOrDefault(a => a.GameId == gameId);
    }

    // Los asientos de jugador que hay guardados, del mas reciente al mas
    // antiguo y uno por partida. Que sigan valiendo solo lo sabe la API.
    public async Task<List<Asiento>> AsientosDeJugador()
    {
        var lista = await Leer();
        lista.Reverse();
        return lista.Where(a => !a.Espectador).DistinctBy(a => a.GameId).ToList();
    }

    public async Task OlvidarAsiento(Guid gameId, Guid id)
    {
        var lista = await Leer();
        if (lista.RemoveAll(a => a.GameId == gameId && a.Id == id) > 0)
            await Escribir(lista);
    }

    private async Task<List<Asiento>> Leer()
    {
        try
        {
            var json = await js.InvokeAsync<string?>("localStorage.getItem", AsientosKey);
            return string.IsNullOrEmpty(json) ? [] : JsonSerializer.Deserialize<List<Asiento>>(json) ?? [];
        }
        catch
        {
            return []; // almacenamiento bloqueado o contenido roto: sin asientos
        }
    }

    private async Task Escribir(List<Asiento> lista)
    {
        try { await js.InvokeVoidAsync("localStorage.setItem", AsientosKey, JsonSerializer.Serialize(lista)); }
        catch { /* sin almacenamiento la partida sigue, pero no se recupera al recargar */ }
    }
}
