using Triker.Shared.Models;

namespace Triker.Shared.Dtos;

public class GameSummaryDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public GameStatus Status { get; set; }
    public int PlayerCount { get; set; }
    public int MaxPlayers { get; set; }
    public string CreatorName { get; set; } = "";
}
