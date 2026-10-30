using System;
using System.Collections.Generic;

public struct Pairing
{
    public ulong A, B;
    public bool BIsGhost;   // jugador imparell: no té rival real (B no val)
}

// Emparellaments de cada ronda: parelles aleatòries intentant no repetir el rival de la ronda anterior.
// Si el nombre de jugadors és imparell, un d'ells lluita contra un fantasma.
public static class MatchMaker
{
    public static List<Pairing> Pair(IList<ulong> players, IDictionary<ulong, ulong> lastOpponent, Random rng)
    {
        List<Pairing> best = null;
        int bestScore = int.MaxValue;

        for (int attempt = 0; attempt < 24; attempt++)
        {
            var order = new List<ulong>(players);
            for (int i = order.Count - 1; i > 0; i--)   // Fisher-Yates
            {
                int j = rng.Next(i + 1);
                var t = order[i]; order[i] = order[j]; order[j] = t;
            }

            var list = new List<Pairing>();
            int score = 0;
            for (int i = 0; i + 1 < order.Count; i += 2)
            {
                list.Add(new Pairing { A = order[i], B = order[i + 1] });
                if (Repeats(lastOpponent, order[i], order[i + 1])) score++;
            }
            if (order.Count % 2 == 1)
                list.Add(new Pairing { A = order[order.Count - 1], BIsGhost = true });

            if (score < bestScore) { best = list; bestScore = score; }
            if (score == 0) break;
        }
        return best ?? new List<Pairing>();
    }

    static bool Repeats(IDictionary<ulong, ulong> last, ulong a, ulong b)
    {
        ulong x;
        return last != null && last.TryGetValue(a, out x) && x == b;
    }
}
