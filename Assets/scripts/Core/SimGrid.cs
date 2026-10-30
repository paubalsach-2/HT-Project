using System;
using System.Collections.Generic;

// Graella hexagonal del combat (només dades i regles; les posicions 3D les posa la vista).
// Mateixa disposició que HexBoard: "odd-r".
public sealed class SimGrid
{
    public readonly int Columns, Rows;

    readonly SimUnit[,] occupant;

    static readonly HexCoord[] evenDirs =
    {
        new HexCoord(1, 0), new HexCoord(-1, 0),
        new HexCoord(-1, -1), new HexCoord(0, -1),
        new HexCoord(-1, 1), new HexCoord(0, 1)
    };
    static readonly HexCoord[] oddDirs =
    {
        new HexCoord(1, 0), new HexCoord(-1, 0),
        new HexCoord(0, -1), new HexCoord(1, -1),
        new HexCoord(0, 1), new HexCoord(1, 1)
    };

    public SimGrid(int columns, int rows)
    {
        Columns = columns;
        Rows = rows;
        occupant = new SimUnit[columns, rows];
    }

    public bool Exists(HexCoord p) => p.C >= 0 && p.R >= 0 && p.C < Columns && p.R < Rows;
    public bool IsFree(HexCoord p) => Exists(p) && occupant[p.C, p.R] == null;
    public void Place(SimUnit u, HexCoord p) { occupant[p.C, p.R] = u; }

    // Només buida la casella si la ocupa aquesta unitat
    public void Clear(HexCoord p, SimUnit only)
    {
        if (Exists(p) && occupant[p.C, p.R] == only) occupant[p.C, p.R] = null;
    }

    // ---------------------------------------------------------------- geometria
    static void ToCube(HexCoord p, out int x, out int y, out int z)
    {
        x = p.C - (p.R - (p.R & 1)) / 2;
        z = p.R;
        y = -x - z;
    }

    // Distància en caselles (0 = la mateixa, 1 = veïna...)
    public static int Distance(HexCoord a, HexCoord b)
    {
        ToCube(a, out int ax, out int ay, out int az);
        ToCube(b, out int bx, out int by, out int bz);
        return Math.Max(Math.Abs(ax - bx), Math.Max(Math.Abs(ay - by), Math.Abs(az - bz)));
    }

    // Omple 'buffer' (mida >= 6) amb els veïns que existeixen, sempre en el mateix ordre
    public int GetNeighbors(HexCoord p, HexCoord[] buffer)
    {
        var dirs = (p.R & 1) == 0 ? evenDirs : oddDirs;
        int n = 0;
        for (int i = 0; i < 6; i++)
        {
            var q = new HexCoord(p.C + dirs[i].C, p.R + dirs[i].R);
            if (Exists(q)) buffer[n++] = q;
        }
        return n;
    }

    // BFS per caselles lliures: busca la casella més propera des de la qual 'goal' queda a
    // 'range' caselles o menys, i retorna el PRIMER pas del camí. False si no hi ha camí.
    public bool TryFindStep(HexCoord from, HexCoord goal, int range, out HexCoord step)
    {
        step = from;
        var visited = new bool[Columns, Rows];
        var prev = new HexCoord[Columns, Rows];
        var queue = new Queue<HexCoord>();
        var nb = new HexCoord[6];

        visited[from.C, from.R] = true;
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            int n = GetNeighbors(cur, nb);
            for (int i = 0; i < n; i++)
            {
                var p = nb[i];
                if (visited[p.C, p.R] || occupant[p.C, p.R] != null) continue;
                visited[p.C, p.R] = true;
                prev[p.C, p.R] = cur;
                if (Distance(p, goal) <= range)
                {
                    var s = p;
                    while (prev[s.C, s.R] != from) s = prev[s.C, s.R];
                    step = s;
                    return true;
                }
                queue.Enqueue(p);
            }
        }
        return false;
    }
}
