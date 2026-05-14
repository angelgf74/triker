using System.Text.Json.Serialization;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Triker.Shared.Dtos;
using Triker.Shared.Models;

namespace Triker.Web.Services;

public class HubService : IAsyncDisposable
{
    private HubConnection? _hub;
    public GameStateDto? GameState { get; private set; }
    public List<HandResultDto>? LastResults { get; private set; }

    public event Action? OnChange;
    public event Action<List<HandResultDto>>? OnHandsRevealed;

    public bool IsConnected => _hub?.State == HubConnectionState.Connected;

    public async Task Connect(string apiBaseUrl, Guid gameId, Guid playerId)
    {
        if (_hub != null) await DisposeAsync();

        _hub = new HubConnectionBuilder()
            .WithUrl($"{apiBaseUrl}/gamehub?gameId={gameId}&playerId={playerId}")
            .WithAutomaticReconnect()
            .AddJsonProtocol(options =>
            {
                options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            })
            .Build();

        // Personalized state: always apply (includes the player's own hand)
        _hub.On<GameStateDto>("GameState", state =>
        {
            GameState = state;
            OnChange?.Invoke();
        });

        // Public state (no hand data): only use it during waiting room or initial load.
        // During Playing/Revealing/Finished the personalized "GameState" is authoritative.
        _hub.On<GameStateDto>("GameStatePublic", state =>
        {
            if (state.Status == GameStatus.Waiting || GameState == null)
            {
                GameState = state;
                OnChange?.Invoke();
            }
        });

        _hub.On<List<HandResultDto>>("HandsRevealed", results =>
        {
            LastResults = results;
            OnHandsRevealed?.Invoke(results);
            OnChange?.Invoke();
        });

        await _hub.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_hub != null)
        {
            await _hub.DisposeAsync();
            _hub = null;
        }
    }
}
