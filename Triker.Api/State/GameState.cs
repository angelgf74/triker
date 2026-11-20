using System.Text.Json.Serialization;
using Triker.Shared.Dtos;
using Triker.Shared.Models;

namespace Triker.Api.State;

public enum BotPersonality { Balanced, Conservative, Aggressive }

public class PlayerState
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    // Secreto del asiento (ver ApiHeaders). Se guarda en el snapshot para que
    // sobreviva a un reinicio de la API; nunca sale en un DTO. Null en los bots
    // y en las partidas guardadas antes de que existiera.
    public string? Token { get; set; }
    public List<Card> Hand { get; set; } = [];
    public List<Card> Discarded { get; set; } = [];
    public PlayerTurnStatus TurnStatus { get; set; } = PlayerTurnStatus.Thinking;
    public bool WantsToContinue { get; set; } = false;
    public bool IsBot { get; set; } = false;
    public BotPersonality Personality { get; set; } = BotPersonality.Balanced;
    public int Wins { get; set; } = 0;
    public int Seconds { get; set; } = 0;
    public int Thirds { get; set; } = 0;
    public string? ConnectionId { get; set; }
    public DateTimeOffset LastReactionAt { get; set; } = DateTimeOffset.MinValue;
    // El turno se jugo solo al agotarse el tiempo. Se reinicia cada ronda en
    // StartGame; sin esto el jugador ve "paso" y no sabe que no fue cosa suya.
    public bool TurnTimedOut { get; set; } = false;
    // Salio a mitad de ronda (GameService.LeaveGame): sigue en la lista hasta
    // revelar para no mover el orden de turnos, pero ya no es su asiento.
    public bool HasLeft { get; set; } = false;
}

public class SpectatorState
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string? Token { get; set; }
    public string? ConnectionId { get; set; }
    // Al perder la conexion el espectador no se borra: la reconexion automatica
    // de SignalR (o una recarga) vuelve con el mismo id y token. La limpieza
    // periodica quita a los que llevan un rato sin volver.
    public DateTimeOffset? DisconnectedAt { get; set; }
}

public class GameState
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public GameStatus Status { get; set; } = GameStatus.Waiting;
    public List<PlayerState> Players { get; set; } = [];
    public List<SpectatorState> Spectators { get; set; } = [];
    public List<Card> Deck { get; set; } = [];
    public List<Card> DiscardPile { get; set; } = [];
    public int CurrentTurnIndex { get; set; } = 0;
    public int StartPlayerIndex { get; set; } = 0;
    public Guid CreatorId { get; set; }
    public int N { get; set; } = 8;
    public int RoundNumber { get; set; } = 1;

    public DateTimeOffset? TurnDeadline { get; set; }
    public DateTimeOffset LastActivityAt { get; set; } = DateTimeOffset.UtcNow;
    // Desde cuando no hay ningun jugador humano conectado al hub; null mientras
    // haya alguno. Una sala de espera asi deja de listarse y acaba cerrandose
    // (GameService.CleanupStaleGames). No va al snapshot: las conexiones no
    // sobreviven a un reinicio y se cuenta desde el arranque.
    [JsonIgnore] public DateTimeOffset? EmptySince { get; set; } = DateTimeOffset.UtcNow;
    public int TargetWins { get; set; } = 0;
    public bool GameOver { get; set; } = false;
    public bool IsPrivate { get; set; } = false;
    public string? AccessCode { get; set; }

    // Resultados de la ultima ronda revelada; se borran al repartir. Van en el
    // estado (y en el snapshot) para que quien recarga en la pantalla de
    // resultados la recupere.
    public List<HandResultDto>? LastResults { get; set; }

    // Todo lo que lee o cambia la partida lo hace con este cerrojo: las
    // peticiones HTTP, los temporizadores y el hub llegan en hilos distintos.
    [JsonIgnore] public object SyncLock { get; } = new();
    [JsonIgnore] public int MaxPlayers => Math.Min(16, (int)Math.Floor((double)(N * N) / 2));
    [JsonIgnore] public PlayerState? CurrentTurnPlayer => Players.Count > 0 ? Players[CurrentTurnIndex % Players.Count] : null;
    [JsonIgnore] public bool AllPlayersReady => Players.Count > 0 && Players.All(p => p.TurnStatus == PlayerTurnStatus.Ready);
}
