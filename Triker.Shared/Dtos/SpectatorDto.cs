namespace Triker.Shared.Dtos;

public class SpectatorDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
}

public class SpectateGameRequest
{
    public string PlayerName { get; set; } = "";
    public string? AccessCode { get; set; }
}

public class SpectateResponse
{
    public Guid SpectatorId { get; set; }
    public GameStateDto GameState { get; set; } = null!;
}
