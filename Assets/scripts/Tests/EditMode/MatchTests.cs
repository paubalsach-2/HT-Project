using NUnit.Framework;

public class MatchTests
{
    [Test]
    public void InterestIsCapped()
    {
        Assert.AreEqual(0, Economy.InterestFor(9));
        Assert.AreEqual(1, Economy.InterestFor(10));
        Assert.AreEqual(3, Economy.InterestFor(37));
        Assert.AreEqual(5, Economy.InterestFor(50));
        Assert.AreEqual(5, Economy.InterestFor(200));
    }

    [Test]
    public void IncomeIsBasePlusInterest()
    {
        Assert.AreEqual(5 + 2, Economy.IncomeFor(25));
    }

    [Test]
    public void DamageGrowsEveryTwoRounds()
    {
        Assert.AreEqual(2, Economy.BaseDamageForRound(1));
        Assert.AreEqual(2, Economy.BaseDamageForRound(2));
        Assert.AreEqual(3, Economy.BaseDamageForRound(3));
        Assert.AreEqual(3, Economy.BaseDamageForRound(4));
        Assert.AreEqual(4, Economy.BaseDamageForRound(5));
        Assert.AreEqual(2 + 3, Economy.DamageFor(1, 3));
    }

    [Test]
    public void PhasesFollowTheCycle()
    {
        var t = new PhaseTimer { PrepTicks = 3, ResolveTicks = 2 };
        t.Start();
        Assert.AreEqual(MatchPhase.Prep, t.Phase);
        Assert.AreEqual(1, t.Round);

        t.Tick(); t.Tick();
        Assert.AreEqual(MatchPhase.Prep, t.Phase);
        t.Tick();
        Assert.AreEqual(MatchPhase.Combat, t.Phase);

        t.Tick(); t.Tick(); t.Tick();                 // el combat no té límit propi
        Assert.AreEqual(MatchPhase.Combat, t.Phase);

        t.EndCombat();
        Assert.AreEqual(MatchPhase.Resolve, t.Phase);
        t.Tick(); t.Tick();
        Assert.AreEqual(MatchPhase.Prep, t.Phase);
        Assert.AreEqual(2, t.Round);
    }

    [Test]
    public void SkipPrepAndEndCombatOnlyWorkInTheirPhase()
    {
        var t = new PhaseTimer { PrepTicks = 10, ResolveTicks = 10 };
        t.Start();
        t.EndCombat();                                // no estem en combat: s'ignora
        Assert.AreEqual(MatchPhase.Prep, t.Phase);
        t.SkipPrep();
        Assert.AreEqual(MatchPhase.Combat, t.Phase);
        t.SkipPrep();                                 // ja no som a Prep: s'ignora
        Assert.AreEqual(MatchPhase.Combat, t.Phase);
    }
}
