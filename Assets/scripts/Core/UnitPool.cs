using System;
using System.Collections.Generic;

// Reserva compartida de còpies (com al TFT): comprar-ne treu còpies, vendre-les les retorna.
public sealed class UnitPool
{
    readonly Dictionary<UnitDefinition, int> left = new Dictionary<UnitDefinition, int>();

    public UnitPool()
    {
        foreach (var d in UnitDatabase.All) left[d] = ShopConfig.CopiesPerTier[d.Tier];
    }

    public int Remaining(UnitDefinition d) => left[d];

    public int TierStock(int tier)
    {
        int n = 0;
        foreach (var d in UnitDatabase.ByTier(tier)) n += left[d];
        return n;
    }

    // Tria un personatge del tier amb pes = còpies restants i en treu una. Null si el tier és buit.
    public UnitDefinition Draw(int tier, Random rng)
    {
        int total = TierStock(tier);
        if (total <= 0) return null;
        int r = rng.Next(total);
        foreach (var d in UnitDatabase.ByTier(tier))
        {
            if (r < left[d]) { left[d]--; return d; }
            r -= left[d];
        }
        return null;
    }

    public void Return(UnitDefinition d, int copies = 1)
    {
        left[d] = Math.Min(left[d] + copies, ShopConfig.CopiesPerTier[d.Tier]);
    }
}
