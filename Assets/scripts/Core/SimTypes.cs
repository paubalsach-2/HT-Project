using System;

// Tipus del combat determinista (Core, sense Unity).
// Equip A = el de baix de la pantalla (el jugador); equip B = el de dalt (el rival).
public enum SimTeam { A = 0, B = 1 }

public enum SimOutcome { TeamAWon, TeamBWon, Draw }

public static class SimConfig
{
    public const int TicksPerSecond = 20;          // 1 tick = 50 ms
    public const int MaxTicks = 600;               // 30 s; passat això guanya qui té més vida total
    public const int AttackAnimTicks = 5;          // durada de l'animació d'atac
    public const int HitTick = 3;                  // tick de l'animació en què cau el cop
}

// Una unitat que entra al combat. Els Uid han de ser únics a tot el combat (els dos equips).
public struct SimUnitSetup
{
    public int Uid, DefId, Star;
    public SimTeam Team;
    public int Col, Row;
}

public struct SimResult
{
    public SimOutcome Outcome;
    public int SurvivorsA, SurvivorsB;
    public int Ticks;
}

public enum SimEventType { Move, Attack, Hit, Death }

// El que ha passat en un tick; la vista (CombatView) ho reprodueix com a animació.
public struct SimEvent
{
    public int Tick;
    public SimEventType Type;
    public int Uid;          // qui fa l'acció (o qui mor)
    public int TargetUid;    // objectiu (Attack, Hit)
    public int Amount;       // dany (Hit) o durada en ticks (Move)
    public HexCoord From, To; // Move
}

// Estadístiques en enters. De moment es dedueixen del tier, la forma i les estrelles
// (mateixos valors de prova que Gameplay/CombatStats, però sense decimals).
public struct SimStats
{
    public int MaxHp;
    public int Attack;               // dany per cop
    public int AttackCooldownTicks;  // ticks entre cops
    public int MoveTicks;            // ticks per moure's una casella
    public int Range;                // en caselles (1 = cos a cos)

    static readonly int[] HpPct = { 100, 180, 324 };   // x1, x1.8, x1.8^2
    static readonly int[] AtkPct = { 100, 150, 225 };  // x1, x1.5, x1.5^2

    public static SimStats For(UnitDefinition def, int star)
    {
        int tier = def.Tier < 1 ? 1 : def.Tier;
        int range = RangeOf(def.Shape);
        int si = star < 1 ? 0 : (star > 3 ? 2 : star - 1);

        int hp = (500 + 150 * (tier - 1)) * HpPct[si] / 100;
        int atk = (40 + 15 * (tier - 1)) * AtkPct[si] / 100;
        if (range >= 3) hp = hp * 80 / 100; // els de llarg abast són més fràgils

        return new SimStats
        {
            MaxHp = hp,
            Attack = atk,
            AttackCooldownTicks = range >= 3 ? 29 : 22, // 0,7 i 0,9 cops/s
            MoveTicks = 10,                              // 2 caselles/s
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
