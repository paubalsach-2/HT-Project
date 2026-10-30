using System;
using System.Collections.Generic;

// Equip rival generat per ronda (la "IA" de la Fase 1). Determinista: el mateix
// (ronda, seed, caselles) dona sempre el mateix equip, així el servidor i els clients coincideixen.
// Els Uid són negatius (-1, -2...) perquè no xoquin mai amb els del jugador.
public static class EnemyTeamGenerator
{
    public static List<SimUnitSetup> Generate(int round, int playerUnits, int seed, IList<HexCoord> tiles)
    {
        var result = new List<SimUnitSetup>();
        if (tiles == null || tiles.Count == 0) return result;

        var rng = new SimRandom(seed * 31 + round);

        int count = Math.Max(1, Math.Max(playerUnits, 1 + round / 2));
        count = Math.Min(count, tiles.Count);

        int maxTier = Math.Min(Math.Max(1 + round / 3, 1), ShopConfig.MaxTier);
        int star = round >= 8 ? 2 : 1;

        // caselles barrejades (Fisher-Yates amb el nostre RNG)
        var pool = new List<HexCoord>(tiles);
        for (int i = pool.Count - 1; i > 0; i--)
        {
            int j = rng.Range(i + 1);
            var tmp = pool[i]; pool[i] = pool[j]; pool[j] = tmp;
        }

        var defs = new List<UnitDefinition>();
        foreach (var d in UnitDatabase.All) if (d.Tier <= maxTier) defs.Add(d);

        for (int i = 0; i < count; i++)
        {
            var def = defs[rng.Range(defs.Count)];
            result.Add(new SimUnitSetup
            {
                Uid = -(i + 1),
                DefId = def.Id,
                Star = star,
                Team = SimTeam.B,
                Col = pool[i].C,
                Row = pool[i].R
            });
        }
        return result;
    }
}
