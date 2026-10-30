using System.Collections.Generic;
using NUnit.Framework;

public class EnemyTeamGeneratorTests
{
    static List<HexCoord> HalfBoard()
    {
        var l = new List<HexCoord>();
        for (int r = 0; r < BoardConfig.HalfRows; r++)
            for (int c = 0; c < BoardConfig.Columns; c++)
                l.Add(new HexCoord(c, r));
        return l;
    }

    [Test]
    public void SameInputGivesSameTeam()
    {
        var a = EnemyTeamGenerator.Generate(5, 3, 42, HalfBoard());
        var b = EnemyTeamGenerator.Generate(5, 3, 42, HalfBoard());
        Assert.AreEqual(a.Count, b.Count);
        for (int i = 0; i < a.Count; i++)
        {
            Assert.AreEqual(a[i].DefId, b[i].DefId);
            Assert.AreEqual(a[i].Col, b[i].Col);
            Assert.AreEqual(a[i].Row, b[i].Row);
        }
    }

    [Test]
    public void UnitsGoToUniqueGivenTilesWithUniqueNegativeUids()
    {
        var tiles = HalfBoard();
        var team = EnemyTeamGenerator.Generate(10, 8, 7, tiles);
        var seenTiles = new HashSet<HexCoord>();
        var seenUids = new HashSet<int>();
        foreach (var u in team)
        {
            var c = new HexCoord(u.Col, u.Row);
            Assert.IsTrue(tiles.Contains(c));
            Assert.IsTrue(seenTiles.Add(c), "casella repetida");
            Assert.Less(u.Uid, 0);
            Assert.IsTrue(seenUids.Add(u.Uid), "uid repetit");
            Assert.AreEqual(SimTeam.B, u.Team);
        }
    }

    [Test]
    public void FirstRoundsOnlyUseLowTiers()
    {
        foreach (var u in EnemyTeamGenerator.Generate(1, 3, 1, HalfBoard()))
            Assert.LessOrEqual(UnitLookup.ById(u.DefId).Tier, 1);
    }

    [Test]
    public void TeamNeverExceedsAvailableTiles()
    {
        var tiles = new List<HexCoord> { new HexCoord(0, 0), new HexCoord(1, 0) };
        var team = EnemyTeamGenerator.Generate(20, 10, 3, tiles);
        Assert.LessOrEqual(team.Count, 2);
    }

    [Test]
    public void GeneratedTeamCanFightAFullCombat()
    {
        var setup = EnemyTeamGenerator.Generate(3, 3, 5, HalfBoard());
        setup.Add(new SimUnitSetup { Uid = 1, DefId = 0, Star = 1, Team = SimTeam.A, Col = 2, Row = 5 });
        setup.Add(new SimUnitSetup { Uid = 2, DefId = 5, Star = 1, Team = SimTeam.A, Col = 4, Row = 5 });
        var sim = new CombatSim(setup);
        var r = sim.RunToEnd();
        Assert.IsTrue(sim.Finished);
        Assert.LessOrEqual(r.Ticks, SimConfig.MaxTicks);
    }
}
