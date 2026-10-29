using System;
using System.Collections.Generic;
using UnityEngine;

public enum CombatOutcome { PlayerWon, EnemyWon, Draw }

public struct CombatResult
{
    public CombatOutcome Outcome;
    public int PlayerSurvivors, EnemySurvivors;
    public float Duration;
}

// Executa UN combat: crea còpies de les unitats del tauler del jugador, posa l'equip enemic
// a l'altra meitat, les fa lluitar i diu qui ha guanyat. No sap res de rondes: el futur
// RoundManager només ha de cridar StartCombat() i escoltar OnCombatEnded / OnCombatCleared.
// També dibuixa (en pantalla) les barres de vida, els números de dany i el tooltip.
[RequireComponent(typeof(ShopManager))]
public class CombatManager : MonoBehaviour
{
    [Header("Combat")]
    public float timeScale = 1f;        // 2 = el doble de ràpid (per provar)
    public float maxDuration = 60f;     // passat aquest temps es decideix pels supervivents
    public float resultDelay = 2f;      // pausa abans de netejar el camp

    [Header("Enemic de prova (si no se'n passa cap)")]
    public int testEnemyCount = 0;      // 0 = tants com unitats tingui el jugador
    public int testEnemyMaxTier = 2;

    [Header("Interfície de combat")]
    public bool showHealthBars = true;  // barres de vida i números de dany
    public bool debugGUI = true;        // botó "Provar combat" + text de resultat

    public bool IsRunning { get; private set; }   // combat en curs o pausa de resultat
    public CombatResult LastResult { get; private set; }

    public event Action<CombatResult> OnCombatEnded;   // quan es decideix el guanyador
    public event Action OnCombatCleared;               // quan el camp ja s'ha netejat

    struct FloatText { public Vector3 world; public string text; public Color color; public float age; }

    static readonly Color playerCol = new Color(0.3f, 1f, 0.4f, 1f);
    static readonly Color enemyCol = new Color(1f, 0.3f, 0.3f, 1f);
    const float FloatLife = 0.9f;

    ShopManager shop;
    HexGrid grid;
    Camera cam;
    float tileRadius;
    Transform root;
    readonly List<CombatUnit> units = new List<CombatUnit>();
    readonly List<FloatText> floats = new List<FloatText>();
    readonly System.Random rng = new System.Random();
    float elapsed, resultTimer;
    bool finished;
    GUIStyle bigStyle, smallStyle, floatStyle;

    void Awake()
    {
        shop = GetComponent<ShopManager>();
        if (GetComponent<RoundManager>() == null) gameObject.AddComponent<RoundManager>(); // rondes i dany de vida
    }

    // ---------------------------------------------------------------- inici
    [ContextMenu("Start Combat")]
    public void StartCombatTest() { StartCombat(null); }

    // enemyTeam = null -> equip aleatori de prova
    public bool StartCombat(IList<UnitInstance> enemyTeam = null)
    {
        if (IsRunning) return false;

        var view = shop.BoardView;
        if (view == null || view.BoardRoot == null)
        {
            Debug.LogError("[CombatManager] No hi ha BenchView/tauler (encara no s'ha fet el Start de ShopManager?).");
            return false;
        }
        grid = HexGrid.FromBoard(view.BoardRoot);
        if (grid == null)
        {
            Debug.LogError("[CombatManager] No s'han trobat les caselles Tile_c_r del tauler.");
            return false;
        }

        var bench = shop.Shop.Bench;
        cam = shop.cam != null ? shop.cam : Camera.main;
        tileRadius = view.TileRadius;

        root = new GameObject("CombatUnits").transform;
        units.Clear();
        floats.Clear();

        // Jugador: una còpia de cada unitat del tauler, a la seva casella
        var playerTiles = new HashSet<Vector2Int>();
        int playerCount = 0;
        for (int i = 0; i < Bench.BoardSize; i++)
        {
            if (!view.TryGetBoardCoord(i, out int c, out int r)) continue;
            var coord = new Vector2Int(c, r);
            playerTiles.Add(coord);
            var u = bench.BoardAt(i);
            if (u == null) continue;
            AddUnit(u, CombatTeam.Player, coord);
            playerCount++;
        }

        // Enemic: caselles de la meitat que no és del jugador
        var enemyTiles = new List<Vector2Int>();
        foreach (var t in grid.Tiles) if (!playerTiles.Contains(t)) enemyTiles.Add(t);
        Shuffle(enemyTiles);

        IList<UnitInstance> enemies = enemyTeam;
        if (enemies == null)
            enemies = BuildTestEnemies(testEnemyCount > 0 ? testEnemyCount : Mathf.Max(1, playerCount), enemyTiles.Count);
        for (int i = 0; i < enemies.Count && i < enemyTiles.Count; i++)
            AddUnit(enemies[i], CombatTeam.Enemy, enemyTiles[i]);

        Shuffle(units); // ordre d'actuació aleatori: cap bàndol juga sempre primer

        view.SetBoardUnitsVisible(false); // s'amaguen les unitats "reals" mentre lluiten les còpies
        shop.LockInput = true;

        elapsed = 0f;
        finished = false;
        IsRunning = true;
        return true;
    }

    // La unitat de combat que té aquesta vista (per al panell d'informació); null si no n'hi ha
    public CombatUnit FindCombatUnit(UnitView v)
    {
        if (v == null || !IsRunning) return null;
        foreach (var u in units) if (u.View == v) return u;
        return null;
    }

    void AddUnit(UnitInstance src, CombatTeam team, Vector2Int coord)
    {
        var copy = new UnitInstance(src.Def);
        copy.Star = src.Star;
        var v = UnitView.Create(copy, tileRadius);
        v.transform.SetParent(root, false);
        v.Refresh();
        var cu = new CombatUnit(copy, team, v, grid, units, coord, tileRadius);
        cu.Damaged += OnUnitDamaged;
        units.Add(cu);
    }

    List<UnitInstance> BuildTestEnemies(int count, int max)
    {
        var pool = new List<UnitDefinition>();
        foreach (var d in UnitDatabase.All) if (d.Tier <= Mathf.Max(1, testEnemyMaxTier)) pool.Add(d);
        var list = new List<UnitInstance>();
        count = Mathf.Min(count, max);
        for (int i = 0; i < count; i++) list.Add(new UnitInstance(pool[rng.Next(pool.Count)]));
        return list;
    }

    void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            T tmp = list[i]; list[i] = list[j]; list[j] = tmp;
        }
    }

    // ---------------------------------------------------------------- bucle
    void Update()
    {
        if (!IsRunning) return;

        // números de dany flotants (temps real, no depèn del timeScale)
        for (int i = floats.Count - 1; i >= 0; i--)
        {
            var f = floats[i];
            f.age += Time.deltaTime;
            if (f.age >= FloatLife) floats.RemoveAt(i); else floats[i] = f;
        }

        if (finished)
        {
            resultTimer -= Time.deltaTime;
            if (resultTimer <= 0f) Cleanup();
            return;
        }

        float dt = Mathf.Min(Time.deltaTime, 0.05f) * timeScale;
        elapsed += dt;
        for (int i = 0; i < units.Count; i++) units[i].Tick(dt);

        int pa = 0, ea = 0;
        foreach (var u in units)
        {
            if (!u.Alive) continue;
            if (u.Team == CombatTeam.Player) pa++; else ea++;
        }

        bool timeUp = elapsed >= maxDuration;
        if (pa > 0 && ea > 0 && !timeUp) return;

        CombatOutcome outcome;
        if (pa > ea) outcome = CombatOutcome.PlayerWon;
        else if (ea > pa) outcome = CombatOutcome.EnemyWon;
        else outcome = CombatOutcome.Draw;

        LastResult = new CombatResult { Outcome = outcome, PlayerSurvivors = pa, EnemySurvivors = ea, Duration = elapsed };
        finished = true;
        resultTimer = resultDelay;
        OnCombatEnded?.Invoke(LastResult);
    }

    void Cleanup()
    {
        foreach (var u in units) u.Dispose();
        units.Clear();
        floats.Clear();
        if (root != null) Destroy(root.gameObject);
        root = null;
        grid = null;

        if (shop != null)
        {
            shop.LockInput = false;
            if (shop.BoardView != null) shop.BoardView.SetBoardUnitsVisible(true);
        }
        IsRunning = false;
        finished = false;
        OnCombatCleared?.Invoke();
    }

    void OnUnitDamaged(CombatUnit u, float amount)
    {
        if (cam == null) return;
        if (floats.Count > 80) floats.RemoveAt(0);
        Vector3 jitter = cam.transform.right * (((float)rng.NextDouble() - 0.5f) * tileRadius);
        floats.Add(new FloatText
        {
            world = u.BarAnchor + jitter,
            text = Mathf.RoundToInt(amount).ToString(),
            color = u.Team == CombatTeam.Enemy ? new Color(1f, 0.95f, 0.4f) : new Color(1f, 0.45f, 0.45f),
            age = 0f
        });
    }

    // ---------------------------------------------------------------- interfície (IMGUI)
    void OnGUI()
    {
        if (debugGUI) DrawDebug();

        if (!IsRunning || !showHealthBars || cam == null) return;
        if (Event.current.type != EventType.Repaint) return;

        EnsureStyles();
        DrawUnitsUI();
        DrawFloatingTexts();
    }

    void DrawDebug()
    {
        if (!IsRunning)
        {
            if (GUI.Button(new Rect(Screen.width / 2f - 90f, 10f, 180f, 36f), "Provar combat")) StartCombat(null);
            return;
        }

        if (finished)
        {
            if (bigStyle == null)
                bigStyle = new GUIStyle(GUI.skin.label) { fontSize = 36, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            string msg = LastResult.Outcome == CombatOutcome.PlayerWon ? "Has guanyat!"
                       : LastResult.Outcome == CombatOutcome.EnemyWon ? "Has perdut" : "Empat";
            GUI.Label(new Rect(0f, 60f, Screen.width, 60f), msg, bigStyle);
        }
    }

    void EnsureStyles()
    {
        if (smallStyle != null) return;
        smallStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        smallStyle.normal.textColor = Color.white;
        floatStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
    }

    bool ToGui(Vector3 world, out Vector2 p)
    {
        Vector3 s = cam.WorldToScreenPoint(world);
        p = new Vector2(s.x, Screen.height - s.y);
        return s.z > 0f;
    }

    static void Fill(Rect r, Color c)
    {
        var old = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = old;
    }

    // Amplada de la barra en píxels: segueix el zoom de la càmera
    float BarWidth(Vector3 anchor)
    {
        Vector3 r = cam.transform.right * (tileRadius * 1.1f);
        float a = cam.WorldToScreenPoint(anchor - r).x;
        float b = cam.WorldToScreenPoint(anchor + r).x;
        return Mathf.Clamp(Mathf.Abs(b - a), 34f, 120f);
    }

    void DrawUnitsUI()
    {
        foreach (var u in units)
        {
            if (!u.Alive || !ToGui(u.BarAnchor, out var p)) continue;
            DrawBar(u, p, BarWidth(u.BarAnchor), false);
        }
    }

    void DrawBar(CombatUnit u, Vector2 p, float w, bool hover)
    {
        float h = hover ? 14f : 7f;
        var r = new Rect(p.x - w * 0.5f, p.y - h * 0.5f, w, h);
        float max = u.Stats.MaxHp;
        Color team = u.Team == CombatTeam.Player ? playerCol : enemyCol;

        Fill(new Rect(r.x - 1f, r.y - 1f, r.width + 2f, r.height + 2f), new Color(0f, 0f, 0f, 0.9f)); // vora
        Fill(r, new Color(0.15f, 0.15f, 0.15f, 0.95f));                                                 // fons
        Fill(new Rect(r.x, r.y, r.width * Mathf.Clamp01(u.GhostHp / max), r.height), new Color(1f, 1f, 1f, 0.85f)); // estela de dany
        Fill(new Rect(r.x, r.y, r.width * Mathf.Clamp01(u.DisplayHp / max), r.height), Color.Lerp(team, Color.white, u.HitFlash)); // vida

        // marques cada 100 de vida (com als MOBA): es veu quanta vida té
        float step = 100f;
        while (max / step > 14f) step *= 2f;
        for (float x = step; x < max; x += step)
            Fill(new Rect(r.x + r.width * (x / max), r.y, 1f, r.height), new Color(0f, 0f, 0f, 0.55f));

        // estrelles (quadradets daurats) damunt la barra
        for (int i = 0; i < u.Source.Star; i++)
            Fill(new Rect(r.x + i * 8f, r.y - 8f, 6f, 6f), new Color(1f, 0.82f, 0.25f, 1f));

        if (hover)
            GUI.Label(r, Mathf.CeilToInt(u.Hp) + " / " + Mathf.RoundToInt(max), smallStyle);
    }

    void DrawFloatingTexts()
    {
        foreach (var f in floats)
        {
            if (!ToGui(f.world, out var p)) continue;
            float t = f.age / FloatLife;
            float alpha = 1f - t * t;
            var rect = new Rect(p.x - 40f, p.y - 12f - 45f * t, 80f, 24f);

            floatStyle.normal.textColor = new Color(0f, 0f, 0f, alpha);
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), f.text, floatStyle);
            floatStyle.normal.textColor = new Color(f.color.r, f.color.g, f.color.b, alpha);
            GUI.Label(rect, f.text, floatStyle);
        }
    }
}
