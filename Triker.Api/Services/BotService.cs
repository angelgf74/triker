using Triker.Api.State;
using Triker.Shared.Models;

namespace Triker.Api.Services;

public class BotService(ScoringService scoringService)
{
    private static readonly (string Name, BotPersonality Personality)[] Roster =
    [
        ("Atlas",  BotPersonality.Conservative),
        ("Vega",   BotPersonality.Conservative),
        ("Lyra",   BotPersonality.Conservative),
        ("Echo",   BotPersonality.Balanced),
        ("Zara",   BotPersonality.Balanced),
        ("Pixel",  BotPersonality.Balanced),
        ("Blaze",  BotPersonality.Aggressive),
        ("Rex",    BotPersonality.Aggressive),
        ("Nova",   BotPersonality.Aggressive),
        ("Nyx",    BotPersonality.EscaleraSeeker),
        ("Orion",  BotPersonality.EscaleraSeeker),
        ("Kronos", BotPersonality.EscaleraSeeker),
    ];

    public (string Name, BotPersonality Personality) PickBot(IEnumerable<string> usedNames)
    {
        var taken = usedNames.ToHashSet();
        var available = Roster.Where(b => !taken.Contains(b.Name)).ToList();
        if (available.Count > 0)
            return available[Random.Shared.Next(available.Count)];
        var n = Random.Shared.Next(100, 999);
        return ($"Bot-{n}", (BotPersonality)Random.Shared.Next(4));
    }

    // Kept for backwards compatibility; personality defaults to Balanced.
    public string PickName(IEnumerable<string> usedNames) => PickBot(usedNames).Name;

    public List<Guid> ChooseDiscards(List<Card> hand, BotPersonality personality) => personality switch
    {
        BotPersonality.Conservative  => ChooseConservative(hand),
        BotPersonality.Aggressive    => ChooseAggressive(hand),
        BotPersonality.EscaleraSeeker => ChooseEscaleraSeeker(hand),
        _                            => ChooseBalanced(hand),
    };

    // ── Strategies ────────────────────────────────────────────────────────────

    // Keep all group cards, discard the rest (original behavior).
    private List<Guid> ChooseBalanced(List<Card> hand)
    {
        var result = scoringService.EvaluateHand(Guid.Empty, "", hand);
        if (result.Groups.Count == 0 && result.TotalScore > 0) return []; // Escalera
        var keep = result.Groups.SelectMany(g => g.Cards.Select(c => c.Id)).ToHashSet();
        return Discards(hand, keep);
    }

    // Pass if already holding a decent hand; otherwise improve but discard at most 2 cards.
    private List<Guid> ChooseConservative(List<Card> hand)
    {
        var result = scoringService.EvaluateHand(Guid.Empty, "", hand);
        if (result.HandRank >= HandRank.ParejaFuerte) return [];
        if (result.Groups.Count == 0 && result.TotalScore > 0) return []; // Escalera
        var keep = result.Groups.SelectMany(g => g.Cards.Select(c => c.Id)).ToHashSet();
        return Discards(hand, keep).Take(2).ToList();
    }

    // Keep only the single strongest group; if it's just a ParejaDebil, sometimes go all-in.
    private List<Guid> ChooseAggressive(List<Card> hand)
    {
        var result = scoringService.EvaluateHand(Guid.Empty, "", hand);
        if (result.Groups.Count == 0 && result.TotalScore > 0) return []; // Escalera

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

    // Discard cards that block an Escalera (duplicate any attribute value).
    // Falls back to Balanced when too many blockers exist.
    private List<Guid> ChooseEscaleraSeeker(List<Card> hand)
    {
        var blockers = FindEscaleraBlockers(hand);

        if (blockers.Count == 0) return []; // Already an Escalera
        if (blockers.Count <= 2) return blockers;

        // Too many blockers to fix cheaply — use balanced strategy instead
        return ChooseBalanced(hand);
    }

    // Returns one card per duplicate attribute value (the "extra" copy to discard).
    private static List<Guid> FindEscaleraBlockers(List<Card> hand)
    {
        var blockers = new HashSet<Guid>();
        foreach (var g in hand.GroupBy(c => c.Letter).Where(g => g.Count() > 1))
            foreach (var c in g.Skip(1)) blockers.Add(c.Id);
        foreach (var g in hand.GroupBy(c => c.Figure).Where(g => g.Count() > 1))
            foreach (var c in g.Skip(1)) blockers.Add(c.Id);
        foreach (var g in hand.GroupBy(c => c.Color).Where(g => g.Count() > 1))
            foreach (var c in g.Skip(1)) blockers.Add(c.Id);
        return [.. blockers];
    }

    private static List<Guid> Discards(List<Card> hand, HashSet<Guid> keepIds)
        => hand.Where(c => !keepIds.Contains(c.Id)).Select(c => c.Id).ToList();
}
