using System.Net.Http.Json;
using System.Text.Json;
using Triker.Shared.Dtos;

namespace Triker.Web.Services;

public class ApiService(HttpClient http, JsonSerializerOptions json)
{
    public Task<List<GameSummaryDto>?> ListGames()
        => http.GetFromJsonAsync<List<GameSummaryDto>>("api/games", json);

    public async Task<JoinResponse?> CreateGame(CreateGameRequest req)
    {
        var res = await http.PostAsJsonAsync("api/games", req, json);
        return res.IsSuccessStatusCode ? await res.Content.ReadFromJsonAsync<JoinResponse>(json) : null;
    }

    public async Task<(JoinResponse? response, string? error)> JoinGame(Guid gameId, JoinGameRequest req)
    {
        var res = await http.PostAsJsonAsync($"api/games/{gameId}/join", req, json);
        if (res.IsSuccessStatusCode)
            return (await res.Content.ReadFromJsonAsync<JoinResponse>(json), null);

        var msg = await res.Content.ReadAsStringAsync();
        return (null, string.IsNullOrWhiteSpace(msg) ? "No se puede unir" : msg);
    }

    public async Task<bool> AddBot(Guid gameId, Guid playerId)
    {
        var res = await http.PostAsync($"api/games/{gameId}/addbot?playerId={playerId}", null);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> StartGame(Guid gameId, Guid playerId)
    {
        var res = await http.PostAsync($"api/games/{gameId}/start?playerId={playerId}", null);
        return res.IsSuccessStatusCode;
    }

    public async Task<GameStateDto?> GetGameState(Guid gameId, Guid playerId)
        => await http.GetFromJsonAsync<GameStateDto>($"api/games/{gameId}?playerId={playerId}", json);

    public async Task<bool> Discard(Guid gameId, Guid playerId, DiscardRequest req)
    {
        var res = await http.PostAsJsonAsync($"api/games/{gameId}/discard?playerId={playerId}", req, json);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> Continue(Guid gameId, Guid playerId, ContinueRequest req)
    {
        var res = await http.PostAsJsonAsync($"api/games/{gameId}/continue?playerId={playerId}", req, json);
        return res.IsSuccessStatusCode;
    }

    public async Task SendReaction(Guid gameId, Guid playerId, string emoji)
    {
        try { await http.PostAsJsonAsync($"api/games/{gameId}/react?playerId={playerId}", new SendReactionRequest { Emoji = emoji }, json); }
        catch { /* fire-and-forget */ }
    }

    public async Task<(SpectateResponse? response, string? error)> Spectate(Guid gameId, SpectateGameRequest req)
    {
        var res = await http.PostAsJsonAsync($"api/games/{gameId}/spectate", req, json);
        if (res.IsSuccessStatusCode)
            return (await res.Content.ReadFromJsonAsync<SpectateResponse>(json), null);
        var msg = await res.Content.ReadAsStringAsync();
        return (null, string.IsNullOrWhiteSpace(msg) ? "No se puede ver la partida" : msg);
    }

    public async Task LeaveSpectate(Guid gameId, Guid spectatorId)
    {
        try { await http.PostAsync($"api/games/{gameId}/spectate/leave?spectatorId={spectatorId}", null); }
        catch { /* fire-and-forget */ }
    }
}
