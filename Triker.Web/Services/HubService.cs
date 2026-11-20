using System.Text.Json.Serialization;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Triker.Shared.Dtos;

namespace Triker.Web.Services;

public class HubService : IAsyncDisposable
{
    private HubConnection? _hub;
    private Asiento? _asiento;

    public GameStateDto? GameState { get; private set; }

    public event Action? OnChange;
    public event Action<List<HandResultDto>>? OnHandsRevealed;
    public event Action? OnAutoLeft;
    public event Action? OnTurnTimedOut;
    public event Action? OnReconnecting;
    public event Action? OnReconnected;
    public event Action? OnConnectionClosed;
    // El servidor ya no reconoce el asiento (partida cerrada o te sacaron).
    public event Action? OnSeatLost;
    public event Action<ChatReactionDto>? OnReaction;

    public bool IsConnected => _hub?.State == HubConnectionState.Connected;

    // Conecta y se identifica con GameHub.Entrar. False si no se pudo conectar
    // o el asiento no vale.
    public async Task<bool> Connect(string apiBaseUrl, Asiento asiento)
    {
        if (_hub != null) await DisposeAsync();
        _asiento = asiento;

        _hub = new HubConnectionBuilder()
            .WithUrl($"{apiBaseUrl.TrimEnd('/')}/gamehub?gameId={asiento.GameId}")
            .WithAutomaticReconnect()
            .AddJsonProtocol(options =>
            {
                options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            })
            .Build();

        _hub.On<GameStateDto>("GameState", state =>
        {
            GameState = state;
            OnChange?.Invoke();
        });

        // Sin OnChange: aqui no llega ningun estado nuevo. Lanzarlo hacia que
        // la sala reaplicara el ultimo que tenia, que si la ronda la cerro un
        // humano es anterior a su jugada (aun "en juego"), y eso apagaba el
        // aviso de turno agotado recien encendido. El estado ya revelado
        // llega justo despues por GameState.
        _hub.On<List<HandResultDto>>("HandsRevealed", results => OnHandsRevealed?.Invoke(results));

        _hub.On<bool>("AutoLeft", _ => OnAutoLeft?.Invoke());

        _hub.On<bool>("TurnTimedOut", _ => OnTurnTimedOut?.Invoke());

        _hub.On<ChatReactionDto>("Reaction", r => OnReaction?.Invoke(r));

        _hub.Reconnecting += _ => { OnReconnecting?.Invoke(); return Task.CompletedTask; };
        // La conexion nueva tiene otro id: hay que volver a entrar en la partida.
        _hub.Reconnected += async _ =>
        {
            if (await Entrar()) OnReconnected?.Invoke();
            else OnSeatLost?.Invoke();
        };
        _hub.Closed += _ => { OnConnectionClosed?.Invoke(); return Task.CompletedTask; };

        try
        {
            await _hub.StartAsync();
        }
        catch
        {
            return false;
        }
        return await Entrar();
    }

    private async Task<bool> Entrar()
    {
        if (_hub == null || _asiento == null) return false;
        try
        {
            return await _hub.InvokeAsync<bool>("Entrar", _asiento.GameId, _asiento.Id, _asiento.Token, _asiento.Espectador);
        }
        catch
        {
            return false;
        }
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
