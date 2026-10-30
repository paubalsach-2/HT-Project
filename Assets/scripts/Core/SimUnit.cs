using System;

// Una unitat dins del combat determinista: vida, objectiu, moviment i atac, tot en ticks i enters.
public sealed class SimUnit
{
    public readonly int Uid, DefId, Star;
    public readonly SimTeam Team;
    public readonly SimStats Stats;

    public int Hp { get; private set; }
    public bool Alive => Hp > 0;
    public int DamageDealt { get; private set; }
    public HexCoord Coord { get; private set; }
    public const int NoTarget = int.MinValue;   // cap Uid real el pot tenir (els rivals son -1, -2...)
    public int TargetUid => target != null ? target.Uid : NoTarget;

    enum State { Idle, Moving, Attacking }

    readonly CombatSim sim;
    readonly HexCoord[] nb = new HexCoord[6];

    State state;
    int stateTicks;
    int cooldown;
    bool hitDone;
    SimUnit target;

    internal SimUnit(CombatSim sim, SimUnitSetup s, SimStats stats)
    {
        this.sim = sim;
        Uid = s.Uid;
        DefId = s.DefId;
        Star = s.Star;
        Team = s.Team;
        Stats = stats;
        Hp = stats.MaxHp;
        Coord = new HexCoord(s.Col, s.Row);
        sim.Grid.Place(this, Coord);
    }

    internal void Tick()
    {
        if (!Alive) return;
        if (cooldown > 0) cooldown--;

        switch (state)
        {
            case State.Moving:
                stateTicks++;
                if (stateTicks >= Stats.MoveTicks) state = State.Idle;
                break;
            case State.Attacking:
                AdvanceAttack();
                break;
            default:
                Decide();
                break;
        }
    }

    // Lliure: tria objectiu, ataca si hi arriba o s'hi acosta
    void Decide()
    {
        if (target == null || !target.Alive) target = FindTarget();
        if (target == null) return;

        int dist = SimGrid.Distance(Coord, target.Coord);
        if (dist <= Stats.Range)
        {
            if (cooldown <= 0) StartAttack();
            return;
        }

        if (sim.Grid.TryFindStep(Coord, target.Coord, Stats.Range, out var next) || TryFallbackStep(out next))
            StartMove(next);
    }

    // L'enemic viu més proper; en cas d'empat, el de Uid més baix (la llista va ordenada per Uid)
    SimUnit FindTarget()
    {
        SimUnit best = null;
        int bestD = int.MaxValue;
        var units = sim.Units;
        for (int i = 0; i < units.Count; i++)
        {
            var u = units[i];
            if (u.Team == Team || !u.Alive) continue;
            int d = SimGrid.Distance(Coord, u.Coord);
            if (d < bestD) { best = u; bestD = d; }
        }
        return best;
    }

    // Si el camí complet està bloquejat, almenys intenta acostar-se una casella
    bool TryFallbackStep(out HexCoord next)
    {
        next = Coord;
        int cur = SimGrid.Distance(Coord, target.Coord);
        int best = cur;
        int n = sim.Grid.GetNeighbors(Coord, nb);
        for (int i = 0; i < n; i++)
        {
            if (!sim.Grid.IsFree(nb[i])) continue;
            int d = SimGrid.Distance(nb[i], target.Coord);
            if (d < best) { best = d; next = nb[i]; }
        }
        return best < cur;
    }

    void StartMove(HexCoord next)
    {
        var from = Coord;
        sim.Grid.Clear(Coord, this);
        Coord = next;
        sim.Grid.Place(this, next); // la casella queda reservada des del principi
        state = State.Moving;
        stateTicks = 0;
        sim.Emit(new SimEvent { Type = SimEventType.Move, Uid = Uid, From = from, To = next, Amount = Stats.MoveTicks });
    }

    void StartAttack()
    {
        state = State.Attacking;
        stateTicks = 0;
        hitDone = false;
        cooldown = Stats.AttackCooldownTicks;
        sim.Emit(new SimEvent { Type = SimEventType.Attack, Uid = Uid, TargetUid = target.Uid });
    }

    void AdvanceAttack()
    {
        stateTicks++;
        if (!hitDone && stateTicks >= SimConfig.HitTick)
        {
            hitDone = true;
            if (target != null && target.Alive)
            {
                sim.Emit(new SimEvent { Type = SimEventType.Hit, Uid = Uid, TargetUid = target.Uid, Amount = Stats.Attack });
                DamageDealt += target.TakeDamage(Stats.Attack);
            }
        }
        if (stateTicks >= SimConfig.AttackAnimTicks) state = State.Idle;
    }

    // Retorna el dany realment fet (sense comptar el que sobra en matar)
    int TakeDamage(int amount)
    {
        if (!Alive) return 0;
        int dealt = Math.Min(amount, Hp);
        Hp -= amount;
        if (Hp <= 0)
        {
            Hp = 0;
            sim.Grid.Clear(Coord, this);
            sim.Emit(new SimEvent { Type = SimEventType.Death, Uid = Uid });
        }
        return dealt;
    }
}
