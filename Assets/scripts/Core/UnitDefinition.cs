// Definició d'un personatge (de moment només una forma + color).
public enum UnitShape { Triangle, Square, Diamond, Pentagon, Hexagon, Octagon, Circle, Star, Cross }

public sealed class UnitDefinition
{
    public readonly int Id;
    public readonly string Name;
    public readonly int Tier;
    public readonly UnitShape Shape;
    public readonly float R, G, B;

    public int Cost => ShopConfig.CostOfTier(Tier);

    public UnitDefinition(int id, string name, int tier, UnitShape shape, float r, float g, float b)
    {
        Id = id; Name = name; Tier = tier; Shape = shape; R = r; G = g; B = b;
    }
}

// Còpia concreta d'un personatge al banc o al tauler (amb estrelles).
// Uid: identificador estable dins de la partida (assignat pel generador del Bench).
public sealed class UnitInstance
{
    public readonly UnitDefinition Def;
    public readonly int Uid;
    public int Star = 1;

    public UnitInstance(UnitDefinition def, int uid) { Def = def; Uid = uid; }

    // Còpies que equivalen a aquesta unitat (1, 3, 9)
    public int CopiesWorth => Star == 1 ? 1 : (Star == 2 ? 3 : 9);

    // Com al TFT: cost * còpies, i -1 d'or si té més d'1 estrella i costa més d'1
    public int SellValue
    {
        get
        {
            int v = Def.Cost * CopiesWorth;
            if (Star > 1 && Def.Cost > 1) v -= 1;
            return v;
        }
    }
}
