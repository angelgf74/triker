using Triker.Shared.Models;

namespace Triker.Shared.Dtos;

public class GameStateDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public GameStatus Status { get; set; }
    public List<PlayerDto> Players { get; set; } = [];
    public int DeckSize { get; set; }
    public int CurrentTurnIndex { get; set; }
    public Guid CreatorId { get; set; }
    public int N { get; set; }
    public int RoundNumber { get; set; }
    public Guid? MyPlayerId { get; set; }
    public DateTimeOffset? TurnDeadline { get; set; }
}
