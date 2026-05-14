import random
from collections import defaultdict

random.seed(42)

deck = [(l, f, c) for l in range(5) for f in range(6) for c in range(6)]

def calc_score(n, a):
    return (n - 1) * (n - 1) * (1 << a)

def hand_name(n, a):
    sizes = ['', '', 'Pareja', 'Trío', 'Triker', 'Retriker']
    suffixes = ['', 'Simple', 'Doble']
    s = sizes[n]
    x = suffixes[a] if 1 <= a <= 2 else ''
    return (s + ' ' + x).strip()

def evaluate(hand):
    groups = []
    seen = set()

    for attr in range(3):
        by_val = defaultdict(list)
        for i, c in enumerate(hand):
            by_val[c[attr]].append(i)
        for idxs in by_val.values():
            if len(idxs) < 2:
                continue
            key = tuple(sorted(idxs))
            if key in seen:
                continue
            seen.add(key)
            a = sum(1 for p in range(3) if len(set(hand[i][p] for i in key)) == 1)
            n = len(key)
            groups.append((key, calc_score(n, a), n, a))

    best_score = 0
    best = []
    k = len(groups)
    if k:
        masks = [sum(1 << i for i in g[0]) for g in groups]
        for bits in range(1, 1 << k):
            used, ok, sc, cur = 0, True, 0, []
            for i in range(k):
                if not (bits >> i & 1):
                    continue
                if used & masks[i]:
                    ok = False
                    break
                used |= masks[i]
                sc += groups[i][1]
                cur.append(groups[i])
            if ok and sc > best_score:
                best_score, best = sc, cur

    el = sum(1 for p in range(3) if len(set(c[p] for c in hand)) == 5)
    es = [0, 3, 12, 50][el]

    if es > best_score:
        return ['', 'Escalera Simple', 'Escalera Doble', 'Escalera Real'][el], es
    if not best:
        return 'Sin jugada', 0

    parts = ' y '.join(hand_name(g[2], g[3]) for g in sorted(best, key=lambda x: -x[1]))
    return parts, best_score

N = 1_000_000
counts = defaultdict(int)
for _ in range(N):
    hand = random.sample(deck, 5)
    result, _ = evaluate(hand)
    counts[result] += 1

print(f"Total samples: {N}")
for result, cnt in sorted(counts.items(), key=lambda x: -x[1]):
    print(f"{cnt/N*100:.4f}%\t{result}")
