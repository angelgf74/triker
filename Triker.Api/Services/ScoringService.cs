using Triker.Shared.Dtos;
using Triker.Shared.Models;

namespace Triker.Api.Services;

public class ScoringService
{
    public HandResultDto EvaluateHand(Guid playerId, string playerName, List<Card> hand)
    {
        var candidates = GetCandidateGroups(hand);
        var bestGroups = FindBestGrouping(hand, candidates);
        int totalScore = bestGroups.Sum(g => g.Score);

        var rank = ComputeHandRank(bestGroups);

        return new HandResultDto
        {
            PlayerId = playerId,
            PlayerName = playerName,
            Hand = hand.Select(ToDto).ToList(),
            Groups = bestGroups,
            TotalScore = totalScore,
            HandRank = rank,
            HandName = GetHandName(rank)
        };
    }

    private static HandRank ComputeHandRank(List<CardGroupDto> groups)
    {
        if (groups.Count == 0) return HandRank.SinJugada;

        var key = groups
            .Select(g => (n: g.Cards.Count, a: g.MatchingParams))
            .OrderByDescending(x => x.n).ThenByDescending(x => x.a)
            .ToList();

        return key switch
        {
            [(5, 2)] => HandRank.RetrikerFuerte,
            [(5, 1)] => HandRank.RetrikerDebil,
            [(4, 2)] => HandRank.TrikerFuerte,
            [(4, 1)] => HandRank.TrikerDebil,
            [(3, 2), (2, 2)] => HandRank.FullFuerte,
            [(3, 2), (2, 1)] => HandRank.FullMixto1,
            [(3, 1), (2, 2)] => HandRank.FullMixto2,
            [(3, 1), (2, 1)] => HandRank.FullDebil,
            [(3, 2)] => HandRank.TrioFuerte,
            [(3, 1)] => HandRank.TrioDebil,
            [(2, 2), (2, 2)] => HandRank.DobleParejaFuerte,
            [(2, 2), (2, 1)] => HandRank.DobleParejaMedia,
            [(2, 2)] => HandRank.ParejaFuerte,
            [(2, 1), (2, 1)] => HandRank.DobleParejaDebil,
            [(2, 1)] => HandRank.ParejaDebil,
            _ => HandRank.SinJugada
        };
    }

    private static string GetHandName(HandRank rank) => rank switch
    {
        HandRank.ParejaDebil       => "Pareja",
        HandRank.TrioDebil         => "Trío",
        HandRank.DobleParejaDebil  => "Doble Pareja",
        HandRank.TrikerDebil       => "Triker",
        HandRank.FullDebil         => "Full",
        HandRank.RetrikerDebil     => "Retriker",
        HandRank.ParejaFuerte      => "Pareja Fuerte",
        HandRank.DobleParejaMedia  => "Doble Pareja Mixta",
        HandRank.FullMixto2        => "Full Mixto 2",
        HandRank.TrioFuerte        => "Trío Fuerte",
        HandRank.DobleParejaFuerte => "Doble Pareja Fuerte",
        HandRank.FullMixto1        => "Full Mixto 1",
        HandRank.FullFuerte        => "Full Fuerte",
        HandRank.TrikerFuerte      => "Triker Fuerte",
        HandRank.RetrikerFuerte    => "Retriker Fuerte",
        _                          => "Sin jugada"
    };

    private List<CardGroupDto> GetCandidateGroups(List<Card> hand)
    {
        var seen = new HashSet<string>();
        var groups = new List<CardGroupDto>();

        void Add(List<Card> list)
        {
            if (list.Count < 2) return;
            var key = string.Join(",", list.Select(c => c.Id).OrderBy(id => id));
            if (!seen.Add(key)) return;
            int a = CountMatchingParams(list);
            if (a == 0) return;
            int n = list.Count;
            groups.Add(new CardGroupDto
            {
                Cards = list.Select(ToDto).ToList(),
                MatchingParams = a,
                Score = (n - 1) * (n - 1) * (int)Math.Pow(2, a),
                Name = BuildGroupName(n, a)
            });
        }

        // Enumerate all subsets of size ≥2 so that pairs/triples with a=2
        // inside a larger a=1 group are also considered (e.g. two cards sharing
        // both figure and color that belong to a 3-card same-figure group).
        for (int size = 2; size <= hand.Count; size++)
            foreach (var combo in Combinations(hand, size))
                Add(combo);

        return groups;
    }

    // With a 5-card hand, at most 2 non-overlapping groups are possible (2+3 or 2+2).
    // An O(k²) search is therefore complete and avoids 2^k blowup.
    private List<CardGroupDto> FindBestGrouping(List<Card> hand, List<CardGroupDto> candidates)
    {
        if (candidates.Count == 0) return [];

        var cardIdx = hand.Select((c, i) => (c.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var masked = candidates
            .Select(g => (Group: g, Mask: g.Cards.Aggregate(0, (acc, c) => acc | (1 << cardIdx[c.Id]))))
            .ToList();

        var best = new List<CardGroupDto>();
        HandRank bestRank = HandRank.SinJugada;

        foreach (var (group, _) in masked)
        {
            var rank = ComputeHandRank([group]);
            if (rank > bestRank) { bestRank = rank; best = [group]; }
        }

        for (int i = 0; i < masked.Count; i++)
        for (int j = i + 1; j < masked.Count; j++)
        {
            if ((masked[i].Mask & masked[j].Mask) != 0) continue;
            var current = new List<CardGroupDto> { masked[i].Group, masked[j].Group };
            var rank = ComputeHandRank(current);
            if (rank > bestRank) { bestRank = rank; best = current; }
        }

        return best;
    }

    private static List<List<Card>> Combinations(List<Card> src, int k)
    {
        if (k == 0) return [[]];
        var result = new List<List<Card>>();
        for (int i = 0; i <= src.Count - k; i++)
            foreach (var rest in Combinations(src.GetRange(i + 1, src.Count - i - 1), k - 1))
                result.Add([src[i], .. rest]);
        return result;
    }

    private static int CountMatchingParams(List<Card> cards)
    {
        int a = 0;
        if (cards.All(c => c.Letter == cards[0].Letter)) a++;
        if (cards.All(c => c.Figure == cards[0].Figure)) a++;
        if (cards.All(c => c.Color == cards[0].Color)) a++;
        return a;
    }

    private static string BuildGroupName(int n, int a)
    {
        string size = n switch
        {
            2 => "Pareja",
            3 => "Trío",
            4 => "Triker",
            5 => "Retriker",
            _ => $"Grupo({n})"
        };
        string suffix = a switch
        {
            1 => "Débil",
            2 => "Fuerte",
            _ => ""
        };
        return $"{size} {suffix}".Trim();
    }

    private static CardDto ToDto(Card c) => new()
    {
        Id = c.Id,
        Letter = c.Letter,
        Figure = c.Figure,
        Color = c.Color
    };
}
