using System.Collections.Generic;
using UnityEngine;

// Passa de coordenades de casella (c, r) a posició 3D, llegint les caselles "Tile_c_r" del
// tauler de l'escena. És l'únic lloc on el combat toca la geometria: la simulació (Core)
// només coneix HexCoord.
// Flip = true gira el marc 180° (el jugador que és l'equip B es veu a la meitat de baix).
public sealed class BoardWorld
{
    public readonly List<HexCoord> Tiles = new List<HexCoord>();
    readonly Dictionary<HexCoord, Vector3> pos = new Dictionary<HexCoord, Vector3>();

    public bool Flip;

    HexCoord Map(HexCoord c)
    {
        return Flip ? new HexCoord(BoardConfig.Columns - 1 - c.C, BoardConfig.Rows - 1 - c.R) : c;
    }

    public bool Has(HexCoord c) => pos.ContainsKey(Map(c));

    public Vector3 WorldPos(HexCoord c)
    {
        return pos.TryGetValue(Map(c), out var p) ? p : Vector3.zero;
    }

    // Null si no hi ha caselles
    public static BoardWorld Build(Transform boardRoot)
    {
        if (boardRoot == null) return null;
        var w = new BoardWorld();
        foreach (Transform child in boardRoot)
        {
            if (!TryParse(child.name, out int c, out int r)) continue;
            var rs = child.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) continue;
            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            var hc = new HexCoord(c, r);
            // mateixa alçada que fa servir BenchView per posar les unitats (cara superior + 0.02)
            w.pos[hc] = new Vector3(b.center.x, b.max.y + 0.02f, b.center.z);
            w.Tiles.Add(hc);
        }
        return w.Tiles.Count > 0 ? w : null;
    }

    static bool TryParse(string name, out int c, out int r)
    {
        c = 0; r = 0;
        var p = name.Split('_');
        return p.Length == 3 && p[0] == "Tile" && int.TryParse(p[1], out c) && int.TryParse(p[2], out r);
    }
}
