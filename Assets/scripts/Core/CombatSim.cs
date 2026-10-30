using System.Collections.Generic;

// Simulació d'UN combat, determinista: sense Unity, sense float, sense Random, sense temps real.
// La mateixa entrada dona sempre el mateix resultat, a qualsevol màquina.
//
// Ús:
//   var sim = new CombatSim(setup);
//   while (!sim.Finished) { sim.Tick(); foreach (var e in sim.LastEvents) ... }   // animació
//   var result = sim.RunToEnd();                                                  // o, només el resultat
//
// Les unitats actuen en ordre de Uid ascendent (l'ordre d'entrada no importa).
public sealed class CombatSim
{
    public readonly SimGrid Grid;
    public int TickCount { get; private set; }
    public bool Finished { get; private set; }
    public SimResult Result { get; private set; }

    readonly List<SimUnit> units = new List<SimUnit>();
    readonly List<SimEvent> events = new List<SimEvent>();

    public IReadOnlyList<SimUnit> Units => units;
    public IReadOnlyList<SimEvent> LastEvents => events;   // el que ha passat en l'últim Tick

    public CombatSim(IList<SimUnitSetup> setup)
    {
        Grid = new SimGrid(BoardConfig.Columns, BoardConfig.Rows);

        var sorted = new List<SimUnitSetup>(setup);
        sorted.Sort((a, b) => a.Uid.CompareTo(b.Uid));

        foreach (var s in sorted)
        {
            var def = UnitLookup.ById(s.DefId);
            if (def == null) continue;
            if (!Grid.IsFree(new HexCoord(s.Col, s.Row))) continue; // casella invàlida o repetida
            units.Add(new SimUnit(this, s, SimStats.For(def, s.Star)));
        }
        CheckEnd(); // equips buits: acaba al moment
    }

    public void Tick()
    {
        if (Finished) return;
        events.Clear();
        TickCount++;
        for (int i = 0; i < units.Count; i++) units[i].Tick();
        CheckEnd();
    }

    public SimResult RunToEnd()
    {
        while (!Finished) Tick();
        return Result;
    }

    internal void Emit(SimEvent e)
    {
        e.Tick = TickCount;
        events.Add(e);
    }

    void CheckEnd()
    {
        int a = 0, b = 0, hpA = 0, hpB = 0;
        foreach (var u in units)
        {
            if (!u.Alive) continue;
            if (u.Team == SimTeam.A) { a++; hpA += u.Hp; } else { b++; hpB += u.Hp; }
        }

        bool timeUp = TickCount >= SimConfig.MaxTicks;
        if (a > 0 && b > 0 && !timeUp) return;

        SimOutcome o;
        if (a > 0 && b == 0) o = SimOutcome.TeamAWon;
        else if (b > 0 && a == 0) o = SimOutcome.TeamBWon;
        else if (a == 0 && b == 0) o = SimOutcome.Draw;
        else o = hpA > hpB ? SimOutcome.TeamAWon : (hpB > hpA ? SimOutcome.TeamBWon : SimOutcome.Draw);

        Finished = true;
        Result = new SimResult { Outcome = o, SurvivorsA = a, SurvivorsB = b, Ticks = TickCount };
    }
}
