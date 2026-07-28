using Triker.Shared.Models;

namespace Triker.Shared.Dtos;

public class PlayerDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public int HandSize { get; set; }
    public int DiscardCount { get; set; }
    public PlayerTurnStatus TurnStatus { get; set; }
    public bool IsCurrentTurn { get; set; }
    public bool WantsToContinue { get; set; }
    public bool IsBot { get; set; }
    public int Wins { get; set; }
    public int Seconds { get; set; }
    public int Thirds { get; set; }
    // El turno se paso solo al agotarse el tiempo, no lo decidio el jugador.
    public bool TurnTimedOut { get; set; }
    public List<CardDto> Hand { get; set; } = [];

    // Jugada evaluada de la mano. El servidor solo la rellena para el propio
    // jugador durante la partida (para mostrar su mejor jugada en curso).
    public HandResultDto? Play { get; set; }
}
