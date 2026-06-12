namespace Triker.Shared.Dtos;

public class CreateGameRequest
{
    public string PlayerName { get; set; } = "";
    public string GameName { get; set; } = "";
    public int TargetWins { get; set; } = 0;
    public bool IsPrivate { get; set; } = false;
}

public class JoinGameRequest
{
    public string PlayerName { get; set; } = "";
    public string? AccessCode { get; set; }
}

public class DiscardRequest
{
    public List<Guid> CardIds { get; set; } = [];
}

public class ContinueRequest
{
    public bool WantsToContinue { get; set; }
}

public class JoinResponse
{
    public Guid PlayerId { get; set; }
    public GameStateDto GameState { get; set; } = null!;
}
