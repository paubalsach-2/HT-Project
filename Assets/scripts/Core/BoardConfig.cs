// Mida del tauler: única font de veritat per a Core, vista i (més endavant) combat.
// HexBoard.columns / HexBoard.rows han de coincidir amb Columns / Rows.
public static class BoardConfig
{
    public const int Columns = 7;
    public const int Rows = 8;                          // tauler complet (les dues meitats)
    public const int HalfRows = Rows / 2;               // meitat d'un jugador
    public const int BoardSize = Columns * HalfRows;    // 28 caselles per jugador
}
