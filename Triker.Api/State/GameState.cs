using Triker.Shared.Models;

namespace Triker.Api.State;

public class PlayerState
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public List<Card> Hand { get; set; } = [];
    public List<Card> Discarded { get; set; } = [];
    public PlayerTurnStatus TurnStatus { get; set; } = PlayerTurnStatus.Thinking;
    public bool WantsToContinue { get; set; } = false;
    public bool IsBot { get; set; } = false;
    public int Wins { get; set; } = 0;
    public int Seconds { get; set; } = 0;
    public int Thirds { get; set; } = 0;
    public string? ConnectionId { get; set; }
}

public class GameState
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public GameStatus Status { get; set; } = GameStatus.Waiting;
    public List<PlayerState> Players { get; set; } = [];
    public List<Card> Deck { get; set; } = [];
    public List<Card> DiscardPile { get; set; } = [];
    public int CurrentTurnIndex { get; set; } = 0;
    public int StartPlayerIndex { get; set; } = 0;
    public Guid CreatorId { get; set; }
    public int N { get; set; } = 5;
    public int RoundNumber { get; set; } = 1;

    public int MaxPlayers => (int)Math.Floor((double)(N * N) / 2);
    public PlayerState? CurrentTurnPlayer => Players.Count > 0 ? Players[CurrentTurnIndex % Players.Count] : null;
    public bool AllPlayersReady => Players.Count > 0 && Players.All(p => p.TurnStatus == PlayerTurnStatus.Ready);
}
