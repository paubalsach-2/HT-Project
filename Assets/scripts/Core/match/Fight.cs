using System.Collections.Generic;

// Un combat d'una ronda, decidit i simulat pel SERVIDOR.
// Marc de la simulació: l'equip A és a la meitat de baix i el B a la d'adalt (formació girada 180°).
// Els clients simulen exactament el mateix Setup per fer l'animació; el resultat és el del servidor.
public sealed class Fight
{
    public ulong A;                 // jugador real (sempre)
    public ulong B;                 // rival real (només si RealB)
    public bool BIsGhost;           // el rival és una còpia del tauler d'un altre jugador
    public ulong GhostOf;           // de qui és la còpia (si BIsGhost)
    public bool BIsAI;              // no hi ha ningú més per copiar: equip generat (partida d'1 jugador)

    public readonly List<SimUnitSetup> Setup = new List<SimUnitSetup>();
    public SimResult Result;
    public int DamageA, DamageB;    // vides perdudes en acabar

    public bool RealB => !BIsGhost && !BIsAI;
}
