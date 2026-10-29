using System;

public enum ShopResult { Ok, NotEnoughGold, BenchFull, EmptySlot, MaxLevel }

// Resultat d'un moviment d'unitat (tauler <-> banc)
public enum MoveResult { Ok, Invalid, BoardFull, WrongPhase }

// Lògica pura de la botiga (sense Unity): reroll, compra, venda, XP i moviments. Copia el TFT.
public sealed class ShopSystem
{
    public readonly PlayerProgress Progress = new PlayerProgress();
    public readonly Bench Bench;
    public readonly UnitPool Pool;

    readonly IWallet wallet;
    readonly Random rng;
    readonly UnitDefinition[] slots = new UnitDefinition[ShopConfig.ShopSlots];

    public event Action OnChanged;

    // Màxim d'unitats al tauler = nivell (com al TFT)
    public int BoardLimit => Progress.Level;

    // El servidor el posa a false durant el combat
    public bool CanRearrange = true;

    // Mode un jugador: pool i generador d'uids propis
    public ShopSystem(IWallet wallet, int seed = -1)
        : this(wallet, new UnitPool(), null, seed) { }

    // Mode multijugador: pool compartit i generador d'uids únic de la partida
    public ShopSystem(IWallet wallet, UnitPool pool, Func<int> uidGen, int seed = -1)
    {
        this.wallet = wallet;
        Pool = pool;
        Bench = new Bench(uidGen);
        rng = seed >= 0 ? new Random(seed) : new Random();
    }

    public UnitDefinition GetSlot(int i) => slots[i];

    // Reroll gratuït (inici de ronda)
    public void NewRound() => Roll();

    public ShopResult Reroll()
    {
        if (!wallet.TrySpend(ShopConfig.RerollCost)) return ShopResult.NotEnoughGold;
        Roll();
        return ShopResult.Ok;
    }

    public ShopResult BuyXp()
    {
        if (Progress.IsMax) return ShopResult.MaxLevel;
        if (!wallet.TrySpend(ShopConfig.BuyXpCost)) return ShopResult.NotEnoughGold;
        Progress.AddXp(ShopConfig.BuyXpAmount);
        OnChanged?.Invoke(); // les probabilitats poden haver canviat
        return ShopResult.Ok;
    }

    public ShopResult Buy(int slot)
    {
        if (slot < 0 || slot >= slots.Length) return ShopResult.EmptySlot;
        var d = slots[slot];
        if (d == null) return ShopResult.EmptySlot;
        if (wallet.Gold < d.Cost) return ShopResult.NotEnoughGold;
        if (!Bench.CanAdd(d)) return ShopResult.BenchFull;
        wallet.TrySpend(d.Cost);
        Bench.Add(d);
        slots[slot] = null;
        OnChanged?.Invoke();
        return ShopResult.Ok;
    }

    // Retorna l'or obtingut (0 si la unitat no és al banc)
    public int Sell(UnitInstance u)
    {
        if (u == null || !Bench.Remove(u)) return 0;
        int gold = u.SellValue;
        wallet.AddGold(gold);
        Pool.Return(u.Def, u.CopiesWorth);
        return gold;
    }

    // Versió per Uid (és la que farà servir la xarxa)
    public int Sell(int uid) => Sell(Bench.ByUid(uid));

    // ---------- Moviments (les regles viuen aquí, no a la vista) ----------

    public MoveResult MoveToBoard(UnitInstance u, int boardIndex)
    {
        if (!CanRearrange) return MoveResult.WrongPhase;
        if (u == null || !Bench.Contains(u)) return MoveResult.Invalid;
        if (boardIndex < 0 || boardIndex >= Bench.BoardSize) return MoveResult.Invalid;

        // Només puja el nombre d'unitats al tauler si ve del banc i la casella és buida
        bool fromBench = Bench.IndexOf(u) >= 0;
        bool targetEmpty = Bench.BoardAt(boardIndex) == null;
        if (fromBench && targetEmpty && Bench.BoardCount >= BoardLimit)
            return MoveResult.BoardFull;

        return Bench.MoveToBoard(u, boardIndex) ? MoveResult.Ok : MoveResult.Invalid;
    }

    public MoveResult MoveToBench(UnitInstance u, int benchIndex)
    {
        if (!CanRearrange) return MoveResult.WrongPhase;
        if (u == null || !Bench.Contains(u)) return MoveResult.Invalid;
        return Bench.MoveToBench(u, benchIndex) ? MoveResult.Ok : MoveResult.Invalid;
    }

    public MoveResult MoveToBoard(int uid, int boardIndex) => MoveToBoard(Bench.ByUid(uid), boardIndex);
    public MoveResult MoveToBench(int uid, int benchIndex) => MoveToBench(Bench.ByUid(uid), benchIndex);

    // Retorna tot al pool (botiga + unitats). Per a jugadors eliminats o desconnectats.
    public void ReleaseAll()
    {
        for (int i = 0; i < slots.Length; i++)
            if (slots[i] != null) { Pool.Return(slots[i]); slots[i] = null; }

        foreach (var u in Bench.Units())
            Pool.Return(u.Def, u.CopiesWorth);

        Bench.Clear();
        OnChanged?.Invoke();
    }

    // ---------- Snapshots ----------

    public PlayerSnapshot ExportSnapshot()
    {
        var s = new PlayerSnapshot
        {
            Gold = wallet.Gold,
            Level = Progress.Level,
            Xp = Progress.Xp
        };
        for (int i = 0; i < slots.Length; i++)
            s.ShopDefIds[i] = slots[i] != null ? slots[i].Id : -1;
        Bench.ExportSnapshot(s.Cells);
        return s;
    }

    // NO toca l'or: l'or viu a PlayerStats / IWallet.
    public void ApplySnapshot(PlayerSnapshot s)
    {
        Progress.Set(s.Level, s.Xp);
        for (int i = 0; i < slots.Length; i++)
            slots[i] = i < s.ShopDefIds.Length ? UnitLookup.ById(s.ShopDefIds[i]) : null;
        Bench.ImportSnapshot(s.Cells);
        OnChanged?.Invoke();
    }

    // ---------- Interns ----------

    void Roll()
    {
        for (int i = 0; i < slots.Length; i++)
            if (slots[i] != null) { Pool.Return(slots[i]); slots[i] = null; }
        for (int i = 0; i < slots.Length; i++) slots[i] = RollOne();
        OnChanged?.Invoke();
    }

    UnitDefinition RollOne()
    {
        var odds = ShopConfig.Odds(Progress.Level);
        var w = new int[ShopConfig.MaxTier];
        int total = 0;
        for (int t = 1; t <= ShopConfig.MaxTier; t++)
        {
            w[t - 1] = Pool.TierStock(t) > 0 ? odds[t - 1] : 0; // tier esgotat: se salta
            total += w[t - 1];
        }
        if (total <= 0) return null;

        int r = rng.Next(total);
        for (int t = 1; t <= ShopConfig.MaxTier; t++)
        {
            if (r < w[t - 1]) return Pool.Draw(t, rng);
            r -= w[t - 1];
        }
        return null;
    }
}
