using UnityEngine;
using UnityEngine.EventSystems;

// Inspecció de personatges (al banc, al tauler i durant el combat):
//  - Ratolí per sobre d'un personatge  -> aura (anells al terra + guspires + llum) al seu voltant.
//  - Clic dret sobre un personatge     -> pestanya a la dreta amb tota la seva informació.
//  - Clic dret al buit                 -> mou el personatge principal (el panell es queda obert).
//  - X / Esc                           -> es tanca la pestanya.
// Es crea sol des de ShopManager: no cal posar res a l'editor.
[RequireComponent(typeof(ShopManager))]
public class UnitInspector : MonoBehaviour
{
    // BenchView i ShopManager el consulten per no agafar/vendre unitats a través del panell
    public static bool PointerOverPanel { get; private set; }

    static UnitInspector instance;

    // El clic dret actual NO ha de moure el personatge principal si cau sobre
    // un aliat/enemic, sobre el panell o sobre la UI. Es calcula al moment, així
    // no depèn de l'ordre d'execució dels scripts.
    public static bool BlocksWorldClick
    {
        get { return instance != null && instance.ComputeBlocksWorldClick(); }
    }

    [Header("Panell")]
    public float panelWidth = 310f;
    public float panelTop = 70f;

    [Header("Aura")]
    public bool showAura = true;
    public bool auraLight = true;

    static readonly Color hoverColor = new Color(0.45f, 0.9f, 1f, 1f);
    static readonly Color enemyHoverColor = new Color(1f, 0.5f, 0.35f, 1f);
    static readonly Color selectColor = new Color(1f, 0.85f, 0.3f, 1f);
    static readonly Color playerCol = new Color(0.3f, 1f, 0.4f, 1f);
    static readonly Color enemyCol = new Color(1f, 0.3f, 0.3f, 1f);
    static readonly Color starCol = new Color(1f, 0.82f, 0.25f, 1f);

    // alçades del panell (la fórmula de l'alçada total i el dibuix les comparteixen)
    const float HeaderH = 52f, IdentH = 74f, TitleH = 24f, RowH = 22f, BarH = 18f, SellH = 54f;

    ShopManager shop;
    CombatManager combat;
    Camera cam;
    UnitView hovered, selected;
    CombatUnit selCombat;
    bool panelOpen;
    float panelH;
    UnitAura hoverAura, selectAura;
    GUIStyle titleStyle, labelStyle, valueStyle, sectionStyle, buttonStyle;

    void Awake() { shop = GetComponent<ShopManager>(); instance = this; }

    // ---------------------------------------------------------------- lògica
    void Update()
    {
        if (shop == null) return;
        var bv = shop.BoardView;
        if (cam == null) cam = shop.cam != null ? shop.cam : Camera.main;
        if (combat == null) combat = GetComponent<CombatManager>();
        if (cam == null || bv == null) return;

        // la unitat seleccionada pot haver desaparegut (venuda, combat acabat, amagada...)
        if (panelOpen)
        {
            if (selected == null) Close();
            else
            {
                selCombat = combat != null ? combat.FindCombatUnit(selected) : null;
                bool gone = !selected.gameObject.activeInHierarchy && (selCombat == null || selCombat.Alive);
                if (gone) Close();
            }
        }

        Vector2 mouse = InputUtil.MousePosition;
        PointerOverPanel = panelOpen && PanelRect().Contains(new Vector2(mouse.x, Screen.height - mouse.y));
        bool overUI = PointerOverPanel || (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject());

        hovered = (overUI || bv.IsDragging) ? null : UnitUnderMouse(mouse);

        if (InputUtil.RightClickDown && !overUI && !bv.IsDragging)
        {
            // Clic dret sobre un personatge: obre el panell, el canvia si és un altre
            // personatge, o el tanca si és el mateix que ja està seleccionat.
            // Clic dret al buit = moure el personatge, el panell es queda obert.
            if (hovered != null)
            {
                if (panelOpen && hovered == selected) Close();
                else Select(hovered);
            }
        }

        UpdateAuras(bv);
    }

    void UpdateAuras(BenchView bv)
    {
        if (!showAura)
        {
            if (hoverAura != null) hoverAura.Set(null, hoverColor);
            if (selectAura != null) selectAura.Set(null, selectColor);
            if (hoverAura != null) hoverAura.Tick(0f);
            if (selectAura != null) selectAura.Tick(0f);
            return;
        }
        if (hoverAura == null) hoverAura = new UnitAura("HoverAura", auraLight);
        if (selectAura == null) selectAura = new UnitAura("SelectedAura", auraLight);

        float t = Time.unscaledTime;
        float maxR = bv.TileRadius * 1.85f;

        UnitView hv = hovered != selected || !panelOpen ? hovered : null;
        Color hc = hoverColor;
        if (hv != null && combat != null)
        {
            var cu = combat.FindCombatUnit(hv);
            if (cu != null && cu.Team == CombatTeam.Enemy) hc = enemyHoverColor;
        }
        hoverAura.MaxRadius = maxR;
        hoverAura.Set(hv, hc);
        hoverAura.Tick(t);

        selectAura.MaxRadius = maxR;
        selectAura.Set(panelOpen ? selected : null, selectColor);
        selectAura.Tick(t);
    }

    UnitView UnitUnderMouse(Vector2 mouse)
    {
        var ray = cam.ScreenPointToRay(mouse);
        var hits = Physics.RaycastAll(ray, 500f);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var h in hits)
        {
            var v = h.collider.GetComponentInParent<UnitView>();
            if (v != null) return v;
        }
        return null;
    }

    bool ComputeBlocksWorldClick()
    {
        Vector2 mouse = InputUtil.MousePosition;

        if (panelOpen && PanelRect().Contains(new Vector2(mouse.x, Screen.height - mouse.y)))
            return true;

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return true;

        if (cam == null) cam = (shop != null && shop.cam != null) ? shop.cam : Camera.main;
        if (cam == null) return false;

        return UnitUnderMouse(mouse) != null;
    }

    void Select(UnitView v)
    {
        selected = v;
        panelOpen = true;
        selCombat = combat != null ? combat.FindCombatUnit(v) : null;
    }

    void Close()
    {
        panelOpen = false;
        selected = null;
        selCombat = null;
        PointerOverPanel = false;
    }

    Rect PanelRect() { return new Rect(Screen.width - panelWidth - 12f, panelTop, panelWidth, panelH); }

    void OnDestroy()
    {
        if (instance == this) instance = null;
        if (hoverAura != null) hoverAura.Destroy();
        if (selectAura != null) selectAura.Destroy();
        PointerOverPanel = false;
    }

    // ---------------------------------------------------------------- panell (IMGUI)
    void OnGUI()
    {
        if (!panelOpen || selected == null) return;
        GUI.depth = -10; // per sobre de la resta d'IMGUI
        EnsureStyles();

        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
        {
            Close();
            Event.current.Use();
            return;
        }
        DrawPanel();
    }

    void EnsureStyles()
    {
        if (titleStyle != null) return;
        titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
        titleStyle.normal.textColor = Color.white;
        labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 13 };
        labelStyle.normal.textColor = new Color(0.7f, 0.74f, 0.8f);
        valueStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };
        valueStyle.normal.textColor = Color.white;
        sectionStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold };
        sectionStyle.normal.textColor = starCol;
        buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 14, fontStyle = FontStyle.Bold };
    }

    static void Fill(Rect r, Color c)
    {
        var old = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = old;
    }

    static string ShapeName(UnitShape s)
    {
        switch (s)
        {
            case UnitShape.Triangle: return "Triangle";
            case UnitShape.Square: return "Quadrat";
            case UnitShape.Diamond: return "Rombe";
            case UnitShape.Pentagon: return "Pentàgon";
            case UnitShape.Hexagon: return "Hexàgon";
            case UnitShape.Octagon: return "Octàgon";
            case UnitShape.Circle: return "Cercle";
            case UnitShape.Star: return "Estrella";
            default: return "Creu";
        }
    }

    void DrawPanel()
    {
        var unit = selected.Unit;
        var def = unit.Def;
        var cu = selCombat;
        bool inCombat = cu != null;
        var st = inCombat ? cu.Stats : CombatStats.For(def, unit.Star);
        bool sellable = !(combat != null && combat.IsRunning) && shop.Shop.Bench.Contains(unit);
        bool hasNext = unit.Star < 3;

        float h = HeaderH + IdentH
                + TitleH + 6 * RowH + (inCombat ? BarH : 0f)
                + (hasNext ? TitleH + 2 * RowH + 8f : 0f)
                + (inCombat ? TitleH + 3 * RowH + 6f : 0f)
                + (sellable ? SellH : 0f)
                + 12f;
        panelH = h;

        var r = PanelRect();
        Color accent = !inCombat ? selectColor : (cu.Team == CombatTeam.Player ? playerCol : enemyCol);
        Fill(r, new Color(0.07f, 0.08f, 0.11f, 0.95f));
        Fill(new Rect(r.x, r.y, r.width, 3f), accent);
        Fill(new Rect(r.x, r.y, 2f, r.height), new Color(accent.r, accent.g, accent.b, 0.5f));

        float x = r.x + 16f, w = r.width - 30f, y = r.y;

        // capçalera: nom, estrelles i botó de tancar
        GUI.Label(new Rect(x, y + 8f, w - 30f, 26f), def.Name, titleStyle);
        if (GUI.Button(new Rect(r.xMax - 38f, r.y + 10f, 24f, 24f), "X", buttonStyle)) { Close(); return; }
        for (int i = 0; i < unit.Star; i++) Fill(new Rect(x + i * 16f, y + 36f, 12f, 12f), starCol);
        y = r.y + HeaderH;

        // identitat
        Fill(new Rect(x, y + 4f, 52f, 52f), new Color(0f, 0f, 0f, 0.6f));
        Fill(new Rect(x + 3f, y + 7f, 46f, 46f), new Color(def.R, def.G, def.B, 1f));
        GUI.Label(new Rect(x + 64f, y + 2f, w - 64f, 20f), "Tier " + def.Tier + "  -  " + ShapeName(def.Shape), labelStyle);
        GUI.Label(new Rect(x + 64f, y + 20f, w - 64f, 20f), "Cost: " + def.Cost + " or", labelStyle);
        GUI.Label(new Rect(x + 64f, y + 38f, w - 64f, 20f), "Venda: " + unit.SellValue + " or", labelStyle);
        y += IdentH;

        // estadístiques
        Section(x, ref y, w, "ESTADÍSTIQUES");
        string hp = inCombat ? Mathf.CeilToInt(cu.Hp) + " / " + Mathf.RoundToInt(st.MaxHp) : Mathf.RoundToInt(st.MaxHp).ToString();
        Row(x, ref y, w, "Vida", hp);
        if (inCombat)
        {
            var bar = new Rect(x, y + 3f, w, 10f);
            float max = st.MaxHp;
            Fill(new Rect(bar.x - 1f, bar.y - 1f, bar.width + 2f, bar.height + 2f), Color.black);
            Fill(bar, new Color(0.15f, 0.15f, 0.15f, 1f));
            Fill(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(cu.GhostHp / max), bar.height), new Color(1f, 1f, 1f, 0.85f));
            Fill(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(cu.DisplayHp / max), bar.height), accent);
            y += BarH;
        }
        Row(x, ref y, w, "Atac", Mathf.RoundToInt(st.Attack).ToString());
        Row(x, ref y, w, "Velocitat d'atac", st.AttackSpeed.ToString("0.0") + " /s");
        Row(x, ref y, w, "Dany per segon", (st.Attack * st.AttackSpeed).ToString("0"));
        Row(x, ref y, w, "Abast", st.Range == 1 ? "1 (cos a cos)" : st.Range + " caselles");
        Row(x, ref y, w, "Velocitat de moviment", st.MoveSpeed.ToString("0.0") + " caselles/s");

        // següent nivell d'estrelles
        if (hasNext)
        {
            var nx = CombatStats.For(def, unit.Star + 1);
            Section(x, ref y, w, "AL SEGÜENT NIVELL (" + (unit.Star + 1) + " estrelles)");
            Row(x, ref y, w, "Vida", Mathf.RoundToInt(st.MaxHp) + "  >  " + Mathf.RoundToInt(nx.MaxHp));
            Row(x, ref y, w, "Atac", Mathf.RoundToInt(st.Attack) + "  >  " + Mathf.RoundToInt(nx.Attack));
            y += 8f;
        }

        // estat dins del combat
        if (inCombat)
        {
            Section(x, ref y, w, "COMBAT");
            Row(x, ref y, w, "Equip", cu.Team == CombatTeam.Player ? "Jugador" : "Enemic");
            Row(x, ref y, w, "Estat", cu.Alive ? "Lluitant" : "Eliminada");
            string tgt = (cu.Target != null && cu.Target.Alive) ? cu.Target.Source.Def.Name : "-";
            Row(x, ref y, w, "Objectiu", tgt);
            y += 6f;
        }

        // vendre (només fora de combat i si és al banc/tauler del jugador)
        if (sellable)
        {
            if (GUI.Button(new Rect(x, y + 8f, w, 38f), "Vendre  (+" + unit.SellValue + " or)", buttonStyle))
            {
                shop.Sell(unit);
                Close();
            }
        }
    }

    void Section(float x, ref float y, float w, string title)
    {
        GUI.Label(new Rect(x, y + 2f, w, 20f), title, sectionStyle);
        Fill(new Rect(x, y + 21f, w, 1f), new Color(1f, 1f, 1f, 0.15f));
        y += TitleH;
    }

    void Row(float x, ref float y, float w, string label, string value)
    {
        GUI.Label(new Rect(x, y, w * 0.5f, RowH), label, labelStyle);
        GUI.Label(new Rect(x + w * 0.4f, y, w * 0.6f, RowH), value, valueStyle);
        y += RowH;
    }
}

// Aura al voltant d'un personatge: dos anells plans al terra (un pulsa), guspires que
// pugen en espiral i una llum puntual. Tot amb el material pla de UnitShapes.
sealed class UnitAura
{
    const int Sparks = 6;
    static Mesh ringMesh;

    public float MaxRadius = 1f;   // límit perquè no se surti de la casella

    readonly GameObject root;
    readonly Transform ringOuter, ringInner;
    readonly Transform[] sparks = new Transform[Sparks];
    readonly Material matRing, matSpark;
    readonly Light glow;
    UnitView target;
    Transform bodyT;
    Color color = Color.clear;
    float radius = 0.5f, height = 1f;

    public UnitAura(string name, bool withLight)
    {
        if (ringMesh == null) ringMesh = BuildRing(1f, 0.82f, 48);
        root = new GameObject(name);
        matRing = UnitShapes.MakeFlat(Color.white);
        matSpark = UnitShapes.MakeFlat(Color.white);
        ringOuter = MakeRing("Outer");
        ringInner = MakeRing("Inner");
        for (int i = 0; i < Sparks; i++) sparks[i] = MakeSpark();

        if (withLight)
        {
            var lg = new GameObject("Glow");
            lg.transform.SetParent(root.transform, false);
            lg.transform.localPosition = Vector3.up * 0.8f;
            glow = lg.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.range = 3.5f;
            glow.shadows = LightShadows.None;
            glow.intensity = 1.5f;
        }
        root.SetActive(false);
    }

    Transform MakeRing(string n)
    {
        var go = new GameObject(n);
        go.transform.SetParent(root.transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = ringMesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = matRing;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        return go.transform;
    }

    Transform MakeSpark()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "Spark";
        var col = go.GetComponent<Collider>();
        if (col != null) Object.Destroy(col);
        go.transform.SetParent(root.transform, false);
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = matSpark;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        return go.transform;
    }

    // Anell pla visible per les dues cares (no depenem de com talli el shader)
    static Mesh BuildRing(float outer, float inner, int seg)
    {
        var v = new Vector3[seg * 2];
        for (int i = 0; i < seg; i++)
        {
            float a = i * Mathf.PI * 2f / seg;
            float c = Mathf.Cos(a), s = Mathf.Sin(a);
            v[i] = new Vector3(c * outer, 0f, s * outer);
            v[seg + i] = new Vector3(c * inner, 0f, s * inner);
        }
        var t = new int[seg * 12];
        int k = 0;
        for (int i = 0; i < seg; i++)
        {
            int j = (i + 1) % seg;
            t[k++] = i; t[k++] = seg + i; t[k++] = j;
            t[k++] = j; t[k++] = seg + i; t[k++] = seg + j;
            t[k++] = i; t[k++] = j; t[k++] = seg + i;
            t[k++] = j; t[k++] = seg + j; t[k++] = seg + i;
        }
        var m = new Mesh { name = "AuraRing" };
        m.vertices = v;
        m.triangles = t;
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    public void Set(UnitView v, Color c)
    {
        if (v != target)
        {
            target = v;
            bodyT = v != null ? v.transform.Find("Body") : null;
        }
        if (c != color)
        {
            color = c;
            UnitShapes.SetFlatColor(matRing, c);
            UnitShapes.SetFlatColor(matSpark, Color.Lerp(c, Color.white, 0.55f));
            if (glow != null) glow.color = c;
        }
    }

    public void Tick(float t)
    {
        if (target == null || !target.gameObject.activeInHierarchy)
        {
            if (root.activeSelf) root.SetActive(false);
            return;
        }
        if (!root.activeSelf) root.SetActive(true);

        if (bodyT != null) { radius = bodyT.localScale.x; height = bodyT.localScale.y; }
        root.transform.position = target.transform.position + Vector3.up * 0.04f;

        float pulse = 0.5f + 0.5f * Mathf.Sin(t * 4f);
        float inner = Mathf.Min(radius * 1.2f, MaxRadius * 0.8f);
        float outer = Mathf.Min(radius * (1.45f + 0.35f * pulse), MaxRadius);
        ringInner.localScale = Vector3.one * inner;
        ringOuter.localScale = Vector3.one * Mathf.Max(outer, inner * 1.25f);

        for (int i = 0; i < Sparks; i++)
        {
            float ph = (t * 0.5f + i / (float)Sparks) % 1f;
            float ang = t * 1.8f + i * Mathf.PI * 2f / Sparks;
            float rr = inner * 0.95f;
            sparks[i].localPosition = new Vector3(Mathf.Cos(ang) * rr, 0.05f + ph * height * 1.1f, Mathf.Sin(ang) * rr);
            sparks[i].localScale = Vector3.one * (radius * 0.16f * (1f - ph));
        }
        if (glow != null) glow.intensity = 1.4f + 0.8f * pulse;
    }

    public void Destroy()
    {
        if (root != null) Object.Destroy(root);
        if (matRing != null) Object.Destroy(matRing);
        if (matSpark != null) Object.Destroy(matSpark);
    }
}
