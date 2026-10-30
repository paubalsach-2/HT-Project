using System.Collections.Generic;

// Què s'envia als clients d'un combat (dades pures; la xarxa ho serialitza a Net/CombatNet).
public sealed class CombatSetup
{
    public int Round;
    public bool Flipped;                    // true = el receptor és l'equip B: ha de dibuixar el marc girat 180°
    public readonly List<SimUnitSetup> Units = new List<SimUnitSetup>();
}

public struct CombatEnd
{
    public SimOutcome Outcome;              // en el marc de la simulació (A baix, B dalt)
    public int SurvivorsA, SurvivorsB, Ticks;
    public int DamageTaken;                 // vides que ha perdut el receptor
    public bool Flipped;                    // true = el receptor és l'equip B
}

public static class CombatMessages
{
    public static CombatSetup SetupFor(Fight f, int round, bool receiverIsB)
    {
        var s = new CombatSetup { Round = round, Flipped = receiverIsB };
        s.Units.AddRange(f.Setup);
        return s;
    }

    public static CombatEnd EndFor(Fight f, bool receiverIsB)
    {
        return new CombatEnd
        {
            Outcome = f.Result.Outcome,
            SurvivorsA = f.Result.SurvivorsA,
            SurvivorsB = f.Result.SurvivorsB,
            Ticks = f.Result.Ticks,
            DamageTaken = receiverIsB ? f.DamageB : f.DamageA,
            Flipped = receiverIsB
        };
    }
}
