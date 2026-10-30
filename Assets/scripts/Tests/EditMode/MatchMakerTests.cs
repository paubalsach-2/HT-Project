using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

public class MatchMakerTests
{
    static List<ulong> Ids(int n) { var l = new List<ulong>(); for (ulong i = 1; i <= (ulong)n; i++) l.Add(i); return l; }

    [Test]
    public void EveryPlayerPlaysOnce_AndOddCountGetsOneGhost()
    {
        for (int n = 1; n <= 8; n++)
        {
            var pairs = MatchMaker.Pair(Ids(n), new Dictionary<ulong, ulong>(), new Random(n));
            var seen = new List<ulong>();
            foreach (var p in pairs) { seen.Add(p.A); if (!p.BIsGhost) seen.Add(p.B); }
            Assert.AreEqual(n, seen.Count, "jugadors repetits o oblidats amb n=" + n);
            Assert.AreEqual(n, seen.Distinct().Count());
            Assert.AreEqual(n % 2, pairs.Count(p => p.BIsGhost), "fantasmes amb n=" + n);
        }
    }

    [Test]
    public void AvoidsRepeatingLastOpponent_WhenPossible()
    {
        var last = new Dictionary<ulong, ulong> { { 1, 2 }, { 2, 1 }, { 3, 4 }, { 4, 3 } };
        for (int seed = 0; seed < 50; seed++)
        {
            foreach (var p in MatchMaker.Pair(Ids(4), last, new Random(seed)))
            {
                ulong x;
                Assert.IsFalse(last.TryGetValue(p.A, out x) && x == p.B, "repeteix rival (seed " + seed + ")");
            }
        }
    }

    [Test]
    public void TwoPlayers_StillGetPaired()
    {
        var last = new Dictionary<ulong, ulong> { { 1, 2 }, { 2, 1 } };
        var pairs = MatchMaker.Pair(Ids(2), last, new Random(1));
        Assert.AreEqual(1, pairs.Count);
        Assert.IsFalse(pairs[0].BIsGhost);
    }
}
