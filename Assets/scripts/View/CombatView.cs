using System;
using System.Collections.Generic;
using UnityEngine;

// VISTA del combat en un CLIENT (multijugador). No decideix res:
//  - el servidor envia la formació (CombatSetup) -> Play() la re-simula amb CombatSim (determinista)
//    només per fer l'animació, amb el mateix resultat que ha calculat el servidor;
//  - el servidor envia el resultat (CombatEnd) -> End(): si l'animació va endarrerida s'avança fins
//    al final, i el resultat oficial és sempre el del servidor.
// Si el client és l'equip B, el marc es dibuixa girat 180° per veure's a la meitat de baix.
// A l'escena de xarxa NO s'ha de fer servir CombatManager/RoundManager (són del mode un jugador).
[DisallowMultipleComponent]
public class CombatView : MonoBehaviour
{
    [Header("Escena (si són buits es busquen sols)")]
    public BenchView benchView;
    public Camera cam;

    [Header("Combat")]
    public float timeScale = 1f;
    public float autoClearSeconds = 1.5f;   // temps que es veu el resultat abans de recollir les còpies (0 = ho fa PhaseUI amb Clear())
    public float endTimeoutSeconds = 8f;    // si el servidor no envia CombatEnd, es neteja igualment

    [Header("Interfície de combat")]
    public bool showHealthBars = true;
    public bool showResultText = true;

    public bool IsRunning { get; private set; }
    public bool PlaybackFinished => finished;
    public CombatResult LastResult { get; private set; }   // des del punt de vista del jugador local
    public int LastDamageTaken { get; private set; }
    public int StatsRound { get; private set; }

    public event Action<CombatResult> OnCombatEnded;
    public event Action OnCombatCleared;

    struct FloatText { public Vector3 world; public string text; public Color color; public float age; }

    static readonly Color playerCol = new Color(0.3f, 1f, 0.4f, 1f);
    static readonly Color enemyCol = new Color(1f, 0.3f, 0.3f, 1f);
    const float FloatLife = 0.9f;
    const float TickSeconds = 1f / SimConfig.TicksPerSecond;

    ShopManager shopMgr;
    BoardWorld world;
    CombatSim sim;
    float tileRadius;
    Transform root;
    SimTeam localTeam;
    bool flipped;
    readonly List<CombatUnit> units = new List<CombatUnit>();
    readonly Dictionary<int, CombatUnit> byUid = new Dictionary<int, CombatUnit>();
    readonly List<FloatText> floats = new List<FloatText>();
    readonly List<UnitDamageStat> lastStats = new List<UnitDamageStat>();
    readonly System.Random jitter = new System.Random(); // només per a la posició visual dels números de dany
    float acc, leadLeft, waitEnd, clearLeft;
    bool finished, ended;
    GUIStyle bigStyle, smallStyle, floatStyle;

    // ---------------------------------------------------------------- inici (ordre del servidor)
    public bool Play(CombatSetup setup)
    {
        if (IsRunning) Clear(); // queda un combat anterior

        if (benchView == null) benchView = FindFirstObjectByType<BenchView>();
        if (shopMgr == null) shopMgr = FindFirstObjectByType<ShopManager>();
        if (cam == null) cam = Camera.main;
        if (benchView == null || benchView.BoardRoot == null || cam == null)
        {
            Debug.LogError("[CombatView] Falta el BenchView/tauler o la càmera.");
            return false;
        }

        world = BoardWorld.Build(benchView.BoardRoot);
        if (world == null)
        {
            Debug.LogError("[CombatView] No s'han trobat les caselles Tile_c_r del tauler.");
            return false;
        }
        world.Flip = setup.Flipped;
        flipped = setup.Flipped;
        localTeam = flipped ? SimTeam.B : SimTeam.A;
        tileRadius = benchView.TileRadius;
        StatsRound = setup.Round;

        root = new GameObject("CombatUnits").transform;
        units.Clear();
        byUid.Clear();
        floats.Clear();
        lastStats.Clear();

        sim = new CombatSim(setup.Units);

        foreach (var su in sim.Units)
        {
            var def = UnitLookup.ById(su.DefId);
            var copy = new UnitInstance(def, su.Uid) { Star = su.Star };
            var v = UnitView.Create(copy, tileRadius);
            v.transform.SetParent(root, false);
            v.Refresh();
            var team = su.Team == localTeam ? CombatTeam.Player : CombatTeam.Enemy;
            var cu = new CombatUnit(su, copy, team, v, world, tileRadius);
            cu.Damaged += OnUnitDamaged;
            units.Add(cu);
            byUid[su.Uid] = cu;
        }

        benchView.SetBoardUnitsVisible(false); // les unitats "reals" s'amaguen mentre lluiten les còpies
        if (shopMgr != null) shopMgr.LockInput = true;

        acc = 0f;
        waitEnd = 0f;
        leadLeft = MatchState.CombatLeadTicks * TickSeconds; // petit marge perquè tothom tingui la formació
        finished = false;
        ended = false;
        LastDamageTaken = 0;
        IsRunning = true;
        return true;
    }

    // ---------------------------------------------------------------- final (ordre del servidor)
    public void End(CombatEnd end)
    {
        if (!IsRunning || ended) return;

        if (!finished) // l'animació va endarrerida respecte al servidor: s'avança fins al final
        {
            leadLeft = 0f;
            while (!sim.Finished) { sim.Tick(); ProcessEvents(); }
            FinishPlayback();
        }

        // El resultat oficial és el del servidor
        bool localIsA = !end.Flipped;
        CombatOutcome o = end.Outcome == SimOutcome.Draw ? CombatOutcome.Draw
                        : ((end.Outcome == SimOutcome.TeamAWon) == localIsA ? CombatOutcome.PlayerWon : CombatOutcome.EnemyWon);
        LastResult = new CombatResult
        {
            Outcome = o,
            PlayerSurvivors = localIsA ? end.SurvivorsA : end.SurvivorsB,
            EnemySurvivors = localIsA ? end.SurvivorsB : end.SurvivorsA,
            Duration = end.Ticks * TickSeconds
        };
        LastDamageTaken = end.DamageTaken;
        ended = true;
        clearLeft = autoClearSeconds;
        OnCombatEnded?.Invoke(LastResult);
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

        for (int i = floats.Count - 1; i >= 0; i--) // números de dany (temps real)
        {
            var f = floats[i];
            f.age += Time.deltaTime;
            if (f.age >= FloatLife) floats.RemoveAt(i); else floats[i] = f;
        }

        float dt = Mathf.Min(Time.deltaTime, 0.05f) * timeScale;

        if (!finished)
        {
            if (leadLeft > 0f) leadLeft -= dt;
            else
            {
                acc += dt;
                while (acc >= TickSeconds && !sim.Finished)
                {
                    acc -= TickSeconds;
                    sim.Tick();
                    ProcessEvents();
                }
                foreach (var u in units)
                    u.Target = byUid.TryGetValue(u.Sim.TargetUid, out var tg) ? tg : null;
                if (sim.Finished) FinishPlayback();
            }
        }

        for (int i = 0; i < units.Count; i++) units[i].Advance(dt);

        if (finished && !ended) // esperem el CombatEnd del servidor
        {
            waitEnd += Time.unscaledDeltaTime;
            if (waitEnd > endTimeoutSeconds) Clear();
        }
        else if (ended && autoClearSeconds > 0f)
        {
            clearLeft -= Time.unscaledDeltaTime;
            if (clearLeft <= 0f) Clear();
        }
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

    // L'animació local ha acabat (el resultat definitiu l'envia el servidor)
    void FinishPlayback()
    {
        var r = sim.Result;
        bool localIsA = localTeam == SimTeam.A;
        CombatOutcome o = r.Outcome == SimOutcome.Draw ? CombatOutcome.Draw
                        : ((r.Outcome == SimOutcome.TeamAWon) == localIsA ? CombatOutcome.PlayerWon : CombatOutcome.EnemyWon);
        LastResult = new CombatResult
        {
            Outcome = o,
            PlayerSurvivors = localIsA ? r.SurvivorsA : r.SurvivorsB,
            EnemySurvivors = localIsA ? r.SurvivorsB : r.SurvivorsA,
            Duration = r.Ticks * TickSeconds
        };
        finished = true;
    }

    // Dany de cada unitat: en directe durant el combat i, un cop acabat, l'últim combat (pestanya Estadístiques)
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

    // Recull les còpies i torna les unitats reals. Es crida sol (autoClearSeconds) o des de PhaseUI en entrar a Prep.
    public void Clear()
    {
        if (!IsRunning) return;

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

        if (shopMgr != null) shopMgr.LockInput = false;
        if (benchView != null) benchView.SetBoardUnitsVisible(true);

        IsRunning = false;
        finished = false;
        ended = false;
        OnCombatCleared?.Invoke();
    }

    void OnDestroy()
    {
        if (IsRunning) Clear();
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
        foreach (var u in units)
        {
            if (!u.Alive || !ToGui(u.BarAnchor, out var p)) continue;
            DrawBar(u, p, BarWidth(u.BarAnchor));
        }
        DrawFloatingTexts();
    }

    void DrawResult()
    {
        if (bigStyle == null)
            bigStyle = new GUIStyle(GUI.skin.label) { fontSize = 36, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        string msg = LastResult.Outcome == CombatOutcome.PlayerWon ? "Has guanyat!"
                   : LastResult.Outcome == CombatOutcome.EnemyWon ? "Has perdut" : "Empat";
        if (ended && LastDamageTaken > 0)
            msg += "  (-" + LastDamageTaken + (LastDamageTaken == 1 ? " vida)" : " vides)");
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

        float step = 100f; // marques cada 100 de vida
        while (max / step > 14f) step *= 2f;
        for (float x = step; x < max; x += step)
            Fill(new Rect(r.x + r.width * (x / max), r.y, 1f, r.height), new Color(0f, 0f, 0f, 0.55f));

        for (int i = 0; i < u.Source.Star; i++) // estrelles
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
