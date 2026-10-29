using System.Collections.Generic;
using UnityEngine;

// Geometria del tauler 3D: busca les caselles "Tile_c_r" i separa la meitat del jugador
// (la que queda del costat de la banqueta) de la meitat rival.
// Mine[i] = casella lògica i del tauler del jugador (0..27), ordenada: primera fila = la
// més propera a la banqueta, dins de cada fila d'esquerra a dreta.
// Més endavant el combat el reutilitzarà (Mirror per veure el rival girat 180°).
public sealed class BoardLayout
{
    public struct Tile
    {
        public int c, r;
        public Vector3 pos;      // x,z = centre; y = cara superior
        public Renderer[] rends;
    }

    public readonly List<Tile> Mine = new List<Tile>();
    public readonly List<Tile> Theirs = new List<Tile>();
    public Transform BoardRoot { get; private set; }   // arrel "Board" amb les caselles Tile_c_r
    public Vector3 Center { get; private set; }
    public float HexR { get; private set; }

    // Gira una posició 180° al voltant del centre del tauler (perspectiva del rival)
    public Vector3 Mirror(Vector3 p) => new Vector3(2f * Center.x - p.x, p.y, 2f * Center.z - p.z);

    // Retorna null si no hi ha tauler o no té caselles
    public static BoardLayout Build(Transform benchRoot, IList<Vector3> benchPositions)
    {
        Transform boardRoot = null;
        if (benchRoot.parent != null) boardRoot = benchRoot.parent.Find("Board");
        if (boardRoot == null)
        {
            var g = GameObject.Find("Board");
            if (g != null) boardRoot = g.transform;
        }
        if (boardRoot == null)
        {
            Debug.LogWarning("[BoardLayout] No s'ha trobat el tauler (\"Board\"): només es podrà moure dins del banc.");
            return null;
        }

        var all = new List<Tile>();
        Vector3 sum = Vector3.zero;
        float hexR = 0f;
        foreach (Transform child in boardRoot)
        {
            if (!ParseTile(child.name, out int c, out int r)) continue;
            var rs = child.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) continue;
            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            var t = new Tile { c = c, r = r, pos = new Vector3(b.center.x, b.max.y, b.center.z), rends = rs };
            all.Add(t);
            sum += t.pos;
            hexR = b.size.z * 0.5f;
        }
        if (all.Count == 0) return null;

        var layout = new BoardLayout { BoardRoot = boardRoot, Center = sum / all.Count, HexR = hexR };

        // La meitat del jugador és la que queda del costat de la banqueta
        Vector3 benchCenter = Vector3.zero;
        foreach (var p in benchPositions) benchCenter += p;
        if (benchPositions.Count > 0) benchCenter /= benchPositions.Count;
        Vector3 dir = benchCenter - layout.Center; dir.y = 0f;

        foreach (var t in all)
        {
            Vector3 d = t.pos - layout.Center; d.y = 0f;
            if (Vector3.Dot(d, dir) > 0.01f) layout.Mine.Add(t); else layout.Theirs.Add(t);
        }

        // Primera fila = la més propera a la banqueta
        bool lowFirst = true;
        if (layout.Mine.Count > 0)
        {
            Tile lo = layout.Mine[0], hi = layout.Mine[0];
            foreach (var t in layout.Mine)
            {
                if (t.r < lo.r) lo = t;
                if (t.r > hi.r) hi = t;
            }
            Vector3 dl = lo.pos - layout.Center, dh = hi.pos - layout.Center;
            lowFirst = Vector3.Dot(dl, dir) >= Vector3.Dot(dh, dir);
        }
        layout.Mine.Sort((a, b) =>
        {
            int ra = lowFirst ? a.r : -a.r, rb = lowFirst ? b.r : -b.r;
            return ra != rb ? ra.CompareTo(rb) : a.c.CompareTo(b.c);
        });

        return layout;
    }

    static bool ParseTile(string name, out int c, out int r)
    {
        c = 0; r = 0;
        var p = name.Split('_');
        return p.Length == 3 && p[0] == "Tile" && int.TryParse(p[1], out c) && int.TryParse(p[2], out r);
    }
}