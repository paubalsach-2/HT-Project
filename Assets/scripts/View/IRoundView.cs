// El que PhaseUI necessita per dibuixar el rellotge. El compleixen:
//  - RoundManager (mode un jugador: el rellotge és local),
//  - NetRoundView (multijugador: el rellotge arriba del servidor).
public interface IRoundView
{
    bool Waiting { get; }          // la partida encara no ha començat (només xarxa)
    MatchPhase Phase { get; }
    int Round { get; }
    double SecondsLeft { get; }    // temps que queda a la fase actual

    bool CanSkipPrep { get; }      // botó "Combatre ara"
    void SkipPrep();

    // Avisos opcionals (en xarxa, els ingressos arriben com a toast des del servidor)
    float IncomeShownAt { get; }
    int LastIncomeBase { get; }
    int LastIncomeInterest { get; }
    bool DamageShown { get; }
    int LastDamage { get; }
}
