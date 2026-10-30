using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

// Fase 5 (Core): combats al servidor, danys, eliminació i final de partida.
public class CombatPhaseTests
{
    // Mapa de proves: índex i -> columna i % 7, fila i / 7 (la meitat de baix són les files 0..3)
    class FakeMap : IBoardMap
    {
        public bool TryGetCoord(int i, out int col, out int row)
        {
            col = i % BoardConfig.Columns;
            row = i / BoardConfig.Columns;
            return i >= 0 && i < Bench.BoardSize;
        }
    }

    static MatchState NewMatch(int players, int seed = 1)
    {
        var m = new MatchState(seed);
        m.BoardMap = new FakeMap();
        m.Configure(2, 1, 1);
        for (ulong i = 1; i <= (ulong)players; i++) m.AddPlayer(i, "J" + i);
        m.Start();
        return m;
    }

    static void TickUntil(MatchState m, MatchPhase p, int max = 5000)
    {
        int i = 0;
        while (m.Phase != p && i++ < max) m.Tick();
        Assert.AreEqual(p, m.Phase);
    }

    // Posa una unitat nova (defId) al tauler del jugador, directament (sense límit de nivell)
    static UnitInstance Put(PlayerSession s, int defId, int boardIndex)
    {
        s.Shop.Bench.Add(UnitDatabase.All[defId]);
        var u = s.Shop.Bench.Units().OrderByDescending(x => x.Uid).First();
        Assert.IsTrue(s.Shop.Bench.MoveToBoard(u, boardIndex));
        return u;
    }

    const int Strong = 20; // tier 5
    const int Weak = 0;    // tier 1

    [Test]
    public void Combat_BuildsMirroredFormations()
    {
        var m = NewMatch(2);
        Put(m.Get(1), Strong, 10);
        Put(m.Get(1), 21, 11);
        Put(m.Get(2), Weak, 3);
        TickUntil(m, MatchPhase.Combat);

        Assert.AreEqual(1, m.Fights.Count);
        var f = m.Fights[0];
        Assert.IsTrue(f.RealB);
        Assert.AreEqual(3, f.Setup.Count);
        foreach (var u in f.Setup)
        {
            if (u.Team == SimTeam.A) Assert.Less(u.Row, 4);      // A: meitat de baix
            else Assert.Greater(u.Row, 3);                        // B: meitat d'adalt (girada)
        }
        Assert.AreEqual(3, f.Setup.Select(u => u.Row * 10 + u.Col).Distinct().Count(), "caselles repetides");
        Assert.AreEqual(3, f.Setup.Select(u => u.Uid).Distinct().Count(), "uids repetits");
    }

    [Test]
    public void Combat_DurationFollowsTheSimulation()
    {
        var m = NewMatch(2);
        Put(m.Get(1), Strong, 10);
        Put(m.Get(2), Weak, 10);
        TickUntil(m, MatchPhase.Combat);
        var f = m.Fights[0];
        Assert.AreEqual(MatchState.CombatLeadTicks + f.Result.Ticks + MatchState.CombatTailTicks, m.TicksLeftInPhase);
    }

    [Test]
    public void Combat_LoserLosesLives_WinnerKeepsThem()
    {
        var m = NewMatch(2);
        Put(m.Get(1), Strong, 10);
        Put(m.Get(2), Weak, 10);
        TickUntil(m, MatchPhase.Combat);
        var f = m.Fights[0];
        var again = new CombatSim(f.Setup).RunToEnd();            // determinista: mateix resultat
        Assert.AreEqual(again.Outcome, f.Result.Outcome);
        Assert.AreEqual(again.Ticks, f.Result.Ticks);

        TickUntil(m, MatchPhase.Resolve);
        Assert.AreEqual(PlayerState.StartLives, m.Get(1).State.Lives);
        Assert.AreEqual(PlayerState.StartLives - Economy.DamageFor(1, 1), m.Get(2).State.Lives);
        StringAssert.Contains("perdut", m.Get(2).PendingMessage);
        StringAssert.Contains("guanyat", m.Get(1).PendingMessage);
        Assert.IsFalse(m.Over);
    }

    [Test]
    public void Elimination_ReleasesUnits_EndsMatch_AndRanksPlayers()
    {
        var m = NewMatch(2);
        Put(m.Get(1), Strong, 10);
        Put(m.Get(2), Weak, 10);
        m.Get(2).DebugAddLives(-19);                               // li queda 1 vida
        int overEvents = 0;
        m.OnMatchOver += () => overEvents++;
        ulong eliminated = 0;
        m.OnEliminated += s => eliminated = s.ClientId;

        TickUntil(m, MatchPhase.Resolve);

        Assert.IsTrue(m.Over);
        Assert.IsTrue(m.HasWinner);
        Assert.AreEqual(1UL, m.WinnerId);
        Assert.AreEqual(2UL, eliminated);
        Assert.AreEqual(1, m.Get(1).Place);
        Assert.AreEqual(2, m.Get(2).Place);
        Assert.AreEqual(0, m.Get(2).Shop.Bench.Units().Count(), "les unitats del eliminat tornen al pool");
        Assert.AreEqual(1, overEvents);
        StringAssert.Contains("1;J1", m.RankingText());
        StringAssert.Contains("2;J2", m.RankingText());

        var phase = m.Phase;                                       // la partida s'atura
        for (int i = 0; i < 400; i++) m.Tick();
        Assert.AreEqual(phase, m.Phase);
        Assert.AreEqual(1, overEvents);
    }

    [Test]
    public void OddPlayers_OneFightsAGhost_AndEveryoneFightsOnce()
    {
        var m = NewMatch(3);
        foreach (var s in m.Sessions) Put(s, Weak, 5);
        TickUntil(m, MatchPhase.Combat);

        Assert.AreEqual(2, m.Fights.Count);
        Assert.AreEqual(1, m.Fights.Count(f => f.BIsGhost));
        var ids = new List<ulong>();
        foreach (var f in m.Fights) { ids.Add(f.A); if (f.RealB) ids.Add(f.B); }
        Assert.AreEqual(3, ids.Distinct().Count());

        var ghost = m.Fights.First(f => f.BIsGhost);
        Assert.IsTrue(ghost.GhostOf != ghost.A);
        Assert.AreEqual(1, ghost.Setup.Count(u => u.Team == SimTeam.B), "el fantasma és una còpia del tauler d'un altre");
    }

    [Test]
    public void SinglePlayer_FightsAnAITeam_AndMatchContinues()
    {
        var m = NewMatch(1);
        Put(m.Get(1), Strong, 10);
        TickUntil(m, MatchPhase.Combat);

        Assert.AreEqual(1, m.Fights.Count);
        Assert.IsTrue(m.Fights[0].BIsAI);
        Assert.IsTrue(m.Fights[0].Setup.Any(u => u.Team == SimTeam.B && u.Uid < 0));

        TickUntil(m, MatchPhase.Resolve);
        Assert.IsFalse(m.Over);
    }

    [Test]
    public void DisconnectedPlayer_IsEliminated_AndMatchEnds()
    {
        var m = NewMatch(2);
        Put(m.Get(2), Weak, 3);
        bool over = false;
        m.OnMatchOver += () => over = true;

        m.RemovePlayer(2);

        Assert.IsTrue(over);
        Assert.IsTrue(m.Over);
        Assert.AreEqual(1UL, m.WinnerId);
    }

    [Test]
    public void CombatMessages_MirrorTheFight_ForBothPlayers()
    {
        var m = NewMatch(2);
        Put(m.Get(1), Strong, 10);
        Put(m.Get(2), Weak, 10);
        TickUntil(m, MatchPhase.Resolve);
        var f = m.Fights[0];

        var a = CombatMessages.SetupFor(f, 1, false);
        var b = CombatMessages.SetupFor(f, 1, true);
        Assert.IsFalse(a.Flipped);
        Assert.IsTrue(b.Flipped);
        Assert.AreEqual(f.Setup.Count, a.Units.Count);
        Assert.AreEqual(f.Setup.Count, b.Units.Count);

        var ea = CombatMessages.EndFor(f, false);
        var eb = CombatMessages.EndFor(f, true);
        Assert.AreEqual(f.DamageA, ea.DamageTaken);
        Assert.AreEqual(f.DamageB, eb.DamageTaken);
        Assert.AreEqual(ea.Outcome, eb.Outcome);                   // mateix marc: només canvia qui el mira
    }

    [Test]
    public void FullMatch_PlaysUntilSomeoneWins_WithoutLosingCopies()
    {
        var m = NewMatch(2);
        Put(m.Get(1), Strong, 10);
        Put(m.Get(2), Weak, 10);
        var total = Circulating(m);

        int guard = 0;
        while (!m.Over && guard++ < 200000) m.Tick();

        Assert.IsTrue(m.Over, "la partida ha d'acabar");
        Assert.AreEqual(1UL, m.WinnerId);
        Assert.AreEqual(total, Circulating(m), "ni es creen ni es perden còpies");
    }

    static int Circulating(MatchState m)
    {
        int n = UnitDatabase.All.Sum(d => m.Pool.Remaining(d));
        foreach (var s in m.Sessions)
        {
            for (int i = 0; i < ShopConfig.ShopSlots; i++) if (s.Shop.GetSlot(i) != null) n++;
            foreach (var u in s.Shop.Bench.Units()) n += u.CopiesWorth;
        }
        return n;
    }
}
