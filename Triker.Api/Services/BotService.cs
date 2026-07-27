using Triker.Api.State;
using Triker.Shared.Models;

namespace Triker.Api.Services;

public class BotService(ScoringService scoringService)
{
    // 12 por personalidad. La mesa admite 16 jugadores (MaxPlayers en
    // GameRoom.razor), asi que llenarla entera de bots deja 20 nombres sin usar
    // y las partidas no salen siempre con el mismo reparto. Con 12 en total se
    // agotaba la lista en cada mesa llena y aparecian los "Bot-799" de reserva.
    // Nombres cortos a proposito: cada rival es una fila de una linea.
    private static readonly (string Name, BotPersonality Personality)[] Roster =
    [
        ("Atlas",  BotPersonality.Conservative),
        ("Vega",   BotPersonality.Conservative),
        ("Lyra",   BotPersonality.Conservative),
        ("Nyx",    BotPersonality.Conservative),
        ("Ceres",  BotPersonality.Conservative),
        ("Juno",   BotPersonality.Conservative),
        ("Vesta",  BotPersonality.Conservative),
        ("Selene", BotPersonality.Conservative),
        ("Titan",  BotPersonality.Conservative),
        ("Rhea",   BotPersonality.Conservative),
        ("Iris",   BotPersonality.Conservative),
        ("Pallas", BotPersonality.Conservative),

        ("Echo",   BotPersonality.Balanced),
        ("Zara",   BotPersonality.Balanced),
        ("Pixel",  BotPersonality.Balanced),
        ("Orion",  BotPersonality.Balanced),
        ("Astra",  BotPersonality.Balanced),
        ("Lumen",  BotPersonality.Balanced),
        ("Indigo", BotPersonality.Balanced),
        ("Delta",  BotPersonality.Balanced),
        ("Onyx",   BotPersonality.Balanced),
        ("Sigma",  BotPersonality.Balanced),
        ("Kira",   BotPersonality.Balanced),
        ("Quasar", BotPersonality.Balanced),

        ("Blaze",  BotPersonality.Aggressive),
        ("Rex",    BotPersonality.Aggressive),
        ("Nova",   BotPersonality.Aggressive),
        ("Kronos", BotPersonality.Aggressive),
        ("Ember",  BotPersonality.Aggressive),
        ("Vulcan", BotPersonality.Aggressive),
        ("Ares",   BotPersonality.Aggressive),
        ("Ignis",  BotPersonality.Aggressive),
        ("Talon",  BotPersonality.Aggressive),
        ("Draco",  BotPersonality.Aggressive),
        ("Furia",  BotPersonality.Aggressive),
        ("Sable",  BotPersonality.Aggressive),
    ];

    public (string Name, BotPersonality Personality) PickBot(IEnumerable<string> usedNames)
    {
        var taken = usedNames.ToHashSet();
        var available = Roster.Where(b => !taken.Contains(b.Name)).ToList();
        if (available.Count > 0)
            return available[Random.Shared.Next(available.Count)];
        var n = Random.Shared.Next(100, 999);
        return ($"Bot-{n}", (BotPersonality)Random.Shared.Next(3));
    }

    // Kept for backwards compatibility; personality defaults to Balanced.
    public string PickName(IEnumerable<string> usedNames) => PickBot(usedNames).Name;

    public List<Guid> ChooseDiscards(List<Card> hand, BotPersonality personality) => personality switch
    {
        BotPersonality.Conservative  => ChooseConservative(hand),
        BotPersonality.Aggressive    => ChooseAggressive(hand),
        _                            => ChooseBalanced(hand),
    };

    // ── Strategies ────────────────────────────────────────────────────────────

    // Keep all group cards, discard the rest (original behavior).
    private List<Guid> ChooseBalanced(List<Card> hand)
    {
        var result = scoringService.EvaluateHand(Guid.Empty, "", hand);
        var keep = result.Groups.SelectMany(g => g.Cards.Select(c => c.Id)).ToHashSet();
        return Discards(hand, keep);
    }

    // Pass if already holding a decent hand; otherwise improve but discard at most 2 cards.
    private List<Guid> ChooseConservative(List<Card> hand)
    {
        var result = scoringService.EvaluateHand(Guid.Empty, "", hand);
        if (result.HandRank >= HandRank.ParejaFuerte) return [];
        var keep = result.Groups.SelectMany(g => g.Cards.Select(c => c.Id)).ToHashSet();
        return Discards(hand, keep).Take(2).ToList();
    }

    // Keep only the single strongest group; if it's just a ParejaDebil, sometimes go all-in.
    private List<Guid> ChooseAggressive(List<Card> hand)
    {
        var result = scoringService.EvaluateHand(Guid.Empty, "", hand);

        // Already strong: don't risk it
        if (result.HandRank >= HandRank.TrikerDebil)
        {
            var keep = result.Groups.SelectMany(g => g.Cards.Select(c => c.Id)).ToHashSet();
            return Discards(hand, keep);
        }

        // Pick only the best single group
        var best = result.Groups
            .OrderByDescending(g => g.MatchingParams * 10 + g.Cards.Count)
            .FirstOrDefault();

        if (best == null)
        {
            // No groups: discard 3-4 cards
            return hand.OrderBy(_ => Random.Shared.Next()).Take(Random.Shared.Next(3, 5)).Select(c => c.Id).ToList();
        }

        // Weak single pair: 40% chance to discard all 5 and redraw completely
        if (best.MatchingParams == 1 && best.Cards.Count == 2 && Random.Shared.NextDouble() < 0.4)
            return hand.Select(c => c.Id).ToList();

        var keepIds = best.Cards.Select(c => c.Id).ToHashSet();
        return Discards(hand, keepIds);
    }

    private static List<Guid> Discards(List<Card> hand, HashSet<Guid> keepIds)
        => hand.Where(c => !keepIds.Contains(c.Id)).Select(c => c.Id).ToList();
}
