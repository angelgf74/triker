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
    public int TargetWins { get; set; }
    public bool GameOver { get; set; }
    public bool IsPrivate { get; set; }
    public string? AccessCode { get; set; }
    public List<SpectatorDto> Spectators { get; set; } = [];
    public bool IAmSpectator { get; set; }

    // Resultados de la ronda, mientras la partida esta en Finished. Antes solo
    // llegaban en el evento HandsRevealed: quien recargaba o reconectaba en esa
    // pantalla se quedaba sin ellos y sin botones hasta que le echaba el
    // temporizador de 5 minutos.
    public List<HandResultDto>? Results { get; set; }
}
