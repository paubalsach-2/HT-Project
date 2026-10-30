using System.Linq;
using NUnit.Framework;

// Fase 4 (Core): fases, ingressos, bloqueig de moviments i estat del tauler en acabar la preparació.
public class MatchPhaseTests
{
    // prep = 2 s (40 ticks), combat = 1 s (20), resolve = 1 s (20)
    static MatchState NewMatch(int players, int seed = 1)
    {
        var m = new MatchState(seed);
        m.Configure(2, 1, 1);
        for (ulong i = 1; i <= (ulong)players; i++) m.AddPlayer(i, "J" + i);
        m.Start();
        return m;
    }

    static void Tick(MatchState m, int n) { for (int i = 0; i < n; i++) m.Tick(); }
    static int TotalLeft(UnitPool p) => UnitDatabase.All.Sum(d => p.Remaining(d));

    // Còpies "en circulació": pool + botigues + unitats dels jugadors (una unitat de 2 estrelles val 3 còpies...).
    // Sempre ha de sumar el mateix: ni es creen ni es perden còpies.
    static int Circulating(MatchState m)
    {
        int n = TotalLeft(m.Pool);
        foreach (var s in m.Sessions)
        {
            for (int i = 0; i < ShopConfig.ShopSlots; i++) if (s.Shop.GetSlot(i) != null) n++;
            foreach (var u in s.Shop.Bench.Units()) n += u.CopiesWorth;
        }
        return n;
    }

    [Test]
    public void Start_BeginsPrepRound1_AndAllowsMoves()
    {
        var m = NewMatch(2);
        Assert.AreEqual(MatchPhase.Prep, m.Phase);
        Assert.AreEqual(1, m.Round);
        foreach (var s in m.Sessions) Assert.IsTrue(s.Shop.CanRearrange);
        Assert.AreEqual(40, m.TicksLeftInPhase);
    }

    [Test]
    public void Countdown_DecreasesEveryTick()
    {
        var m = NewMatch(1);
        Tick(m, 10);
        Assert.AreEqual(30, m.TicksLeftInPhase);
    }

    [Test]
    public void PrepEnds_GoesToCombat_AndLocksMoves()
    {
        var m = NewMatch(1);
        var a = m.Get(1);
        a.Buy(0);
        int uid = a.Shop.Bench.Units().First().Uid;
        Assert.IsNull(a.MoveToBoard(uid, 3));                 // a la preparació sí
        Tick(m, 40);

        Assert.AreEqual(MatchPhase.Combat, m.Phase);
        Assert.IsFalse(a.Shop.CanRearrange);
        Assert.AreEqual(20, m.TicksLeftInPhase);

        // el servidor aplica l'ESTAT ACTUAL del tauler: un moviment tardà és rebutjat i no canvia res
        Assert.AreEqual("Ara no pots moure unitats", a.MoveToBench(uid, 0));
        Assert.AreEqual(1, a.Shop.Bench.BoardCount);
        Assert.AreEqual(3, a.Shop.Bench.BoardIndexOf(a.Shop.Bench.ByUid(uid)));
    }

    [Test]
    public void Buying_IsAllowedDuringCombat()
    {
        var m = NewMatch(1);
        var a = m.Get(1);
        Tick(m, 40);
        Assert.AreEqual(MatchPhase.Combat, m.Phase);
        int gold = a.State.Gold;
        Assert.IsNull(a.Buy(0));
        Assert.Less(a.State.Gold, gold);
        Assert.AreEqual(1, a.Shop.Bench.Units().Count());
    }

    [Test]
    public void FullCycle_ReachesRound2_WithIncomeAndNewShop()
    {
        var m = NewMatch(2);
        var a = m.Get(1);
        a.Buy(0);                                  // gasta una mica
        int gold = a.State.Gold;
        int total = Circulating(m);

        Tick(m, 40);                               // Prep -> Combat
        Tick(m, 20);                               // Combat -> Resolve
        Assert.AreEqual(MatchPhase.Resolve, m.Phase);
        Tick(m, 20);                               // Resolve -> Prep (ronda 2)

        Assert.AreEqual(MatchPhase.Prep, m.Phase);
        Assert.AreEqual(2, m.Round);
        Assert.IsTrue(a.Shop.CanRearrange);
        Assert.AreEqual(gold + Economy.IncomeFor(gold), a.State.Gold);
        Assert.AreEqual(total, Circulating(m), "la botiga nova no ha de crear ni perdre còpies");
        for (int i = 0; i < ShopConfig.ShopSlots; i++) Assert.IsNotNull(a.Shop.GetSlot(i), "la botiga nova té les 5 targetes");
        StringAssert.Contains("Ronda 2", a.PendingMessage);
        Assert.IsNotNull(a.TakeMessage());
        Assert.IsNull(a.PendingMessage);
    }

    [Test]
    public void Interest_IsCappedAtMax()
    {
        var m = NewMatch(1);
        var a = m.Get(1);
        for (int i = 0; i < 4; i++) a.DebugAddGold(100);   // molt or
        int gold = a.State.Gold;
        Tick(m, 80);
        Assert.AreEqual(gold + Economy.GoldPerRound + Economy.MaxInterest, a.State.Gold);
    }

    [Test]
    public void DeadPlayer_GetsNoIncome()
    {
        var m = NewMatch(2);
        var dead = m.Get(1);
        var alive = m.Get(2);
        dead.DebugAddLives(-100); dead.DebugAddLives(-100); dead.DebugAddLives(-100);
        int g1 = dead.State.Gold, g2 = alive.State.Gold;
        Tick(m, 80);
        Assert.AreEqual(g1, dead.State.Gold);
        Assert.Greater(alive.State.Gold, g2);
    }

    [Test]
    public void PhaseEvent_FiresAfterRulesAreApplied()
    {
        var m = new MatchState(3);
        m.Configure(2, 1, 1);
        var a = m.AddPlayer(1, "A");
        bool movesLockedWhenCombatEventFired = false;
        m.OnPhaseChanged += (p, r) => { if (p == MatchPhase.Combat) movesLockedWhenCombatEventFired = !a.Shop.CanRearrange; };
        m.Start();
        Tick(m, 40);
        Assert.IsTrue(movesLockedWhenCombatEventFired);
    }

    [Test]
    public void LatePlayer_GetsCurrentPhaseRules()
    {
        var m = NewMatch(1);
        Tick(m, 40);                                // combat
        var late = m.AddPlayer(9, "Tard");
        Assert.IsFalse(late.Shop.CanRearrange);
    }
}
