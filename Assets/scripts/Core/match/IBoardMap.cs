// Com es converteix l'índex del tauler d'un jugador (0..27) en una casella (columna, fila) de l'arena.
// La implementa la vista (llegeix la geometria de l'escena); el Core només la fa servir per construir
// els combats. La "meitat de baix" és sempre la del jugador local; la d'adalt s'obté girant 180°.
public interface IBoardMap
{
    bool TryGetCoord(int boardIndex, out int col, out int row);
}
