using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;

// Posa les unitats del jugador sobre les caselles 3D (banqueta + meitat del jugador del tauler)
// i deixa arrossegar-les amb el ratolí. Mentre s'arrossega, les zones s'il·luminen:
// verd = es pot deixar, vermell = zona prohibida (meitat enemiga).
public class BenchView : MonoBehaviour
{
    class Cell
    {
        public Vector3 pos;      // on es posa la unitat (damunt la casella)
        public float top;        // alçada de la cara superior de la casella
        public bool isBench;
        public bool valid;       // es pot deixar una unitat aquí
        public int index;        // índex al banc o al tauler (-1 si no és vàlida)
        public GameObject ring, fill;
        public Renderer fillRenderer;
    }

    struct BoardTile
    {
        public int c, r;
        public Vector3 pos;      // x,z = centre; y = cara superior
        public Renderer[] rends;
    }

    static readonly Color validColor = new Color(0.25f, 1f, 0.45f, 1f);
    static readonly Color invalidColor = new Color(1f, 0.2f, 0.2f, 1f);
    static readonly Color hoverOkColor = new Color(0.55f, 1f, 0.7f, 1f);
    static readonly Color hoverBadColor = new Color(0.8f, 0.1f, 0.1f, 1f);
    static readonly Color enemyTint = new Color(0.9f, 0.18f, 0.18f, 1f);

    public bool Locked;                       // derrota: res no es pot moure
    public event Action<string> OnMessage;
    public bool IsDragging => isDragging;

    Bench bench;
    Camera cam;
    readonly List<Cell> benchCells = new List<Cell>();
    readonly List<Cell> boardCells = new List<Cell>();   // només la meitat del jugador
    readonly List<Cell> enemyCells = new List<Cell>();   // meitat enemiga (prohibida)
    readonly List<Cell> allCells = new List<Cell>();
    readonly Dictionary<UnitInstance, UnitView> views = new Dictionary<UnitInstance, UnitView>();
    float tileRadius = 0.5f;
    float hexR = 0.87f;
    float planeY;

    Mesh ringMesh, fillMesh;
    Material matValid, matInvalid, matHoverOk, matHoverBad;

    bool isDragging;
    UnitView dragging;
    Cell hover;

    // ---------------------------------------------------------------- inici
    public void Init(Bench bench, Transform benchRoot, Camera cam)
    {
        this.bench = bench;
        this.cam = cam;

        // Caselles de la banqueta, ordenades d'esquerra a dreta a la pantalla
        float sizeSum = 0f, hexSum = 0f;
        var tiles = new List<Vector3>(); // x,z = centre; y = cara superior
        foreach (Transform child in benchRoot)
        {
            var rs = child.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) continue;
            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            tiles.Add(new Vector3(b.center.x, b.max.y, b.center.z));
            sizeSum += Mathf.Min(b.size.x, b.size.z);
            hexSum += b.size.z * 0.5f;
        }
        tiles.Sort((a, c) => cam.transform.InverseTransformPoint(a).x.CompareTo(cam.transform.InverseTransformPoint(c).x));
        if (tiles.Count > 0)
        {
            tileRadius = sizeSum / tiles.Count * 0.5f * 0.62f;
            hexR = hexSum / tiles.Count;
        }
        if (tiles.Count < Bench.Size)
            Debug.LogWarning("[BenchView] La banqueta té " + tiles.Count + " caselles i el banc en necessita " + Bench.Size + ".");
        for (int i = 0; i < tiles.Count && i < Bench.Size; i++)
            AddCell(benchCells, tiles[i], true, true, i);

        BuildBoardCells(benchRoot);

        float ySum = 0f;
        foreach (var c in allCells) ySum += c.pos.y;
        planeY = allCells.Count > 0 ? ySum / allCells.Count : 0f;

        BuildOverlays();

        bench.OnChanged += Sync;
        Sync();
    }

    // Busca el tauler (germà de la banqueta) i separa la meitat del jugador de l'enemiga
    void BuildBoardCells(Transform benchRoot)
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
            Debug.LogWarning("[BenchView] No s'ha trobat el tauler (\"Board\"): només es podrà moure dins del banc.");
            return;
        }

        var list = new List<BoardTile>();
        Vector3 sum = Vector3.zero;
        foreach (Transform child in boardRoot)
        {
            if (!ParseTile(child.name, out int c, out int r)) continue;
            var rs = child.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) continue;
            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            var bt = new BoardTile { c = c, r = r, pos = new Vector3(b.center.x, b.max.y, b.center.z), rends = rs };
            list.Add(bt);
            sum += bt.pos;
            if (benchCells.Count == 0) hexR = b.size.z * 0.5f;
        }
        if (list.Count == 0) return;

        // La meitat del jugador és la que queda del costat de la banqueta
        Vector3 boardCenter = sum / list.Count;
        Vector3 benchCenter = Vector3.zero;
        foreach (var c in benchCells) benchCenter += c.pos;
        if (benchCells.Count > 0) benchCenter /= benchCells.Count;
        Vector3 dir = benchCenter - boardCenter; dir.y = 0f;

        var mine = new List<BoardTile>();
        var theirs = new List<BoardTile>();
        foreach (var t in list)
        {
            Vector3 d = t.pos - boardCenter; d.y = 0f;
            if (Vector3.Dot(d, dir) > 0.01f) mine.Add(t); else theirs.Add(t);
        }

        // Ordre: primera fila = la més propera a la banqueta; dins de cada fila, d'esquerra a dreta
        bool lowFirst = true;
        if (mine.Count > 0)
        {
            BoardTile lo = mine[0], hi = mine[0];
            foreach (var t in mine)
            {
                if (t.r < lo.r) lo = t;
                if (t.r > hi.r) hi = t;
            }
            Vector3 dl = lo.pos - boardCenter, dh = hi.pos - boardCenter;
            lowFirst = Vector3.Dot(dl, dir) >= Vector3.Dot(dh, dir);
        }
        mine.Sort((a, b) =>
        {
            int ra = lowFirst ? a.r : -a.r, rb = lowFirst ? b.r : -b.r;
            return ra != rb ? ra.CompareTo(rb) : a.c.CompareTo(b.c);
        });

        for (int i = 0; i < mine.Count && i < Bench.BoardSize; i++)
            AddCell(boardCells, mine[i].pos, false, true, i);
        foreach (var t in theirs)
            AddCell(enemyCells, t.pos, false, false, -1);

        // Camp rival en vermell (còpia del material: no toca els assets)
        foreach (var t in theirs) TintRed(t.rends);
        var eb = GameObject.Find("EnemyBench");
        if (eb != null) TintRed(eb.GetComponentsInChildren<Renderer>());
    }

    static void TintRed(Renderer[] rends, float amount = 0.65f)
    {
        foreach (var r in rends)
        {
            var m = r.material;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.Lerp(m.GetColor("_BaseColor"), enemyTint, amount));
            if (m.HasProperty("_Color")) m.SetColor("_Color", Color.Lerp(m.GetColor("_Color"), enemyTint, amount));
        }
    }

    static bool ParseTile(string name, out int c, out int r)
    {
        c = 0; r = 0;
        var p = name.Split('_');
        return p.Length == 3 && p[0] == "Tile" && int.TryParse(p[1], out c) && int.TryParse(p[2], out r);
    }

    Cell AddCell(List<Cell> list, Vector3 topPos, bool isBench, bool valid, int index)
    {
        var c = new Cell
        {
            top = topPos.y,
            pos = new Vector3(topPos.x, topPos.y + 0.02f, topPos.z),
            isBench = isBench,
            valid = valid,
            index = index
        };
        list.Add(c);
        allCells.Add(c);
        return c;
    }

    // ---------------------------------------------------------------- contorns i llums de les zones
    void BuildOverlays()
    {
        ringMesh = HexMesh(hexR * 0.98f, hexR * 0.84f);
        fillMesh = HexMesh(hexR * 0.90f, 0f);
        matValid = UnitShapes.MakeFlat(validColor);
        matInvalid = UnitShapes.MakeFlat(invalidColor);
        matHoverOk = UnitShapes.MakeFlat(hoverOkColor);
        matHoverBad = UnitShapes.MakeFlat(hoverBadColor);

        foreach (var c in allCells)
        {
            c.ring = Overlay("ZoneRing", ringMesh, c.valid ? matValid : matInvalid, c, 0.03f);
            c.fill = Overlay("ZoneFill", fillMesh, matHoverOk, c, 0.015f);
            c.fillRenderer = c.fill.GetComponent<Renderer>();
        }
    }

    GameObject Overlay(string name, Mesh mesh, Material mat, Cell c, float yOffset)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.position = new Vector3(c.pos.x, c.top + yOffset, c.pos.z);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        go.SetActive(false);
        return go;
    }

    // Hexàgon pla (mateixa orientació que les caselles): anell si inner > 0, ple si inner <= 0
    static Mesh HexMesh(float outer, float inner)
    {
        var v = new List<Vector3>();
        var t = new List<int>();
        var o = new Vector3[6];
        var n = new Vector3[6];
        for (int i = 0; i < 6; i++)
        {
            float a = Mathf.Deg2Rad * (60f * i + 30f);
            o[i] = new Vector3(Mathf.Cos(a) * outer, 0f, Mathf.Sin(a) * outer);
            n[i] = new Vector3(Mathf.Cos(a) * inner, 0f, Mathf.Sin(a) * inner);
        }

        if (inner <= 0f)
        {
            v.Add(Vector3.zero);
            v.AddRange(o);
            for (int i = 0; i < 6; i++) { t.Add(0); t.Add(1 + (i + 1) % 6); t.Add(1 + i); }
        }
        else
        {
            v.AddRange(o); // 0..5 exteriors
            v.AddRange(n); // 6..11 interiors
            for (int i = 0; i < 6; i++)
            {
                int j = (i + 1) % 6;
                t.Add(6 + i); t.Add(j); t.Add(i);
                t.Add(6 + i); t.Add(6 + j); t.Add(j);
            }
        }

        var m = new Mesh { name = "HexOverlay" };
        m.SetVertices(v);
        m.SetTriangles(t, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    void ShowZones(bool on)
    {
        foreach (var c in allCells)
        {
            if (c.ring != null) c.ring.SetActive(on);
            if (c.fill != null) c.fill.SetActive(false);
        }
    }

    void SetHover(Cell h)
    {
        if (h == hover) return;
        if (hover != null && hover.fill != null) hover.fill.SetActive(false);
        hover = h;
        if (hover != null && hover.fill != null)
        {
            hover.fillRenderer.sharedMaterial = hover.valid ? matHoverOk : matHoverBad;
            hover.fill.SetActive(true);
        }
    }

    // ---------------------------------------------------------------- arrossegar i deixar
    void Update()
    {
        if (bench == null) return;
        if (Locked) { if (isDragging) CancelDrag(); return; }

        if (isDragging)
        {
            if (dragging == null) { CancelDrag(); return; } // venuda o fusionada mentre s'arrossegava
            DragStep();
        }
        else if (InputUtil.LeftClickDown && !OverUI())
        {
            BeginDrag();
        }
    }

    static bool OverUI()
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }

    void BeginDrag()
    {
        var v = ViewUnderMouse();
        if (v == null) return;
        dragging = v;
        isDragging = true;
        hover = null;
        v.SetDragging(true);
        ShowZones(true);
    }

    void DragStep()
    {
        // Pols de llum: els contorns verds "respiren" una mica
        float k = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 6f);
        UnitShapes.SetFlatColor(matValid, new Color(validColor.r * k, validColor.g * k, validColor.b * k, 1f));

        var ray = cam.ScreenPointToRay(InputUtil.MousePosition);
        var plane = new Plane(Vector3.up, new Vector3(0f, planeY, 0f));
        if (plane.Raycast(ray, out float enter))
        {
            Vector3 hit = ray.GetPoint(enter);
            dragging.transform.position = new Vector3(hit.x, planeY + tileRadius * 0.8f, hit.z);
            SetHover(NearestCell(hit));
        }

        if (!InputUtil.LeftClickHeld) EndDrag();
    }

    Cell NearestCell(Vector3 p)
    {
        Cell best = null;
        float bd = hexR * hexR;
        foreach (var c in allCells)
        {
            float dx = c.pos.x - p.x, dz = c.pos.z - p.z;
            float d = dx * dx + dz * dz;
            if (d <= bd) { bd = d; best = c; }
        }
        return best;
    }

    void EndDrag()
    {
        var view = dragging;
        var target = hover;
        dragging = null;
        isDragging = false;
        if (view != null) view.SetDragging(false);
        SetHover(null);
        ShowZones(false);

        if (view != null && target != null)
        {
            if (!target.valid) OnMessage?.Invoke("Només pots col·locar unitats a la teva meitat");
            else if (target.isBench) bench.MoveToBench(view.Unit, target.index);
            else bench.MoveToBoard(view.Unit, target.index);
        }
        Sync(); // si no s'ha mogut res, la unitat torna al seu lloc
    }

    public void CancelDrag()
    {
        if (dragging != null) dragging.SetDragging(false);
        dragging = null;
        isDragging = false;
        SetHover(null);
        ShowZones(false);
        if (bench != null) Sync();
    }

    // ---------------------------------------------------------------- sincronització
    void OnDestroy()
    {
        if (bench != null) bench.OnChanged -= Sync;
        if (ringMesh != null) Destroy(ringMesh);
        if (fillMesh != null) Destroy(fillMesh);
        if (matValid != null) Destroy(matValid);
        if (matInvalid != null) Destroy(matInvalid);
        if (matHoverOk != null) Destroy(matHoverOk);
        if (matHoverBad != null) Destroy(matHoverBad);
    }

    void Sync()
    {
        var seen = new HashSet<UnitInstance>();

        int nb = Mathf.Min(Bench.Size, benchCells.Count);
        for (int i = 0; i < nb; i++) Place(bench[i], benchCells[i].pos, seen);

        int nd = Mathf.Min(Bench.BoardSize, boardCells.Count);
        for (int i = 0; i < nd; i++) Place(bench.BoardAt(i), boardCells[i].pos, seen);

        var stale = new List<UnitInstance>();
        foreach (var kv in views) if (!seen.Contains(kv.Key)) stale.Add(kv.Key);
        foreach (var u in stale) { Destroy(views[u].gameObject); views.Remove(u); }
    }

    void Place(UnitInstance u, Vector3 pos, HashSet<UnitInstance> seen)
    {
        if (u == null) return;
        seen.Add(u);
        if (!views.TryGetValue(u, out var v))
        {
            v = UnitView.Create(u, tileRadius);
            v.transform.SetParent(transform, false);
            views[u] = v;
        }
        if (!(isDragging && v == dragging)) v.transform.position = pos;
        v.Refresh();
    }

    // ---------------------------------------------------------------- consulta
    UnitView ViewUnderMouse()
    {
        var ray = cam.ScreenPointToRay(InputUtil.MousePosition);
        var hits = Physics.RaycastAll(ray, 500f);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var h in hits)
        {
            var v = h.collider.GetComponentInParent<UnitView>();
            if (v != null) return v;
        }
        return null;
    }

    // Unitat sota el ratolí (o la que s'està arrossegant, perquè es pugui vendre amb E)
    public UnitInstance UnderMouse()
    {
        if (isDragging && dragging != null) return dragging.Unit;
        var v = ViewUnderMouse();
        return v != null ? v.Unit : null;
    }
}