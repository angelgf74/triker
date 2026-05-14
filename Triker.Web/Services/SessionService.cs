using Microsoft.JSInterop;

namespace Triker.Web.Services;

public class SessionService(IJSRuntime js)
{
    private Guid? _playerId;

    public async Task<Guid> GetOrCreatePlayerId()
    {
        if (_playerId.HasValue) return _playerId.Value;

        var stored = await js.InvokeAsync<string?>("localStorage.getItem", "triker_player_id");
        if (Guid.TryParse(stored, out var id))
        {
            _playerId = id;
            return id;
        }

        id = Guid.NewGuid();
        _playerId = id;
        await js.InvokeVoidAsync("localStorage.setItem", "triker_player_id", id.ToString());
        return id;
    }

    public async Task<string?> GetPlayerName()
        => await js.InvokeAsync<string?>("localStorage.getItem", "triker_player_name");

    public async Task SetPlayerName(string name)
        => await js.InvokeVoidAsync("localStorage.setItem", "triker_player_name", name);
}
