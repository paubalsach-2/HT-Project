using NUnit.Framework;

// Tests d'Editor (EditMode) de la fase 0: snapshots, fusió, límit de tauler i moviments.
public class CoreShopTests
{
    sealed class FakeWallet : IWallet
    {
        public int Gold { get; private set; }
        public FakeWallet(int gold) { Gold = gold; }
        public bool TrySpend(int amount)
        {
            if (Gold < amount) return false;
            Gold -= amount;
            return true;
        }
        public void AddGold(int amount) { Gold += amount; }
    }

    static UnitDefinition Def(int i) => UnitDatabase.All[i];

    [OneTimeSetUp]
    public void CheckDatabase()
    {
        Assert.GreaterOrEqual(UnitDatabase.All.Count, 4, "Calen almenys 4 personatges a UnitDatabase");
    }

    // ---------- 1. Snapshots ----------

    // Compara tot menys l'or (ApplySnapshot no el toca).
    static void AssertSameSnapshot(PlayerSnapshot a, PlayerSnapshot b)
    {
        Assert.AreEqual(a.Level, b.Level, "Level");
        Assert.AreEqual(a.Xp, b.Xp, "Xp");
        CollectionAssert.AreEqual(a.ShopDefIds, b.ShopDefIds, "ShopDefIds");
        Assert.AreEqual(a.Cells.Length, b.Cells.Length);
        for (int i = 0; i < a.Cells.Length; i++)
        {
            Assert.AreEqual(a.Cells[i].Uid, b.Cells[i].Uid, "Uid cel·la " + i);
            Assert.AreEqual(a.Cells[i].DefId, b.Cells[i].DefId, "DefId cel·la " + i);
            Assert.AreEqual(a.Cells[i].Star, b.Cells[i].Star, "Star cel·la " + i);
        }
    }

    [Test]
    public void Snapshot_ExportImportExport_IsIdentical()
    {
        var a = new ShopSystem(new FakeWallet(100), 7);
        a.NewRound();
        a.BuyXp(); // puja de nivell: el tauler admet més d'una unitat
        for (int slot = 0; slot < 3; slot++) a.Buy(slot);

        var u = a.Bench[0];
        Assert.IsNotNull(u, "S'esperava almenys una unitat al banc");
        Assert.AreEqual(MoveResult.Ok, a.MoveToBoard(u, 5));

        var s1 = a.ExportSnapshot();

        var b = new ShopSystem(new FakeWallet(0), 1);
        b.ApplySnapshot(s1);
        var s2 = b.ExportSnapshot();

        AssertSameSnapshot(s1, s2);
    }

    [Test]
    public void Snapshot_Import_ReusesExistingInstances()
    {
        var shop = new ShopSystem(new FakeWallet(0), 1);
        shop.Bench.Add(Def(0));
        var before = shop.Bench[0];

        var snap = shop.ExportSnapshot();
        shop.ApplySnapshot(snap);

        Assert.AreSame(before, shop.Bench[0], "La mateixa instància s'ha de conservar (les vistes la referencien)");
    }

    // ---------- 2. Fusió i Uid ----------

    [Test]
    public void Merge_AllOnBench_KeepsFirstUid()
    {
        var bench = new Bench();
        var d = Def(0);
        bench.Add(d); // uid 0
        bench.Add(d); // uid 1
        bench.Add(d); // uid 2 -> fusiona

        var all = new System.Collections.Generic.List<UnitInstance>(bench.Units());
        Assert.AreEqual(1, all.Count);
        Assert.AreEqual(0, all[0].Uid, "Ha de quedar l'Uid de la primera unitat");
        Assert.AreEqual(2, all[0].Star);
    }

    [Test]
    public void Merge_WithUnitOnBoard_KeepsBoardUid()
    {
        var bench = new Bench();
        var d = Def(0);
        bench.Add(d);                      // uid 0 al banc
        var first = bench[0];
        Assert.IsTrue(bench.MoveToBoard(first, 3));
        bench.Add(d);                      // uid 1
        bench.Add(d);                      // uid 2 -> fusiona

        Assert.AreEqual(1, new System.Collections.Generic.List<UnitInstance>(bench.Units()).Count);
        Assert.AreSame(first, bench.BoardAt(3), "La unitat del tauler és la que es queda");
        Assert.AreEqual(2, first.Star);
        Assert.AreEqual(0, first.Uid);
    }

    [Test]
    public void SharedUidGenerator_GivesUniqueUidsAcrossBenches()
    {
        int next = 0;
        var b1 = new Bench(() => next++);
        var b2 = new Bench(() => next++);
        b1.Add(Def(0));
        b2.Add(Def(1));
        b1.Add(Def(2));

        Assert.AreNotEqual(b1[0].Uid, b2[0].Uid);
        Assert.AreNotEqual(b1[0].Uid, b1[1].Uid);
        Assert.AreNotEqual(b2[0].Uid, b1[1].Uid);
    }

    // ---------- 3. Límit de tauler = nivell ----------

    [Test]
    public void BoardLimit_EqualsLevel()
    {
        var shop = new ShopSystem(new FakeWallet(100), 1);
        for (int i = 0; i < 3; i++) shop.Bench.Add(Def(i));

        Assert.AreEqual(1, shop.BoardLimit);
        Assert.AreEqual(MoveResult.Ok, shop.MoveToBoard(shop.Bench[0], 0));
        Assert.AreEqual(MoveResult.BoardFull, shop.MoveToBoard(shop.Bench[1], 1));

        shop.BuyXp(); // 4 XP: del nivell 1 al 3
        Assert.AreEqual(3, shop.BoardLimit);
        Assert.AreEqual(MoveResult.Ok, shop.MoveToBoard(shop.Bench[1], 1));
        Assert.AreEqual(MoveResult.Ok, shop.MoveToBoard(shop.Bench[2], 2));
        Assert.AreEqual(3, shop.Bench.BoardCount);
    }

    [Test]
    public void BoardLimit_DoesNotBlockSwapsOrBoardMoves()
    {
        var shop = new ShopSystem(new FakeWallet(0), 1); // nivell 1: límit 1
        shop.Bench.Add(Def(0));
        shop.Bench.Add(Def(1));
        var a = shop.Bench[0];
        var b = shop.Bench[1];
        Assert.AreEqual(MoveResult.Ok, shop.MoveToBoard(a, 0));

        // Tauler ple: intercanviar amb una unitat del banc és permès
        Assert.AreEqual(MoveResult.Ok, shop.MoveToBoard(b, 0));
        Assert.AreSame(b, shop.Bench.BoardAt(0));
        // Moure dins del tauler també
        Assert.AreEqual(MoveResult.Ok, shop.MoveToBoard(b, 4));
        Assert.AreEqual(1, shop.Bench.BoardCount);
    }

    [Test]
    public void Move_WhenCannotRearrange_ReturnsWrongPhase()
    {
        var shop = new ShopSystem(new FakeWallet(0), 1);
        shop.Bench.Add(Def(0));
        shop.CanRearrange = false;

        Assert.AreEqual(MoveResult.WrongPhase, shop.MoveToBoard(shop.Bench[0], 0));
        Assert.AreEqual(MoveResult.WrongPhase, shop.MoveToBench(shop.Bench[0], 1));
    }

    [Test]
    public void Move_InvalidArguments_ReturnInvalid()
    {
        var shop = new ShopSystem(new FakeWallet(0), 1);
        shop.Bench.Add(Def(0));
        var u = shop.Bench[0];

        Assert.AreEqual(MoveResult.Invalid, shop.MoveToBoard(u, -1));
        Assert.AreEqual(MoveResult.Invalid, shop.MoveToBoard(u, Bench.BoardSize));
        Assert.AreEqual(MoveResult.Invalid, shop.MoveToBoard(9999, 0));   // uid inexistent
        Assert.AreEqual(MoveResult.Invalid, shop.MoveToBench(u, Bench.Size));
    }

    // ---------- 4. Intercanvi tauler <-> banc ----------

    [Test]
    public void Swap_BoardAndBench()
    {
        var shop = new ShopSystem(new FakeWallet(0), 1);
        shop.Bench.Add(Def(0));
        shop.Bench.Add(Def(1));
        var a = shop.Bench[0];
        var b = shop.Bench[1];

        Assert.AreEqual(MoveResult.Ok, shop.MoveToBoard(a, 0));
        Assert.IsNull(shop.Bench[0]);

        // b (banc 1) a la casella ocupada per a: s'intercanvien
        Assert.AreEqual(MoveResult.Ok, shop.MoveToBoard(b, 0));
        Assert.AreSame(b, shop.Bench.BoardAt(0));
        Assert.AreSame(a, shop.Bench[1]);
        Assert.IsNull(shop.Bench[0]);

        // b del tauler a un espai de banc buit
        Assert.AreEqual(MoveResult.Ok, shop.MoveToBench(b, 0));
        Assert.IsNull(shop.Bench.BoardAt(0));
        Assert.AreSame(b, shop.Bench[0]);
    }
}
