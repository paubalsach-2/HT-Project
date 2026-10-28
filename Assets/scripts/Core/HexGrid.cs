using System.Collections.Generic;
using UnityEngine;

// Model de dades de la graella hexagonal del combat.
// Mateixa disposició que HexBoard: "odd-r" (les files senars van mitja casella a la dreta).
// Les posicions es llegeixen de les caselles "Tile_c_r" que HexBoard ha generat a l'escena.
public sealed class HexGrid
{
    public readonly int Columns, Rows;
    public readonly List<Vector2Int> Tiles = new List<Vector2Int>();

    readonly bool[,] exists;
    readonly Vector3[,] world;
    readonly CombatUnit[,] occupant;

    static readonly Vector2Int[] evenDirs =
    {
        new Vector2Int(1, 0), new Vector2Int(-1, 0),
        new Vector2Int(-1, -1), new Vector2Int(0, -1),
        new Vector2Int(-1, 1), new Vector2Int(0, 1)
    };
    static readonly Vector2Int[] oddDirs =
    {
        new Vector2Int(1, 0), new Vector2Int(-1, 0),
        new Vector2Int(0, -1), new Vector2Int(1, -1),
        new Vector2Int(0, 1), new Vector2Int(1, 1)
    };

    struct TileInfo { public int c, r; public Vector3 pos; }

    HexGrid(int columns, int rows)
    {
        Columns = columns;
        Rows = rows;
        exists = new bool[columns, rows];
        world = new Vector3[columns, rows];
        occupant = new CombatUnit[columns, rows];
    }

    // ---------------------------------------------------------------- construcció
    public static HexGrid FromBoard(Transform boardRoot)
    {
        if (boardRoot == null) return null;

        var found = new List<TileInfo>();
        int maxC = -1, maxR = -1;
        foreach (Transform child in boardRoot)
        {
            if (!TryParse(child.name, out int c, out int r)) continue;
            var rs = child.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) continue;
            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            // mateixa alçada que fa servir BenchView per posar les unitats (cara superior + 0.02)
            found.Add(new TileInfo { c = c, r = r, pos = new Vector3(b.center.x, b.max.y + 0.02f, b.center.z) });
            if (c > maxC) maxC = c;
            if (r > maxR) maxR = r;
        }
        if (found.Count == 0) return null;

        var g = new HexGrid(maxC + 1, maxR + 1);
        foreach (var t in found)
        {
            g.exists[t.c, t.r] = true;
            g.world[t.c, t.r] = t.pos;
            g.Tiles.Add(new Vector2Int(t.c, t.r));
        }
        return g;
    }

    static bool TryParse(string name, out int c, out int r)
    {
        c = 0; r = 0;
        var p = name.Split('_');
        return p.Length == 3 && p[0] == "Tile" && int.TryParse(p[1], out c) && int.TryParse(p[2], out r);
    }

    // ---------------------------------------------------------------- consultes
    public bool Exists(Vector2Int p) =>
        p.x >= 0 && p.y >= 0 && p.x < Columns && p.y < Rows && exists[p.x, p.y];

    public Vector3 WorldPos(Vector2Int p) => world[p.x, p.y];

    public bool IsFree(Vector2Int p) => Exists(p) && occupant[p.x, p.y] == null;

    public void Place(CombatUnit u, Vector2Int p) { occupant[p.x, p.y] = u; }

    // Només buida la casella si la ocupa aquesta unitat
    public void Clear(Vector2Int p, CombatUnit only)
    {
        if (Exists(p) && occupant[p.x, p.y] == only) occupant[p.x, p.y] = null;
    }

    // ---------------------------------------------------------------- geometria hexagonal
    static void ToCube(Vector2Int p, out int x, out int y, out int z)
    {
        x = p.x - (p.y - (p.y & 1)) / 2;
        z = p.y;
        y = -x - z;
    }

    // Distància en caselles (0 = la mateixa, 1 = veïna...)
    public static int Distance(Vector2Int a, Vector2Int b)
    {
        ToCube(a, out int ax, out int ay, out int az);
        ToCube(b, out int bx, out int by, out int bz);
        return Mathf.Max(Mathf.Abs(ax - bx), Mathf.Abs(ay - by), Mathf.Abs(az - bz));
    }

    // Omple 'buffer' (mida >= 6) amb els veïns que existeixen; retorna quants n'hi ha
    public int GetNeighbors(Vector2Int p, Vector2Int[] buffer)
    {
        var dirs = (p.y & 1) == 0 ? evenDirs : oddDirs;
        int n = 0;
        for (int i = 0; i < 6; i++)
        {
            var q = new Vector2Int(p.x + dirs[i].x, p.y + dirs[i].y);
            if (Exists(q)) buffer[n++] = q;
        }
        return n;
    }

    // ---------------------------------------------------------------- camins
    // BFS per caselles lliures: busca la casella més propera des de la qual 'goal' queda a
    // 'range' caselles o menys, i retorna el PRIMER pas del camí. False si no hi ha camí.
    public bool TryFindStep(Vector2Int from, Vector2Int goal, int range, out Vector2Int step)
    {
        step = from;
        var visited = new bool[Columns, Rows];
        var prev = new Vector2Int[Columns, Rows];
        var queue = new Queue<Vector2Int>();
        var nb = new Vector2Int[6];

        visited[from.x, from.y] = true;
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            int n = GetNeighbors(cur, nb);
            for (int i = 0; i < n; i++)
            {
                var p = nb[i];
                if (visited[p.x, p.y] || occupant[p.x, p.y] != null) continue;
                visited[p.x, p.y] = true;
                prev[p.x, p.y] = cur;
                if (Distance(p, goal) <= range)
                {
                    var s = p;
                    while (prev[s.x, s.y] != from) s = prev[s.x, s.y];
                    step = s;
                    return true;
                }
                queue.Enqueue(p);
            }
        }
        return false;
    }
}
