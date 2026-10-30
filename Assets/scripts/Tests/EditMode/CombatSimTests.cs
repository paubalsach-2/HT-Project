using System.Collections.Generic;
using NUnit.Framework;

public class CombatSimTests
{
    static SimUnitSetup U(int uid, int defId, int star, SimTeam team, int col, int row)
    {
        return new SimUnitSetup { Uid = uid, DefId = defId, Star = star, Team = team, Col = col, Row = row };
    }

    static List<SimUnitSetup> ThreeVsThree()
    {
        return new List<SimUnitSetup>
        {
            U(1, 0, 1, SimTeam.A, 1, 5), U(2, 7, 1, SimTeam.A, 3, 5), U(3, 12, 2, SimTeam.A, 5, 6),
            U(-1, 3, 1, SimTeam.B, 1, 2), U(-2, 9, 1, SimTeam.B, 3, 1), U(-3, 14, 1, SimTeam.B, 5, 2),
        };
    }

    [Test]
    public void StrongerTeamWins()
    {
        // Equip A: una unitat de 3 estrelles. Equip B: una de 1 estrella (mateix personatge).
        var sim = new CombatSim(new List<SimUnitSetup>
        {
            U(1, 0, 3, SimTeam.A, 3, 4),
            U(2, 0, 1, SimTeam.B, 3, 3),
        });
        var r = sim.RunToEnd();
        Assert.AreEqual(SimOutcome.TeamAWon, r.Outcome);
        Assert.AreEqual(1, r.SurvivorsA);
        Assert.AreEqual(0, r.SurvivorsB);
    }

    [Test]
    public void EmptySideLosesImmediately()
    {
        var sim = new CombatSim(new List<SimUnitSetup> { U(1, 0, 1, SimTeam.A, 3, 4) });
        Assert.IsTrue(sim.Finished);
        Assert.AreEqual(SimOutcome.TeamAWon, sim.Result.Outcome);
    }

    [Test]
    public void SameInputGivesSameResult_100Times()
    {
        var first = new CombatSim(ThreeVsThree()).RunToEnd();
        for (int i = 0; i < 100; i++)
        {
            var r = new CombatSim(ThreeVsThree()).RunToEnd();
            Assert.AreEqual(first.Outcome, r.Outcome);
            Assert.AreEqual(first.Ticks, r.Ticks);
            Assert.AreEqual(first.SurvivorsA, r.SurvivorsA);
            Assert.AreEqual(first.SurvivorsB, r.SurvivorsB);
        }
    }

    [Test]
    public void InputOrderDoesNotMatter()
    {
        var a = ThreeVsThree();
        var b = ThreeVsThree();
        b.Reverse();
        var ra = new CombatSim(a).RunToEnd();
        var rb = new CombatSim(b).RunToEnd();
        Assert.AreEqual(ra.Outcome, rb.Outcome);
        Assert.AreEqual(ra.Ticks, rb.Ticks);
    }

    [Test]
    public void UnitWithoutTargetReportsNoTarget()
    {
        // sense enemics no hi ha objectiu: ha de dir NoTarget, no un Uid real
        var sim = new CombatSim(new List<SimUnitSetup> { U(1, 0, 1, SimTeam.A, 3, 4) });
        Assert.AreEqual(SimUnit.NoTarget, sim.Units[0].TargetUid);
    }

    [Test]
    public void CombatAlwaysEndsWithinMaxTicks()
    {
        var r = new CombatSim(ThreeVsThree()).RunToEnd();
        Assert.LessOrEqual(r.Ticks, SimConfig.MaxTicks);
    }
}
