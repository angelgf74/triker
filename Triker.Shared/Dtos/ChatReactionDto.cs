namespace Triker.Shared.Dtos;

public class ChatReactionDto
{
    public Guid PlayerId { get; set; }
    public string PlayerName { get; set; } = "";
    public string Emoji { get; set; } = "";
}

public class SendReactionRequest
{
    public string Emoji { get; set; } = "";
}
