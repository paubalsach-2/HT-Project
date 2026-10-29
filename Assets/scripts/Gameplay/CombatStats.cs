using UnityEngine;

// Estadístiques de combat d'una unitat.
// De moment es dedueixen del tier, la forma i les estrelles (valors de prova per poder
// provar el combat). Més endavant es poden posar a mà a cada UnitDefinition.
public struct CombatStats
{
    public float MaxHp;
    public float Attack;       // dany per cop
    public float AttackSpeed;  // cops per segon
    public float MoveSpeed;    // caselles per segon
    public int Range;          // en caselles (1 = cos a cos)

    public static CombatStats For(UnitDefinition def, int star)
    {
        int tier = Mathf.Max(1, def.Tier);
        int range = RangeOf(def.Shape);
        float s = Mathf.Max(0, star - 1);

        float hp = (500f + 150f * (tier - 1)) * Mathf.Pow(1.8f, s);
        float atk = (40f + 15f * (tier - 1)) * Mathf.Pow(1.5f, s);
        if (range >= 3) hp *= 0.8f; // els de llarg abast són més fràgils

        return new CombatStats
        {
            MaxHp = hp,
            Attack = atk,
            AttackSpeed = range >= 3 ? 0.7f : 0.9f,
            MoveSpeed = 2f,
            Range = range
        };
    }

    static int RangeOf(UnitShape shape)
    {
        switch (shape)
        {
            case UnitShape.Triangle:
            case UnitShape.Star:
                return 4;
            case UnitShape.Circle:
            case UnitShape.Diamond:
                return 2;
            default:
                return 1;
        }
    }
}
