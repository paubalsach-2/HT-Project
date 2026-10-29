using System;

// Estat d'una casella (tauler o banc). DefId = -1 si és buida.
public struct UnitSnapshot
{
    public int Uid;
    public int DefId;
    public int Star;

    public static readonly UnitSnapshot Empty = new UnitSnapshot { Uid = -1, DefId = -1, Star = 0 };
    public bool IsEmpty => DefId < 0;
}

// Estat privat d'un jugador: es pot exportar des del servidor i aplicar al mirall del client.
// L'or s'envia però NO l'aplica ShopSystem.ApplySnapshot: l'or viu a PlayerStats / IWallet.
public sealed class PlayerSnapshot
{
    public int Gold, Level, Xp;
    public int[] ShopDefIds = new int[ShopConfig.ShopSlots];            // -1 = buit
    public UnitSnapshot[] Cells = new UnitSnapshot[Bench.SnapshotSize]; // [0..28) tauler, [28..37) banc
}

// Cerca d'un UnitDefinition per Id (els Id són els índexs de UnitDatabase.All).
public static class UnitLookup
{
    public static UnitDefinition ById(int id)
    {
        if (id < 0 || id >= UnitDatabase.All.Count) return null;
        var d = UnitDatabase.All[id];
        return d.Id == id ? d : null;
    }
}
