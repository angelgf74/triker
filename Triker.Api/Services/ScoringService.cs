using Triker.Shared.Dtos;
using Triker.Shared.Models;

namespace Triker.Api.Services;

public class ScoringService
{
    public HandResultDto EvaluateHand(Guid playerId, string playerName, List<Card> hand)
    {
        var candidates = GetCandidateGroups(hand);
        var bestGroups = FindBestGrouping(hand, candidates);
        int groupsScore = bestGroups.Sum(g => g.Score);

        int escaleraLevel = CountEscaleraLevel(hand);
        int escaleraScore = escaleraLevel == 3 ? 50 : 0;

        bool usedEscalera = escaleraScore > groupsScore;
        int totalScore;
        List<CardGroupDto> resultGroups;

        if (usedEscalera)
        {
            totalScore = escaleraScore;
            resultGroups = [];
        }
        else
        {
            totalScore = groupsScore;
            resultGroups = bestGroups;
        }

        var rank = ComputeHandRank(resultGroups, usedEscalera, escaleraLevel);

        return new HandResultDto
        {
            PlayerId = playerId,
            PlayerName = playerName,
            Hand = hand.Select(ToDto).ToList(),
            Groups = resultGroups,
            TotalScore = totalScore,
            HandRank = rank,
            HandName = GetHandName(rank)
        };
    }

    private static HandRank ComputeHandRank(List<CardGroupDto> groups, bool usedEscalera, int escaleraLevel)
    {
        if (usedEscalera) return HandRank.Escalera;

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
            [(3, 2), (2, 1)] => HandRank.Full,
            [(3, 1), (2, 2)] => HandRank.FullMixto,
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
        HandRank.ParejaDebil       => "Pareja Débil",
        HandRank.TrioDebil         => "Trío Débil",
        HandRank.DobleParejaDebil  => "Doble Pareja",
        HandRank.TrikerDebil       => "Triker Débil",
        HandRank.FullDebil         => "Full Débil",
        HandRank.RetrikerDebil     => "Retriker Débil",
        HandRank.ParejaFuerte      => "Pareja Fuerte",
        HandRank.DobleParejaMedia  => "Doble Pareja Mixta",
        HandRank.FullMixto         => "Full Mixto",
        HandRank.TrioFuerte        => "Trío Fuerte",
        HandRank.DobleParejaFuerte => "Doble Pareja Fuerte",
        HandRank.Full              => "Full",
        HandRank.FullFuerte        => "Full Fuerte",
        HandRank.TrikerFuerte      => "Triker Fuerte",
        HandRank.RetrikerFuerte    => "Retriker Fuerte",
        HandRank.Escalera          => "Escalera",
        _                          => "Sin jugada"
    };

    private List<CardGroupDto> GetCandidateGroups(List<Card> hand)
    {
        var seen = new HashSet<string>();
        var groups = new List<CardGroupDto>();

        void Add(IEnumerable<Card> cards)
        {
            var list = cards.ToList();
            if (list.Count < 2) return;
            var key = string.Join(",", list.Select(c => c.Id).OrderBy(id => id));
            if (!seen.Add(key)) return;
            int a = CountMatchingParams(list);
            int n = list.Count;
            groups.Add(new CardGroupDto
            {
                Cards = list.Select(ToDto).ToList(),
                MatchingParams = a,
                Score = (n - 1) * (n - 1) * (int)Math.Pow(2, a),
                Name = BuildGroupName(n, a)
            });
        }

        foreach (var g in hand.GroupBy(c => c.Letter)) Add(g);
        foreach (var g in hand.GroupBy(c => c.Figure)) Add(g);
        foreach (var g in hand.GroupBy(c => c.Color))  Add(g);

        return groups;
    }

    private List<CardGroupDto> FindBestGrouping(List<Card> hand, List<CardGroupDto> candidates)
    {
        if (candidates.Count == 0) return [];

        // Represent each candidate as a bitmask over hand indices
        var masks = candidates.Select(g =>
        {
            int mask = g.Cards.Aggregate(0, (acc, c) =>
                acc | (1 << hand.FindIndex(h => h.Id == c.Id)));
            return (Group: g, Mask: mask);
        }).ToList();

        int k = masks.Count;
        var best = new List<CardGroupDto>();
        int bestScore = 0;

        // Enumerate all 2^k subsets of candidates (k <= ~15 for N=5)
        for (int subset = 1; subset < (1 << k); subset++)
        {
            int usedCards = 0;
            bool valid = true;
            var current = new List<CardGroupDto>();
            int score = 0;

            for (int i = 0; i < k; i++)
            {
                if ((subset & (1 << i)) == 0) continue;
                var (group, mask) = masks[i];
                if ((usedCards & mask) != 0) { valid = false; break; }
                usedCards |= mask;
                current.Add(group);
                score += group.Score;
            }

            if (valid && score > bestScore)
            {
                bestScore = score;
                best = current;
            }
        }

        return best;
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

    private static int CountEscaleraLevel(List<Card> hand)
    {
        int level = 0;
        if (hand.Select(c => c.Letter).Distinct().Count() == hand.Count) level++;
        if (hand.Select(c => c.Figure).Distinct().Count() == hand.Count) level++;
        if (hand.Select(c => c.Color).Distinct().Count() == hand.Count) level++;
        return level;
    }

    private static CardDto ToDto(Card c) => new()
    {
        Id = c.Id,
        Letter = c.Letter,
        Figure = c.Figure,
        Color = c.Color
    };
}
