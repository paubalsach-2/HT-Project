using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Formes dels personatges de prova: polígons, malles (prismes) i icones 2D.
public static class UnitShapes
{
    static readonly Dictionary<UnitShape, Vector2[]> polys = new Dictionary<UnitShape, Vector2[]>();
    static readonly Dictionary<UnitShape, Mesh> meshes = new Dictionary<UnitShape, Mesh>();
    static readonly Dictionary<UnitShape, Sprite> icons = new Dictionary<UnitShape, Sprite>();

    public static Color ColorOf(UnitDefinition d) => new Color(d.R, d.G, d.B, 1f);

    // Polígon de radi 1, en sentit antihorari, "estrellat" des de l'origen
    public static Vector2[] Polygon(UnitShape s)
    {
        if (polys.TryGetValue(s, out var p)) return p;
        switch (s)
        {
            case UnitShape.Triangle: p = Regular(3, 90f); break;
            case UnitShape.Square: p = Regular(4, 45f); break;
            case UnitShape.Diamond: p = Regular(4, 0f); break;
            case UnitShape.Pentagon: p = Regular(5, 90f); break;
            case UnitShape.Hexagon: p = Regular(6, 0f); break;
            case UnitShape.Octagon: p = Regular(8, 22.5f); break;
            case UnitShape.Circle: p = Regular(32, 0f); break;
            case UnitShape.Star:
                p = new Vector2[10];
                for (int i = 0; i < 10; i++)
                {
                    float a = (90f + i * 36f) * Mathf.Deg2Rad, r = (i % 2 == 0) ? 1f : 0.45f;
                    p[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                }
                break;
            default: // Cross
                const float w = 0.34f;
                p = new[]
                {
                    new Vector2(1, -w), new Vector2(1, w), new Vector2(w, w), new Vector2(w, 1),
                    new Vector2(-w, 1), new Vector2(-w, w), new Vector2(-1, w), new Vector2(-1, -w),
                    new Vector2(-w, -w), new Vector2(-w, -1), new Vector2(w, -1), new Vector2(w, -w)
                };
                break;
        }
        polys[s] = p;
        return p;
    }

    static Vector2[] Regular(int n, float offsetDeg)
    {
        var p = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            float a = (offsetDeg + i * 360f / n) * Mathf.Deg2Rad;
            p[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        }
        return p;
    }

    // Prisma de base = polígon (radi 1, pla XZ) i alçada 1 (y de 0 a 1)
    public static Mesh Prism(UnitShape s)
    {
        if (meshes.TryGetValue(s, out var cached) && cached != null) return cached;

        var p = Polygon(s);
        int n = p.Length;
        var v = new List<Vector3>();
        var nr = new List<Vector3>();
        var t = new List<int>();

        for (int cap = 0; cap < 2; cap++) // 0 = tapa superior, 1 = inferior
        {
            float y = cap == 0 ? 1f : 0f;
            Vector3 normal = cap == 0 ? Vector3.up : Vector3.down;
            int c = v.Count;
            v.Add(new Vector3(0f, y, 0f)); nr.Add(normal);
            for (int i = 0; i < n; i++) { v.Add(new Vector3(p[i].x, y, p[i].y)); nr.Add(normal); }
            for (int i = 0; i < n; i++)
            {
                int a = c + 1 + i, b = c + 1 + (i + 1) % n;
                if (cap == 0) { t.Add(c); t.Add(b); t.Add(a); }
                else { t.Add(c); t.Add(a); t.Add(b); }
            }
        }

        for (int i = 0; i < n; i++) // costats plans
        {
            Vector2 a = p[i], b = p[(i + 1) % n], d = b - a;
            Vector3 nm = new Vector3(d.y, 0f, -d.x).normalized;
            int k = v.Count;
            v.Add(new Vector3(a.x, 0f, a.y)); v.Add(new Vector3(a.x, 1f, a.y));
            v.Add(new Vector3(b.x, 1f, b.y)); v.Add(new Vector3(b.x, 0f, b.y));
            for (int j = 0; j < 4; j++) nr.Add(nm);
            t.Add(k); t.Add(k + 1); t.Add(k + 2);
            t.Add(k); t.Add(k + 2); t.Add(k + 3);
        }

        var m = new Mesh { name = "Prism_" + s };
        m.SetVertices(v);
        m.SetNormals(nr);
        m.SetTriangles(t, 0);
        m.RecalculateBounds();
        meshes[s] = m;
        return m;
    }

    // Icona 2D (blanca amb vora fosca) per a les targetes; es tinta amb el color del personatge
    public static Sprite Icon(UnitShape s)
    {
        if (icons.TryGetValue(s, out var cached) && cached != null) return cached;

        const int N = 96;
        var poly = Polygon(s);
        var cols = new Color[N * N];
        var dark = new Color(0.1f, 0.08f, 0.06f, 1f);
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                int outer = 0, inner = 0;
                for (int sy = 0; sy < 3; sy++)
                    for (int sx = 0; sx < 3; sx++)
                    {
                        var q = new Vector2(((x + (sx + 0.5f) / 3f) / N * 2f - 1f) / 0.92f,
                                            ((y + (sy + 0.5f) / 3f) / N * 2f - 1f) / 0.92f);
                        if (Inside(poly, q)) outer++;
                        if (Inside(poly, q / 0.86f)) inner++;
                    }
                var c = Color.Lerp(dark, Color.white, inner / 9f);
                c.a = outer / 9f;
                cols[y * N + x] = c;
            }

        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave
        };
        tex.SetPixels(cols);
        tex.Apply();
        var sp = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
        icons[s] = sp;
        return sp;
    }

    static bool Inside(Vector2[] p, Vector2 q)
    {
        bool c = false;
        for (int i = 0, j = p.Length - 1; i < p.Length; j = i++)
        {
            if (((p[i].y > q.y) != (p[j].y > q.y)) &&
                (q.x < (p[j].x - p[i].x) * (q.y - p[i].y) / (p[j].y - p[i].y) + p[i].x))
                c = !c;
        }
        return c;
    }

    // Material del pipeline actiu (URP/HDRP/Built-in) amb el color indicat
    public static Material MakeMaterial(Color c)
    {
        Material src = GraphicsSettings.currentRenderPipeline != null
            ? GraphicsSettings.currentRenderPipeline.defaultMaterial : null;
        var m = src != null ? new Material(src) : new Material(Shader.Find("Standard"));
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        return m;
    }

    // Material pla sense llum (es veu brillant, com un llum) per als contorns de les zones
    public static Material MakeFlat(Color c)
    {
        Shader s = Shader.Find("Universal Render Pipeline/Unlit");
        if (s == null) s = Shader.Find("HDRP/Unlit");
        if (s == null) s = Shader.Find("Unlit/Color");
        if (s == null) return MakeMaterial(c);
        var m = new Material(s);
        SetFlatColor(m, c);
        return m;
    }

    public static void SetFlatColor(Material m, Color c)
    {
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_UnlitColor")) m.SetColor("_UnlitColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
    }
}