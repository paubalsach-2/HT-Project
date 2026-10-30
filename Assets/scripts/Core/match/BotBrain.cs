using System.Collections.Generic;

// IA simple de la fase 6 (lògica pura, sense Unity). La fan servir el servidor per a:
//  - els bots (proves amb 8 sessions),
//  - els jugadors desconnectats durant el període de gràcia.
// Cada preparació: puja de nivell fins a un objectiu segons la ronda, compra el que pot i
// posa al tauler les millors unitats (més estrelles, després més cares) fins al límit del nivell.
public static class BotBrain
{
    public static void Play(PlayerSession s, int round)
    {
        if (s == null || !s.IsAlive) return;

        BuyLevels(s, round);
        BuyUnits(s);
        PlaceUnits(s);
    }

    // Objectiu de nivell: 1 + ronda/2 (ronda 2 -> 2, ronda 4 -> 3...)
    static void BuyLevels(PlayerSession s, int round)
    {
        int target = 1 + round / 2;
        for (int i = 0; i < 8; i++)
        {
            if (s.Shop.Progress.IsMax || s.Shop.Progress.Level >= target) break;
            if (s.State.Gold < ShopConfig.BuyXpCost) break;
            if (s.BuyXp() != null) break;   // qualsevol missatge = no s'ha pogut
        }
    }

    static void BuyUnits(PlayerSession s)
    {
        for (int slot = 0; slot < ShopConfig.ShopSlots; slot++)
        {
            var d = s.Shop.GetSlot(slot);
            if (d == null || d.Cost > s.State.Gold) continue;
            s.Buy(slot);   // si el banc és ple, simplement no passa res
        }
    }

    static void PlaceUnits(PlayerSession s)
    {
        var shop = s.Shop;
        var bench = shop.Bench;

        var units = new List<UnitInstance>(bench.Units());
        units.Sort((a, b) =>
        {
            int c = b.Star.CompareTo(a.Star);
            return c != 0 ? c : b.Def.Cost.CompareTo(a.Def.Cost);
        });

        foreach (var u in units)
        {
            if (bench.BoardCount >= shop.BoardLimit) break;
            if (bench.IndexOf(u) < 0) continue;     // ja és al tauler
            int idx = FirstEmptyBoardCell(bench);
            if (idx < 0) break;
            shop.MoveToBoard(u.Uid, idx);
        }
    }

    static int FirstEmptyBoardCell(Bench bench)
    {
        for (int i = 0; i < Bench.BoardSize; i++)
            if (bench.BoardAt(i) == null) return i;
        return -1;
    }
}
