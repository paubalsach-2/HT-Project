using System.Linq;
using NUnit.Framework;

// Fase 3 (Core): pool compartit entre jugadors i ordres manipulades. Sense xarxa.
public class PlayerSessionTests
{
    static int TotalLeft(UnitPool pool) => UnitDatabase.All.Sum(d => pool.Remaining(d));
    static int TotalMax() => UnitDatabase.All.Sum(d => ShopConfig.CopiesPerTier[d.Tier]);

    static int FirstUnitUid(PlayerSession s)
    {
        foreach (var u in s.Shop.Bench.Units()) return u.Uid;
        return -1;
    }

    [Test]
    public void SharedPool_BothPlayersDrawFromTheSamePool()
    {
        var m = new MatchState(1);
        m.AddPlayer(1, "A");
        m.AddPlayer(2, "B");
        // cada botiga inicial treu ShopSlots còpies del MATEIX pool
        Assert.AreEqual(TotalMax() - 2 * ShopConfig.ShopSlots, TotalLeft(m.Pool));
    }

    [Test]
    public void Uids_AreUniqueAcrossPlayers()
    {
        var m = new MatchState(2);
        var a = m.AddPlayer(1, "A");
        var b = m.AddPlayer(2, "B");
        a.Buy(0);
        b.Buy(0);
        Assert.AreNotEqual(-1, FirstUnitUid(a));
        Assert.AreNotEqual(-1, FirstUnitUid(b));
        Assert.AreNotEqual(FirstUnitUid(a), FirstUnitUid(b));
    }

    [Test]
    public void Sell_ReturnsCopiesToSharedPool()
    {
        var m = new MatchState(3);
        var a = m.AddPlayer(1, "A");
        a.Buy(0);
        int before = TotalLeft(m.Pool);
        Assert.IsTrue(a.Sell(FirstUnitUid(a)).StartsWith("Venut"));
        Assert.AreEqual(before + 1, TotalLeft(m.Pool));
    }

    [Test]
    public void Cheat_CannotSellOrMoveAnotherPlayersUnit()
    {
        var m = new MatchState(4);
        var a = m.AddPlayer(1, "A");
        var b = m.AddPlayer(2, "B");
        a.Buy(0);
        int uidA = FirstUnitUid(a);
        int goldB = b.State.Gold;
        int unitsA = a.Shop.Bench.Units().Count();

        Assert.AreEqual(PlayerSession.InvalidMsg, b.Sell(uidA));
        Assert.AreEqual(PlayerSession.InvalidMsg, b.MoveToBoard(uidA, 0));
        Assert.AreEqual(PlayerSession.InvalidMsg, b.MoveToBench(uidA, 0));

        Assert.AreEqual(goldB, b.State.Gold);
        Assert.AreEqual(unitsA, a.Shop.Bench.Units().Count());
        Assert.IsNotNull(a.Shop.Bench.ByUid(uidA));
    }

    [Test]
    public void Cheat_OutOfRangeArgumentsChangeNothing()
    {
        var m = new MatchState(5);
        var a = m.AddPlayer(1, "A");
        int gold = a.State.Gold;

        Assert.AreEqual(PlayerSession.InvalidMsg, a.Buy(-1));
        Assert.AreEqual(PlayerSession.InvalidMsg, a.Buy(99));
        Assert.AreEqual(PlayerSession.InvalidMsg, a.Sell(-5));
        Assert.AreEqual(PlayerSession.InvalidMsg, a.Sell(999999));
        Assert.AreEqual(PlayerSession.InvalidMsg, a.MoveToBoard(999999, 5));
        Assert.AreEqual(gold, a.State.Gold);
        Assert.AreEqual(0, a.Shop.Bench.Units().Count());

        a.Buy(0);
        int uid = FirstUnitUid(a);
        Assert.AreEqual(PlayerSession.InvalidMsg, a.MoveToBoard(uid, 9999));
        Assert.AreEqual(PlayerSession.InvalidMsg, a.MoveToBoard(uid, -1));
        Assert.AreEqual(PlayerSession.InvalidMsg, a.MoveToBench(uid, -3));
        Assert.AreEqual(PlayerSession.InvalidMsg, a.MoveToBench(uid, 99));
    }

    [Test]
    public void Cheat_CannotSpendGoldThatIsNotThere()
    {
        var m = new MatchState(6);
        var a = m.AddPlayer(1, "A");
        for (int i = 0; i < 5; i++) a.Reroll();       // 5 x 2 = 10 d'or
        Assert.AreEqual(0, a.State.Gold);
        Assert.AreEqual("No tens prou or", a.Reroll());
        Assert.AreEqual("No tens prou or", a.BuyXp());
        Assert.AreEqual("No tens prou or", a.Buy(0));
        Assert.AreEqual(0, a.State.Gold);
    }

    [Test]
    public void BoardLimit_IsEnforcedByServer()
    {
        var m = new MatchState(7);
        var a = m.AddPlayer(1, "A");
        a.DebugAddGold(100);
        a.Buy(0); a.Buy(1);
        var uids = a.Shop.Bench.Units().Select(u => u.Uid).ToList();
        Assert.GreaterOrEqual(uids.Count, 2);
        Assert.IsNull(a.MoveToBoard(uids[0], 0));      // nivell 1: una unitat al tauler
        Assert.IsTrue(a.MoveToBoard(uids[1], 1).Contains("màxim"));
        Assert.AreEqual(1, a.Shop.Bench.BoardCount);
    }

    [Test]
    public void DeadPlayer_CannotAct()
    {
        var m = new MatchState(8);
        var a = m.AddPlayer(1, "A");
        a.DebugAddLives(-100);
        a.DebugAddLives(-100);
        a.DebugAddLives(-100);
        Assert.IsFalse(a.IsAlive);
        Assert.AreEqual(PlayerSession.DeadMsg, a.Buy(0));
        Assert.AreEqual(PlayerSession.DeadMsg, a.Reroll());
    }

    [Test]
    public void Snapshot_RoundTripIsIdentical()
    {
        var m = new MatchState(9);
        var a = m.AddPlayer(1, "A");
        a.Buy(0); a.Buy(1);
        var snap = a.Snapshot();

        var replica = new ShopSystem(new PlayerState());
        replica.ApplySnapshot(snap);
        var again = replica.ExportSnapshot();

        Assert.AreEqual(snap.Level, again.Level);
        Assert.AreEqual(snap.Xp, again.Xp);
        CollectionAssert.AreEqual(snap.ShopDefIds, again.ShopDefIds);
        for (int i = 0; i < snap.Cells.Length; i++)
        {
            Assert.AreEqual(snap.Cells[i].Uid, again.Cells[i].Uid, "uid " + i);
            Assert.AreEqual(snap.Cells[i].DefId, again.Cells[i].DefId, "def " + i);
            Assert.AreEqual(snap.Cells[i].Star, again.Cells[i].Star, "star " + i);
        }
    }

    [Test]
    public void RemovePlayer_ReturnsEverythingToPool()
    {
        var m = new MatchState(10);
        var a = m.AddPlayer(1, "A");
        a.Buy(0);
        m.RemovePlayer(1);
        Assert.AreEqual(TotalMax(), TotalLeft(m.Pool));
        Assert.IsNull(m.Get(1));
    }

    // ---------------------------------------------------------------- Fase 6

    [Test]
    public void Disconnect_AfterGrace_EliminatesAndEndsMatch()
    {
        var m = new MatchState(11);
        m.DisconnectGraceTicks = 5;
        int over = 0;
        m.OnMatchOver += () => over++;
        var a = m.AddPlayer(1, "A");
        var b = m.AddPlayer(2, "B");
        m.Start();

        m.Disconnect(2);
        Assert.IsTrue(b.Disconnected);
        Assert.IsTrue(b.IsAlive, "durant el període de gràcia encara és viu");

        for (int i = 0; i < 4; i++) m.Tick();
        Assert.IsTrue(b.IsAlive);
        m.Tick();                                   // 5è tick: s'acaba la gràcia

        Assert.IsFalse(b.IsAlive);
        Assert.IsTrue(m.Over);
        Assert.IsTrue(m.HasWinner);
        Assert.AreEqual(1UL, m.WinnerId);
        Assert.AreEqual(2, b.Place);
        Assert.AreEqual(1, over, "OnMatchOver s'ha de disparar una sola vegada");
        // el servidor conserva la sessió: surt a la classificació
        StringAssert.Contains("B", m.RankingText());
        // les unitats del desconnectat han tornat al pool (només queda la botiga d'A)
        Assert.AreEqual(TotalMax() - ShopConfig.ShopSlots, TotalLeft(m.Pool));
    }

    [Test]
    public void Disconnect_BeforeStart_RemovesPlayer()
    {
        var m = new MatchState(12);
        m.AddPlayer(1, "A");
        m.AddPlayer(2, "B");
        m.Disconnect(2);
        Assert.IsNull(m.Get(2));
        Assert.AreEqual(1, m.PlayerCount);
    }

    [Test]
    public void LastHumanDisconnects_EndsMatchEvenIfBotsRemain()
    {
        var m = new MatchState(13);
        var a = m.AddPlayer(1, "A");
        m.AddBot("Bot 1");
        m.AddBot("Bot 2");
        m.Start();
        m.Disconnect(1);
        Assert.IsTrue(m.Over);
        Assert.IsTrue(a.Disconnected);
    }

    [Test]
    public void Bots_BuyAndPlaceUnitsOnTheBoard()
    {
        var m = new MatchState(14);
        m.AddPlayer(1, "A");
        var bot = m.AddBot("Bot 1");
        m.Start();
        Assert.IsTrue(bot.IsBot);
        Assert.GreaterOrEqual(bot.Shop.Bench.BoardCount, 1);
    }

    [Test]
    public void EightSessions_RunSeveralRounds_AndPoolIsConserved()
    {
        var m = new MatchState(15);
        m.Configure(1, 1, 1);                       // 20 + 20 + 20 ticks per ronda
        m.AddPlayer(1, "A");
        for (int i = 1; i <= 7; i++) m.AddBot("Bot " + i);
        Assert.AreEqual(8, m.PlayerCount);
        m.Start();

        for (int i = 0; i < 250; i++) m.Tick();

        Assert.GreaterOrEqual(m.Round, 3);
        Assert.IsFalse(m.Over);

        // tot el que no és al pool és a mans d'algú (botigues + unitats)
        int held = 0;
        foreach (var s in m.Sessions)
        {
            for (int i = 0; i < ShopConfig.ShopSlots; i++)
                if (s.Shop.GetSlot(i) != null) held++;
            foreach (var u in s.Shop.Bench.Units()) held += u.CopiesWorth;
        }
        Assert.AreEqual(TotalMax(), TotalLeft(m.Pool) + held);
    }
}
