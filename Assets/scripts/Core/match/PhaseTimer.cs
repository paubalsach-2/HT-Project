using System;

public enum MatchPhase { Prep, Combat, Resolve }

// Rellotge de la partida: Prep -> Combat -> Resolve -> Prep (ronda + 1)...
// Treballa en ticks de 50 ms (SimConfig.TicksPerSecond), igual que el combat, sense Unity.
//  - Prep: temps per comprar i col·locar; en acabar passa sol a Combat.
//  - Combat: dura el que triga el combat; qui el controla crida EndCombat().
//  - Resolve: petita pausa per veure el resultat; en acabar comença la ronda següent.
public sealed class PhaseTimer
{
    public int PrepTicks = 30 * SimConfig.TicksPerSecond;
    public int ResolveTicks = 3 * SimConfig.TicksPerSecond;

    public MatchPhase Phase { get; private set; } = MatchPhase.Prep;
    public int Round { get; private set; } = 1;
    public int TicksLeft { get; private set; }          // 0 durant el combat (no té límit propi)
    public bool Started { get; private set; }

    // (nova fase, ronda). Es dispara també en començar (Prep, ronda 1).
    public event Action<MatchPhase, int> OnPhaseChanged;

    public double SecondsLeft => TicksLeft / (double)SimConfig.TicksPerSecond;

    public void Start()
    {
        Started = true;
        Round = 1;
        Enter(MatchPhase.Prep);
    }

    public void Tick()
    {
        if (!Started || Phase == MatchPhase.Combat) return;
        if (TicksLeft > 0) TicksLeft--;
        if (TicksLeft > 0) return;

        if (Phase == MatchPhase.Prep)
        {
            Enter(MatchPhase.Combat);
        }
        else // Resolve
        {
            Round++;
            Enter(MatchPhase.Prep);
        }
    }

    // El jugador (o el servidor) acaba la preparació abans d'hora
    public void SkipPrep()
    {
        if (Started && Phase == MatchPhase.Prep) Enter(MatchPhase.Combat);
    }

    // El combat s'ha acabat
    public void EndCombat()
    {
        if (Started && Phase == MatchPhase.Combat) Enter(MatchPhase.Resolve);
    }

    void Enter(MatchPhase p)
    {
        Phase = p;
        TicksLeft = p == MatchPhase.Prep ? PrepTicks : (p == MatchPhase.Resolve ? ResolveTicks : 0);
        OnPhaseChanged?.Invoke(p, Round);
    }
}
