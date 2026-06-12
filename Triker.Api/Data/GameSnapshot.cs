namespace Triker.Api.Data;

public class GameSnapshot
{
    public Guid Id { get; set; }
    public string StateJson { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
}
