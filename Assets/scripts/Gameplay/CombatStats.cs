// Estadístiques de combat per MOSTRAR (panell d'informació i inspector). Els números reals
// del combat són els de SimStats (Core); aquí només es converteixen a "cops/s" i "caselles/s".
public struct CombatStats
{
    public float MaxHp;
    public float Attack;       // dany per cop
    public float AttackSpeed;  // cops per segon
    public float MoveSpeed;    // caselles per segon
    public int Range;          // en caselles (1 = cos a cos)

    public static CombatStats For(UnitDefinition def, int star)
    {
        var s = SimStats.For(def, star);
        return new CombatStats
        {
            MaxHp = s.MaxHp,
            Attack = s.Attack,
            AttackSpeed = SimConfig.TicksPerSecond / (float)s.AttackCooldownTicks,
            MoveSpeed = SimConfig.TicksPerSecond / (float)s.MoveTicks,
            Range = s.Range
        };
    }
}
