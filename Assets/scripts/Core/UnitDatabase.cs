using System;
using System.Collections.Generic;

// 25 personatges de prova (5 per tier), cadascun amb una forma i un color diferents.
public static class UnitDatabase
{
    public static readonly IReadOnlyList<UnitDefinition> All;
    static readonly List<UnitDefinition>[] byTier = new List<UnitDefinition>[ShopConfig.MaxTier + 1];

    static readonly string[] tierTitle = { "", "Peó", "Cavaller", "Alfil", "Torre", "Reina" };

    static readonly string[] shapeName =
        { "Triangle", "Quadrat", "Rombe", "Pentàgon", "Hexàgon", "Octàgon", "Cercle", "Estrella", "Creu" };

    static readonly UnitShape[][] shapesByTier =
    {
        null,
        new[] { UnitShape.Triangle, UnitShape.Square,   UnitShape.Circle,  UnitShape.Diamond,  UnitShape.Pentagon },
        new[] { UnitShape.Square,   UnitShape.Hexagon,  UnitShape.Cross,   UnitShape.Circle,   UnitShape.Triangle },
        new[] { UnitShape.Pentagon, UnitShape.Star,     UnitShape.Diamond, UnitShape.Hexagon,  UnitShape.Octagon },
        new[] { UnitShape.Cross,    UnitShape.Star,     UnitShape.Octagon, UnitShape.Hexagon,  UnitShape.Diamond },
        new[] { UnitShape.Star,     UnitShape.Cross,    UnitShape.Circle,  UnitShape.Pentagon, UnitShape.Octagon },
    };

    static UnitDatabase()
    {
        var all = new List<UnitDefinition>();
        for (int t = 1; t <= ShopConfig.MaxTier; t++)
        {
            byTier[t] = new List<UnitDefinition>();
            foreach (var shape in shapesByTier[t])
            {
                int id = all.Count;
                HsvToRgb(id / 25f, 0.6f, 0.95f, out float r, out float g, out float b);
                var def = new UnitDefinition(id, tierTitle[t] + " " + shapeName[(int)shape], t, shape, r, g, b);
                all.Add(def);
                byTier[t].Add(def);
            }
        }
        All = all;
    }

    public static IReadOnlyList<UnitDefinition> ByTier(int tier) => byTier[tier];

    static void HsvToRgb(float h, float s, float v, out float r, out float g, out float b)
    {
        float k = h * 6f;
        int i = (int)Math.Floor(k) % 6;
        float f = k - (float)Math.Floor(k);
        float p = v * (1f - s), q = v * (1f - f * s), t = v * (1f - (1f - f) * s);
        switch (i)
        {
            case 0: r = v; g = t; b = p; break;
            case 1: r = q; g = v; b = p; break;
            case 2: r = p; g = v; b = t; break;
            case 3: r = p; g = q; b = v; break;
            case 4: r = t; g = p; b = v; break;
            default: r = v; g = p; b = q; break;
        }
    }
}
