using System;

// Unitats del jugador: banc (9 espais) + la seva meitat del tauler (7x4 = 28 caselles).
// Fusiona 3 còpies iguals (al banc o al tauler) en 1 d'una estrella més.
public sealed class Bench
{
    public const int Size = ShopConfig.BenchSize;
    public const int BoardSize = 28; // 7 columnes x 4 files (meitat del jugador)

    // Disposició: [0..BoardSize) = tauler, [BoardSize..BoardSize+Size) = banc,
    // l'últim és un espai d'excés temporal: permet comprar amb el banc ple si la compra fusiona.
    const int BenchStart = BoardSize;
    const int Overflow = BoardSize + Size;
    readonly UnitInstance[] cells = new UnitInstance[BoardSize + Size + 1];

    public event Action OnChanged;

    public UnitInstance this[int i] => cells[BenchStart + i];  // banc
    public UnitInstance BoardAt(int i) => cells[i];            // tauler

    public bool HasFree => FirstFree() >= 0;
    public bool WouldMerge(UnitDefinition d) => Count(d, 1) >= 2;
    public bool CanAdd(UnitDefinition d) => HasFree || WouldMerge(d);

    public int BoardCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < BoardSize; i++) if (cells[i] != null) n++;
            return n;
        }
    }

    // Posició al banc (0..Size-1) o -1
    public int IndexOf(UnitInstance u)
    {
        for (int i = 0; i < Size; i++) if (cells[BenchStart + i] == u) return i;
        return -1;
    }

    // Posició al tauler (0..BoardSize-1) o -1
    public int BoardIndexOf(UnitInstance u)
    {
        for (int i = 0; i < BoardSize; i++) if (cells[i] == u) return i;
        return -1;
    }

    public bool Contains(UnitInstance u) => u != null && Find(u) >= 0;

    public bool Add(UnitDefinition d)
    {
        int free = FirstFree();
        if (free < 0 && !WouldMerge(d)) return false;
        cells[free >= 0 ? BenchStart + free : Overflow] = new UnitInstance(d);
        Merge(d);
        OnChanged?.Invoke();
        return true;
    }

    // Treu la unitat d'on sigui (banc o tauler)
    public bool Remove(UnitInstance u)
    {
        int i = Find(u);
        if (i < 0) return false;
        cells[i] = null;
        OnChanged?.Invoke();
        return true;
    }

    // Mou la unitat a una casella del tauler (si està ocupada, s'intercanvien)
    public bool MoveToBoard(UnitInstance u, int boardIndex)
    {
        if (boardIndex < 0 || boardIndex >= BoardSize) return false;
        return Swap(Find(u), boardIndex);
    }

    // Mou la unitat a un espai del banc (si està ocupat, s'intercanvien)
    public bool MoveToBench(UnitInstance u, int benchIndex)
    {
        if (benchIndex < 0 || benchIndex >= Size) return false;
        return Swap(Find(u), BenchStart + benchIndex);
    }

    bool Swap(int from, int to)
    {
        if (from < 0 || from == to) return false;
        var tmp = cells[to];
        cells[to] = cells[from];
        cells[from] = tmp;
        OnChanged?.Invoke();
        return true;
    }

    int Find(UnitInstance u)
    {
        if (u == null) return -1;
        for (int i = 0; i < Overflow; i++) if (cells[i] == u) return i;
        return -1;
    }

    int FirstFree()
    {
        for (int i = 0; i < Size; i++) if (cells[BenchStart + i] == null) return i;
        return -1;
    }

    int Count(UnitDefinition d, int star)
    {
        int n = 0;
        for (int i = 0; i <= Overflow; i++)
            if (cells[i] != null && cells[i].Def == d && cells[i].Star == star) n++;
        return n;
    }

    // Compta tauler + banc; la unitat que es queda és la primera trobada (el tauler va primer)
    void Merge(UnitDefinition d)
    {
        for (int star = 1; star < 3; star++)
        {
            int a = -1, b = -1, c = -1;
            for (int i = 0; i <= Overflow && c < 0; i++)
            {
                var u = cells[i];
                if (u == null || u.Def != d || u.Star != star) continue;
                if (a < 0) a = i; else if (b < 0) b = i; else c = i;
            }
            if (c < 0) break;
            cells[b] = null;
            cells[c] = null;
            cells[a].Star = star + 1;
        }
    }
}