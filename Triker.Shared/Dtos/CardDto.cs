using Triker.Shared.Models;

namespace Triker.Shared.Dtos;

public class CardDto
{
    public Guid Id { get; set; }
    public Letter Letter { get; set; }
    public Figure Figure { get; set; }
    public CardColor Color { get; set; }
}
