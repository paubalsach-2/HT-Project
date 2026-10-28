using System;

public enum ShopResult { Ok, NotEnoughGold, BenchFull, EmptySlot, MaxLevel }

// Lògica pura de la botiga (sense Unity): reroll, compra, venda i XP. Copia el TFT.
public sealed class ShopSystem
{
    public readonly PlayerProgress Progress = new PlayerProgress();
    public readonly Bench Bench = new Bench();
    public readonly UnitPool Pool = new UnitPool();

    readonly IWallet wallet;
    readonly Random rng;
    readonly UnitDefinition[] slots = new UnitDefinition[ShopConfig.ShopSlots];

    public event Action OnChanged;

    public ShopSystem(IWallet wallet, int seed = -1)
    {
        this.wallet = wallet;
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
