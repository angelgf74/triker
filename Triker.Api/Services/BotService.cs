using Triker.Shared.Models;

namespace Triker.Api.Services;

public class BotService(ScoringService scoringService)
{
    private static readonly string[] Names =
    [
        "Atlas", "Vega", "Nova", "Orion", "Lyra", "Echo",
        "Zara", "Rex", "Nyx", "Blaze", "Pixel", "Kronos"
    ];

    public string PickName(IEnumerable<string> usedNames)
    {
        var taken = usedNames.ToHashSet();
        var available = Names.Where(n => !taken.Contains(n)).ToList();
        return available.Count > 0
            ? available[Random.Shared.Next(available.Count)]
            : $"Bot-{Random.Shared.Next(100, 999)}";
    }

    public List<Guid> ChooseDiscards(List<Card> hand)
    {
        var result = scoringService.EvaluateHand(Guid.Empty, "", hand);

        // Escalera: no hay grupos pero sí puntuación → conservar todo
        if (result.Groups.Count == 0 && result.TotalScore > 0)
            return [];

        var keepIds = result.Groups
            .SelectMany(g => g.Cards.Select(c => c.Id))
            .ToHashSet();

        return hand.Where(c => !keepIds.Contains(c.Id))
                   .Select(c => c.Id)
                   .ToList();
    }
}
