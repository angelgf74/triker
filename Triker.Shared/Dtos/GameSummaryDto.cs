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
    public bool IsPrivate { get; set; }
    // Sala de espera que lleva un rato sin ningun jugador conectado. Sigue
    // viva (la invitacion y el asiento valen), pero el lobby no la lista: quien
    // entrara esperaria a un creador que quiza no vuelva.
    public bool Unattended { get; set; }
}
