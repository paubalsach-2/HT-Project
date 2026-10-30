// Dona al servidor (Core) la geometria de l'arena: índex del tauler -> casella Tile_c_r.
// El servidor/host la posa a MatchState.BoardMap perquè pugui construir les formacions de combat.
public sealed class BenchBoardMap : IBoardMap
{
    readonly BenchView view;

    public BenchBoardMap(BenchView view) { this.view = view; }

    public bool TryGetCoord(int boardIndex, out int col, out int row)
    {
        return view.TryGetBoardCoord(boardIndex, out col, out row);
    }
}
