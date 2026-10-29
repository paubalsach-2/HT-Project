using System;
using System.Collections.Generic;

// Unitats del jugador: banc (9 espais) + la seva meitat del tauler (BoardConfig.BoardSize caselles).
// Fusiona 3 còpies iguals (al banc o al tauler) en 1 d'una estrella més.
public sealed class Bench
{
    public const int Size = ShopConfig.BenchSize;
    public const int BoardSize = BoardConfig.BoardSize;   // 7 columnes x 4 files (meitat del jugador)
    public const int SnapshotSize = BoardSize + Size;     // tauler + banc (sense l'espai d'excés)

    // Disposició: [0..BoardSize) = tauler, [BoardSize..BoardSize+Size) = banc,
    // l'últim és un espai d'excés temporal: permet comprar amb el banc ple si la compra fusiona.
    const int BenchStart = BoardSize;
    const int Overflow = BoardSize + Size;
    readonly UnitInstance[] cells = new UnitInstance[BoardSize + Size + 1];

    // Generador d'Uid. En multijugador el serveidor en passa un de compartit per a tots els jugadors.
    readonly Func<int> uidGen;
    int localUid;

    public event Action OnChanged;

    public Bench(Func<int> uidGen = null)
    {
        this.uidGen = uidGen ?? (() => localUid++);
    }

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

    // Cerca per Uid (tauler i banc). Null si no hi és.
    public UnitInstance ByUid(int uid)
    {
        if (uid < 0) return null;
        for (int i = 0; i < Overflow; i++)
            if (cells[i] != null && cells[i].Uid == uid) return cells[i];
        return null;
    }

    // Totes les unitats (tauler + banc)
    public IEnumerable<UnitInstance> Units()
    {
        for (int i = 0; i < Overflow; i++)
            if (cells[i] != null) yield return cells[i];
    }

    public bool Add(UnitDefinition d)
    {
        int free = FirstFree();
        if (free < 0 && !WouldMerge(d)) return false;
        cells[free >= 0 ? BenchStart + free : Overflow] = new UnitInstance(d, uidGen());
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

    // Buida tot (banc, tauler i excés). No retorna res al pool: això ho fa ShopSystem.ReleaseAll.
    public void Clear()
    {
        Array.Clear(cells, 0, cells.Length);
        OnChanged?.Invoke();
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

    // ---------- Snapshots ----------

    // Escriu l'estat a dst (mida mínima SnapshotSize). Totes les caselles s'omplen.
    public void ExportSnapshot(UnitSnapshot[] dst)
    {
        for (int i = 0; i < SnapshotSize; i++)
        {
            var u = cells[i];
            dst[i] = u == null
                ? UnitSnapshot.Empty
                : new UnitSnapshot { Uid = u.Uid, DefId = u.Def.Id, Star = u.Star };
        }
    }

    // Aplica un snapshot. Si una unitat ja existia (mateix Uid i personatge) es reutilitza
    // la mateixa instància, així les vistes que la referencien no es perden.
    public void ImportSnapshot(UnitSnapshot[] src)
    {
        var old = new Dictionary<int, UnitInstance>();
        for (int i = 0; i <= Overflow; i++)
            if (cells[i] != null) old[cells[i].Uid] = cells[i];

        Array.Clear(cells, 0, cells.Length);

        for (int i = 0; i < SnapshotSize && i < src.Length; i++)
        {
            var s = src[i];
            if (s.IsEmpty) continue;
            var def = UnitLookup.ById(s.DefId);
            if (def == null) continue;

            UnitInstance u;
            if (!old.TryGetValue(s.Uid, out u) || u.Def != def)
                u = new UnitInstance(def, s.Uid);
            u.Star = s.Star < 1 ? 1 : s.Star;
            cells[i] = u;
        }
        OnChanged?.Invoke();
    }

    // ---------- Interns ----------

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
