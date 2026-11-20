using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Triker.Shared.Dtos;

namespace Triker.Web.Services;

public enum ResultadoEstado { Ok, NoEncontrada, SinConexion }

// Llamadas a la API. Ninguna lanza por un fallo de red: antes una API caida
// tumbaba el componente con la barra roja generica de Blazor y el lobby decia
// "No hay partidas disponibles. ¡Crea una!".
public class ApiService(HttpClient http, JsonSerializerOptions json)
{
    public const string SinConexion = "No se puede conectar con el servidor. Comprueba tu conexión.";

    public async Task<List<GameSummaryDto>?> ListGames()
    {
        var res = await Enviar(new HttpRequestMessage(HttpMethod.Get, "api/games"));
        if (res is not { IsSuccessStatusCode: true }) return null;
        return await res.Content.ReadFromJsonAsync<List<GameSummaryDto>>(json);
    }

    public Task<(JoinResponse? response, string? error)> CreateGame(CreateGameRequest req)
        => Entrar<JoinResponse>("api/games", req, "No se pudo crear la partida");

    public Task<(JoinResponse? response, string? error)> JoinGame(Guid gameId, JoinGameRequest req)
        => Entrar<JoinResponse>($"api/games/{gameId}/join", req, "No se puede unir a la partida");

    public Task<(SpectateResponse? response, string? error)> Spectate(Guid gameId, SpectateGameRequest req)
        => Entrar<SpectateResponse>($"api/games/{gameId}/spectate", req, "No se puede ver la partida");

    public async Task<(GameStateDto? state, ResultadoEstado resultado)> GetGameState(Asiento asiento)
    {
        var quien = asiento.Espectador ? $"spectatorId={asiento.Id}" : $"playerId={asiento.Id}";
        var res = await Enviar(Peticion(HttpMethod.Get, $"api/games/{asiento.GameId}?{quien}", asiento));
        if (res == null) return (null, ResultadoEstado.SinConexion);
        if (res.StatusCode == HttpStatusCode.NotFound) return (null, ResultadoEstado.NoEncontrada);
        if (!res.IsSuccessStatusCode) return (null, ResultadoEstado.SinConexion);
        return (await res.Content.ReadFromJsonAsync<GameStateDto>(json), ResultadoEstado.Ok);
    }

    public Task<bool> AddBot(Asiento a)
        => Ok(Peticion(HttpMethod.Post, $"api/games/{a.GameId}/addbot?playerId={a.Id}", a));

    public Task<bool> StartGame(Asiento a)
        => Ok(Peticion(HttpMethod.Post, $"api/games/{a.GameId}/start?playerId={a.Id}", a));

    public Task<bool> Discard(Asiento a, DiscardRequest req)
        => Ok(Peticion(HttpMethod.Post, $"api/games/{a.GameId}/discard?playerId={a.Id}", a, req));

    public Task<bool> Continue(Asiento a, ContinueRequest req)
        => Ok(Peticion(HttpMethod.Post, $"api/games/{a.GameId}/continue?playerId={a.Id}", a, req));

    // Un 404 tambien cuenta como fuera: el asiento ya no existe (la peticion
    // anterior llego aunque su respuesta se perdiera, o la partida se cerro).
    public async Task<bool> Leave(Asiento a)
    {
        var res = await Enviar(Peticion(HttpMethod.Post, $"api/games/{a.GameId}/leave?playerId={a.Id}", a));
        return res is { IsSuccessStatusCode: true } or { StatusCode: HttpStatusCode.NotFound };
    }

    public Task<bool> SendReaction(Asiento a, string emoji)
        => Ok(Peticion(HttpMethod.Post, $"api/games/{a.GameId}/react?playerId={a.Id}", a, new SendReactionRequest { Emoji = emoji }));

    public Task<bool> LeaveSpectate(Asiento a)
        => Ok(Peticion(HttpMethod.Post, $"api/games/{a.GameId}/spectate/leave?spectatorId={a.Id}", a));

    // ── internos ────────────────────────────────────────────────────────────

    private HttpRequestMessage Peticion(HttpMethod metodo, string url, Asiento asiento, object? cuerpo = null)
    {
        var req = new HttpRequestMessage(metodo, url);
        req.Headers.Add(ApiHeaders.Token, asiento.Token);
        if (cuerpo != null) req.Content = JsonContent.Create(cuerpo, cuerpo.GetType(), options: json);
        return req;
    }

    private async Task<HttpResponseMessage?> Enviar(HttpRequestMessage req)
    {
        try { return await http.SendAsync(req); }
        catch (HttpRequestException) { return null; }
        catch (TaskCanceledException) { return null; }
    }

    private async Task<bool> Ok(HttpRequestMessage req)
        => (await Enviar(req))?.IsSuccessStatusCode == true;

    private async Task<(T? response, string? error)> Entrar<T>(string url, object cuerpo, string porDefecto) where T : class
    {
        var res = await Enviar(new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(cuerpo, cuerpo.GetType(), options: json) });
        if (res == null) return (null, SinConexion);
        if (res.IsSuccessStatusCode) return (await res.Content.ReadFromJsonAsync<T>(json), null);
        return (null, await Mensaje(res, porDefecto));
    }

    // La API responde texto en español para los errores previstos; un JSON
    // (ProblemDetails de validacion) o un cuerpo vacio se cambian por el
    // mensaje de la accion.
    private static async Task<string> Mensaje(HttpResponseMessage res, string porDefecto)
    {
        var texto = (await res.Content.ReadAsStringAsync()).Trim();
        if (texto.Length == 0 || texto.StartsWith('{') || texto.Length > 200) return porDefecto;
        return texto;
    }
}
