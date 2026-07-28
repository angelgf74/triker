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
    public event Action? OnAutoLeft;
    public event Action? OnTurnTimedOut;
    public event Action? OnReconnecting;
    public event Action? OnReconnected;
    public event Action? OnConnectionClosed;
    public event Action<ChatReactionDto>? OnReaction;

    public bool IsConnected => _hub?.State == HubConnectionState.Connected;

    public async Task Connect(string apiBaseUrl, Guid gameId, Guid id, bool asSpectator = false)
    {
        if (_hub != null) await DisposeAsync();

        var idParam = asSpectator ? $"spectatorId={id}" : $"playerId={id}";
        _hub = new HubConnectionBuilder()
            .WithUrl($"{apiBaseUrl}/gamehub?gameId={gameId}&{idParam}")
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

        _hub.On<bool>("AutoLeft", _ => OnAutoLeft?.Invoke());

        _hub.On<bool>("TurnTimedOut", _ => OnTurnTimedOut?.Invoke());

        _hub.On<ChatReactionDto>("Reaction", r => OnReaction?.Invoke(r));

        _hub.Reconnecting  += _ => { OnReconnecting?.Invoke();     return Task.CompletedTask; };
        _hub.Reconnected   += _ => { OnReconnected?.Invoke();      return Task.CompletedTask; };
        _hub.Closed        += _ => { OnConnectionClosed?.Invoke(); return Task.CompletedTask; };

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
