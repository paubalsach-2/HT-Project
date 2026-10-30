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

// Dany fet per una unitat en un combat (el llegeix PlayerHUD a la pestanya Estadístiques)
public struct UnitDamageStat
{
    public string Name;
    public int Star;
    public CombatTeam Team;
    public float Damage;
    public bool Alive;
}

// VISTA del combat. La lògica és a CombatSim (Core, determinista); aquí només:
//  - construeix l'entrada (unitats del tauler del jugador + equip rival de EnemyTeamGenerator),
//  - fa avançar la simulació a 20 ticks/s i reprodueix els esdeveniments com a animació,
//  - dibuixa barres de vida i números de dany.
// No decideix quan comença ni quan acaba: ho fa RoundManager (StartCombat / ClearField).
[RequireComponent(typeof(ShopManager))]
public class CombatManager : MonoBehaviour
{
    [Header("Combat")]
    public float timeScale = 1f;        // 2 = el doble de ràpid (per provar)
    public int seed = -1;               // >= 0 per a equips rivals repetibles

    [Header("Interfície de combat")]
    public bool showHealthBars = true;  // barres de vida i números de dany
    public bool showResultText = true;  // "Has guanyat!" / "Has perdut" / "Empat"

    public bool IsRunning { get; private set; }   // combat en curs o pausa de resultat
    public CombatResult LastResult { get; private set; }
    public int StatsRound { get; private set; }   // ronda a la qual corresponen les estadístiques de dany

    public event Action<CombatResult> OnCombatEnded;   // quan es decideix el guanyador
    public event Action OnCombatCleared;               // quan el camp ja s'ha netejat

    struct FloatText { public Vector3 world; public string text; public Color color; public float age; }

    static readonly Color playerCol = new Color(0.3f, 1f, 0.4f, 1f);
    static readonly Color enemyCol = new Color(1f, 0.3f, 0.3f, 1f);
    const float FloatLife = 0.9f;
    const float TickSeconds = 1f / SimConfig.TicksPerSecond;

    ShopManager shop;
    BoardWorld world;
    CombatSim sim;
    Camera cam;
    float tileRadius;
    Transform root;
    readonly List<CombatUnit> units = new List<CombatUnit>();
    readonly Dictionary<int, CombatUnit> byUid = new Dictionary<int, CombatUnit>();
    readonly List<FloatText> floats = new List<FloatText>();
    readonly List<UnitDamageStat> lastStats = new List<UnitDamageStat>(); // dany de l'últim combat (es conserva en acabar)
    readonly System.Random jitter = new System.Random(); // només per a la posició visual dels números de dany
    float acc;
    bool finished;
    GUIStyle bigStyle, smallStyle, floatStyle;

    void Awake()
    {
        shop = GetComponent<ShopManager>();
        if (GetComponent<RoundManager>() == null) gameObject.AddComponent<RoundManager>(); // fases, rondes i dany de vida
    }

    // ---------------------------------------------------------------- inici
    // Crida'l RoundManager en entrar a la fase de combat
    public bool StartCombat(int round)
    {
        if (IsRunning) return false;

        var view = shop.BoardView;
        if (view == null || view.BoardRoot == null)
        {
            Debug.LogError("[CombatManager] No hi ha BenchView/tauler (encara no s'ha fet el Start de ShopManager?).");
            return false;
        }
        world = BoardWorld.Build(view.BoardRoot);
        if (world == null)
        {
            Debug.LogError("[CombatManager] No s'han trobat les caselles Tile_c_r del tauler.");
            return false;
        }

        var bench = shop.Shop.Bench;
        cam = shop.cam != null ? shop.cam : Camera.main;
        tileRadius = view.TileRadius;

        root = new GameObject("CombatUnits").transform;
        units.Clear();
        byUid.Clear();
        floats.Clear();
        lastStats.Clear();
        StatsRound = round;

        // Equip A (jugador): una còpia de cada unitat del tauler, a la seva casella
        var setup = new List<SimUnitSetup>();
        var playerTiles = new HashSet<HexCoord>();
        int playerCount = 0;
        for (int i = 0; i < Bench.BoardSize; i++)
        {
            if (!view.TryGetBoardCoord(i, out int c, out int r)) continue;
            playerTiles.Add(new HexCoord(c, r));
            var u = bench.BoardAt(i);
            if (u == null) continue;
            setup.Add(new SimUnitSetup { Uid = u.Uid, DefId = u.Def.Id, Star = u.Star, Team = SimTeam.A, Col = c, Row = r });
            playerCount++;
        }

        // Equip B (rival): caselles de la meitat que no és del jugador (en ordre fix)
        var enemyTiles = new List<HexCoord>();
        foreach (var t in world.Tiles) if (!playerTiles.Contains(t)) enemyTiles.Add(t);
        enemyTiles.Sort((a, b) => a.R != b.R ? a.R.CompareTo(b.R) : a.C.CompareTo(b.C));
        int s = seed >= 0 ? seed : Environment.TickCount;
        setup.AddRange(EnemyTeamGenerator.Generate(round, playerCount, s, enemyTiles));

        sim = new CombatSim(setup);

        // Una vista per cada unitat de la simulació
        foreach (var su in sim.Units)
        {
            var def = UnitLookup.ById(su.DefId);
            var copy = new UnitInstance(def, su.Uid) { Star = su.Star };
            var v = UnitView.Create(copy, tileRadius);
            v.transform.SetParent(root, false);
            v.Refresh();
            var cu = new CombatUnit(su, copy, su.Team == SimTeam.A ? CombatTeam.Player : CombatTeam.Enemy, v, world, tileRadius);
            cu.Damaged += OnUnitDamaged;
            units.Add(cu);
            byUid[su.Uid] = cu;
        }

        view.SetBoardUnitsVisible(false); // s'amaguen les unitats "reals" mentre lluiten les còpies
        shop.LockInput = true;

        acc = 0f;
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

        float dt = Mathf.Min(Time.deltaTime, 0.05f) * timeScale;

        if (!finished)
        {
            acc += dt;
            while (acc >= TickSeconds && !sim.Finished)
            {
                acc -= TickSeconds;
                sim.Tick();
                ProcessEvents();
            }
            foreach (var u in units)
            {
                u.Target = byUid.TryGetValue(u.Sim.TargetUid, out var tg) ? tg : null;
            }
            if (sim.Finished) Finish();
        }

        for (int i = 0; i < units.Count; i++) units[i].Advance(dt);
    }

    void ProcessEvents()
    {
        var ev = sim.LastEvents;
        for (int i = 0; i < ev.Count; i++)
        {
            var e = ev[i];
            if (!byUid.TryGetValue(e.Uid, out var u)) continue;
            switch (e.Type)
            {
                case SimEventType.Move:
                    u.PlayMove(e.To, e.Amount * TickSeconds);
                    break;
                case SimEventType.Attack:
                    if (byUid.TryGetValue(e.TargetUid, out var tgt)) u.PlayAttack(tgt.Coord);
                    break;
                case SimEventType.Hit:
                    if (byUid.TryGetValue(e.TargetUid, out var victim)) victim.Hit(e.Amount);
                    break;
                case SimEventType.Death:
                    u.Die();
                    break;
            }
        }
    }

    void Finish()
    {
        var r = sim.Result;
        CombatOutcome outcome = r.Outcome == SimOutcome.TeamAWon ? CombatOutcome.PlayerWon
                              : r.Outcome == SimOutcome.TeamBWon ? CombatOutcome.EnemyWon
                              : CombatOutcome.Draw;
        LastResult = new CombatResult
        {
            Outcome = outcome,
            PlayerSurvivors = r.SurvivorsA,
            EnemySurvivors = r.SurvivorsB,
            Duration = r.Ticks * TickSeconds
        };
        finished = true;
        OnCombatEnded?.Invoke(LastResult);
    }

    // Omple 'into' amb el dany de cada unitat: en directe durant el combat i, un cop acabat, l'últim combat.
    public void GetDamageStats(List<UnitDamageStat> into)
    {
        into.Clear();
        if (IsRunning)
        {
            foreach (var u in units) into.Add(MakeStat(u));
            return;
        }
        into.AddRange(lastStats);
    }

    static UnitDamageStat MakeStat(CombatUnit u)
    {
        return new UnitDamageStat
        {
            Name = u.Source.Def.Name,
            Star = u.Source.Star,
            Team = u.Team,
            Damage = u.DamageDealt,
            Alive = u.Alive
        };
    }

    // Neteja el camp i torna les unitats reals. El crida RoundManager en començar la preparació.
    public void ClearField()
    {
        if (!IsRunning) return;

        // guardem les estadístiques abans de destruir les unitats
        lastStats.Clear();
        foreach (var u in units) lastStats.Add(MakeStat(u));

        foreach (var u in units) u.Dispose();
        units.Clear();
        byUid.Clear();
        floats.Clear();
        if (root != null) Destroy(root.gameObject);
        root = null;
        sim = null;
        world = null;

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
        Vector3 offset = cam.transform.right * (((float)jitter.NextDouble() - 0.5f) * tileRadius);
        floats.Add(new FloatText
        {
            world = u.BarAnchor + offset,
            text = Mathf.RoundToInt(amount).ToString(),
            color = u.Team == CombatTeam.Enemy ? new Color(1f, 0.95f, 0.4f) : new Color(1f, 0.45f, 0.45f),
            age = 0f
        });
    }

    // ---------------------------------------------------------------- interfície (IMGUI)
    void OnGUI()
    {
        if (IsRunning && finished && showResultText) DrawResult();

        if (!IsRunning || !showHealthBars || cam == null) return;
        if (Event.current.type != EventType.Repaint) return;

        EnsureStyles();
        DrawUnitsUI();
        DrawFloatingTexts();
    }

    void DrawResult()
    {
        if (bigStyle == null)
            bigStyle = new GUIStyle(GUI.skin.label) { fontSize = 36, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        string msg = LastResult.Outcome == CombatOutcome.PlayerWon ? "Has guanyat!"
                   : LastResult.Outcome == CombatOutcome.EnemyWon ? "Has perdut" : "Empat";
        GUI.Label(new Rect(0f, 60f, Screen.width, 60f), msg, bigStyle);
    }

    void EnsureStyles()
    {
        if (smallStyle != null) return;
        smallStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        smallStyle.normal.textColor = Color.white;
        floatStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
    }

    bool ToGui(Vector3 worldPos, out Vector2 p)
    {
        Vector3 s = cam.WorldToScreenPoint(worldPos);
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
            DrawBar(u, p, BarWidth(u.BarAnchor));
        }
    }

    void DrawBar(CombatUnit u, Vector2 p, float w)
    {
        float h = 7f;
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
