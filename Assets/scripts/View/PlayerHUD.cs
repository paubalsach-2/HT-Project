using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem.UI;
#endif

// Una fila de la pestanya "Ronda". Quan hi hagi multijugador s'ompliran amb dades reals
// (PlayerHUD.SetOpponents / SetOpponentLives). Fins llavors hi ha jugadors de mentida.
public class PlayerEntry
{
    public string name;
    public int lives, maxLives;
}

// Crea per codi el HUD d'un jugador (Canvas, panells, textos i botons de prova).
// Només LLEGEIX de PlayerStats / RoundManager / CombatManager: no té cap lògica de joc.
// Ús: posar PlayerStats + PlayerHUD al mateix GameObject i assignar les fonts TMP.
[RequireComponent(typeof(PlayerStats))]
public class PlayerHUD : MonoBehaviour
{
    [Header("Tipografia (TMP Font Asset)")]
    public TMP_FontAsset fontLabel;    // Cinzel Medium
    public TMP_FontAsset fontNumber;   // Cinzel Bold (si és buit, usa fontLabel)

    [Header("Opcions")]
    public bool isLocalPlayer = true;  // només el jugador local construeix HUD
    public bool showDebugPanel = true;
    public int debugGoldStep = 5;
    public int debugLifeStep = 1;
    [Range(0f, 1f)] public float lowLifeThreshold = 0.25f;

    [Header("Panell Ronda / Estadístiques")]
    public bool showInfoPanel = true;
    public string localPlayerName = "Tu";
    public int maxStatRows = 12;                  // màxim de personatges a la pestanya Estadístiques
    public int placeholderOpponents = 7;          // jugadors de mentida fins que hi hagi multijugador
    public bool simulatePlaceholderDamage = false; // si és true, els de mentida perden vida cada ronda (per provar)

    [Header("Posició (canvas de referència 1920x1080)")]
    public Vector2 goldPosition = new Vector2(24f, 8f);     // des de baix a l'esquerra
    public Vector2 livesPosition = new Vector2(-24f, -24f); // des de dalt a la dreta
    public Vector2 infoPosition = new Vector2(-24f, -140f); // des de dalt a la dreta (sota les vides)
    public Vector2 debugPosition = new Vector2(-24f, 24f);  // des de baix a la dreta

    [Header("Paleta")]
    public Color bronze = new Color32(184, 137, 63, 255);
    public Color panelDark = new Color32(35, 26, 18, 235);
    public Color buttonColor = new Color32(58, 44, 29, 255);
    public Color textCream = new Color32(239, 227, 198, 255);
    public Color goldText = new Color32(242, 210, 122, 255);
    public Color labelTan = new Color32(168, 137, 90, 255);
    public Color gain = new Color32(127, 191, 106, 255);
    public Color danger = new Color32(224, 88, 74, 255);
    public Color heartRed = new Color32(200, 68, 58, 255);

    class Stat
    {
        public TextMeshProUGUI number, popup;
        public Image border;
        public Color baseBorder;
        public float shown;
        public Coroutine anim;
    }

    class Row
    {
        public GameObject go;
        public Image bg, fill;
        public TextMeshProUGUI left, right;
    }

    const float AnimTime = 0.6f;
    static readonly Vector2 PopupPos = new Vector2(-16f, 4f);

    // mides del panell Ronda / Estadístiques
    const float LivesPanelH = 108f;
    const float InfoW = 300f, TabW = 137f, TabH = 34f;
    const float RowsTop = 84f, RowH = 26f, RowStep = 30f;
    const float RefreshEvery = 0.2f;

    PlayerStats stats;
    RoundManager roundMgr;
    CombatManager combatMgr;
    Stat gold = new Stat(), lives = new Stat();
    TextMeshProUGUI livesMax;
    RectTransform livesFill;
    Image livesFillImg;
    Sprite sRound, sCoin, sHeart;
    Transform canvasRoot;
    GameObject debugRoot;

    // panell Ronda / Estadístiques
    Image infoBorder, tabRoundImg, tabStatsImg;
    TextMeshProUGUI infoHeader, infoHeaderRight;
    readonly List<Row> rows = new List<Row>();
    readonly List<PlayerEntry> opponents = new List<PlayerEntry>();
    readonly List<PlayerEntry> entries = new List<PlayerEntry>();
    readonly List<UnitDamageStat> statBuf = new List<UnitDamageStat>();
    readonly PlayerEntry localEntry = new PlayerEntry();
    System.Comparison<PlayerEntry> entryCmp;
    System.Comparison<UnitDamageStat> statCmp;
    int tab;
    float infoTimer;

    // ---------------------------------------------------------------- cicle de vida
    void Start()
    {
        if (!isLocalPlayer) return;
        stats = GetComponent<PlayerStats>();
        roundMgr = GetComponent<RoundManager>();
        combatMgr = GetComponent<CombatManager>();
        entryCmp = CompareEntries;
        statCmp = CompareStats;

        EnsureEventSystem();
        BuildSprites();
        BuildCanvas();
        BuildGold();
        BuildLives();
        if (showInfoPanel)
        {
            BuildPlaceholders();
            BuildInfoPanel();
        }
        if (showDebugPanel) BuildDebug();

        RefreshGold(stats.Gold, 0, false);
        RefreshLives(stats.Lives, 0, false);

        stats.OnGoldChanged += OnGold;
        stats.OnLivesChanged += OnLives;
        if (roundMgr != null) roundMgr.OnRoundChanged += OnRound;
    }

    void OnDestroy()
    {
        if (stats == null) return;
        stats.OnGoldChanged -= OnGold;
        stats.OnLivesChanged -= OnLives;
        if (roundMgr != null) roundMgr.OnRoundChanged -= OnRound;
    }

    void Update()
    {
        if (infoBorder == null) return;
        infoTimer -= Time.unscaledDeltaTime;
        if (infoTimer > 0f) return;
        infoTimer = RefreshEvery;
        RefreshInfo();
    }

    public void SetDebugVisible(bool v)
    {
        showDebugPanel = v;
        if (debugRoot != null) debugRoot.SetActive(v);
    }

    // ---------------------------------------------------------------- API per al multijugador
    // Substitueix els jugadors de mentida per la llista real dels rivals
    public void SetOpponents(IList<PlayerEntry> list)
    {
        opponents.Clear();
        if (list != null) opponents.AddRange(list);
        infoTimer = 0f;
    }

    // Actualitza la vida d'un rival pel seu nom
    public void SetOpponentLives(string playerName, int newLives)
    {
        foreach (var o in opponents)
        {
            if (o.name != playerName) continue;
            o.lives = Mathf.Clamp(newLives, 0, o.maxLives);
            infoTimer = 0f;
            return;
        }
    }

    void OnGold(int v, int d) => RefreshGold(v, d, true);
    void OnLives(int v, int d) { RefreshLives(v, d, true); infoTimer = 0f; }

    void OnRound(int round)
    {
        if (simulatePlaceholderDamage)
            foreach (var o in opponents) o.lives = Mathf.Max(0, o.lives - Random.Range(0, 5));
        infoTimer = 0f;
    }

    // ---------------------------------------------------------------- refresc
    void RefreshGold(int v, int delta, bool animate)
    {
        Run(gold, v, delta, delta > 0 ? gain : textCream, goldText, bronze, animate);
    }

    void RefreshLives(int v, int delta, bool animate)
    {
        int max = stats.MaxLives;
        bool low = v <= Mathf.CeilToInt(max * lowLifeThreshold);
        livesMax.text = "/ " + max;
        livesFill.anchorMax = new Vector2(max > 0 ? (float)v / max : 0f, 1f);
        livesFillImg.color = low ? danger : heartRed;
        Run(lives, v, delta, delta > 0 ? gain : danger, low ? danger : textCream, low ? danger : bronze, animate);
    }

    void Run(Stat s, int to, int delta, Color flash, Color numberColor, Color borderColor, bool animate)
    {
        s.baseBorder = borderColor;
        s.number.color = numberColor;
        if (s.anim != null) StopCoroutine(s.anim);
        s.anim = StartCoroutine(Animate(s, to, delta, flash, animate));
    }

    // Compte numèric, flaix a la vora, pols del número i text flotant (+5 / -1)
    IEnumerator Animate(Stat s, int to, int delta, Color flash, bool animate)
    {
        var numRt = s.number.rectTransform;
        var popRt = s.popup.rectTransform;

        if (animate)
        {
            float from = s.shown;
            if (delta != 0) { s.popup.text = (delta > 0 ? "+" : "") + delta; s.popup.color = flash; }
            for (float t = 0f; t < AnimTime; t += Time.unscaledDeltaTime)
            {
                float k = t / AnimTime;
                s.shown = Mathf.Lerp(from, to, k);
                s.number.text = Mathf.RoundToInt(s.shown).ToString();
                s.border.color = Color.Lerp(flash, s.baseBorder, k);
                numRt.localScale = Vector3.one * (1f + 0.15f * Mathf.Sin(k * Mathf.PI));
                if (delta != 0)
                {
                    popRt.anchoredPosition = PopupPos + Vector2.up * (36f * k);
                    var c = s.popup.color; c.a = 1f - k; s.popup.color = c;
                }
                yield return null;
            }
        }

        s.shown = to;
        s.number.text = to.ToString();
        s.border.color = s.baseBorder;
        numRt.localScale = Vector3.one;
        var pc = s.popup.color; pc.a = 0f; s.popup.color = pc;
        s.anim = null;
    }

    // ---------------------------------------------------------------- panell Ronda / Estadístiques
    void BuildPlaceholders()
    {
        opponents.Clear();
        for (int i = 0; i < placeholderOpponents; i++)
            opponents.Add(new PlayerEntry { name = "Jugador " + (i + 2), lives = stats.MaxLives, maxLives = stats.MaxLives });
    }

    void BuildInfoPanel()
    {
        // vora i fons (la vora té raycast: clicar el panell no mou el personatge)
        infoBorder = Img("InfoPanel", canvasRoot, sRound, bronze, true, true);
        Place(infoBorder.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), infoPosition, new Vector2(InfoW, 100f));
        var inner = Img("Inner", infoBorder.transform, sRound, panelDark, true);
        Stretch(inner.rectTransform, 3f);

        tabRoundImg = MakeTab(infoBorder.transform, "RONDA", new Vector2(10f, -10f), () => SetTab(0));
        tabStatsImg = MakeTab(infoBorder.transform, "ESTADÍSTIQUES", new Vector2(10f + TabW + 6f, -10f), () => SetTab(1));

        infoHeader = Txt("Header", infoBorder.transform, "", 22f, goldText, TextAlignmentOptions.Left, true);
        Place(infoHeader.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -52f), new Vector2(180f, 28f));

        infoHeaderRight = Txt("HeaderRight", infoBorder.transform, "", 18f, labelTan, TextAlignmentOptions.Right, true);
        Place(infoHeaderRight.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-14f, -54f), new Vector2(110f, 26f));

        SetTab(0);
    }

    Image MakeTab(Transform parent, string text, Vector2 pos, UnityAction onClick)
    {
        var img = Img("Tab_" + text, parent, sRound, buttonColor, true, true);
        Place(img.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), pos, new Vector2(TabW, TabH));

        var b = img.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        b.navigation = new Navigation { mode = Navigation.Mode.None }; // WASD/fletxes no han de moure la selecció
        var cb = b.colors;
        cb.normalColor = new Color(0.85f, 0.85f, 0.85f, 1f);
        cb.highlightedColor = Color.white;
        cb.pressedColor = new Color(0.6f, 0.6f, 0.6f, 1f);
        b.colors = cb;
        b.onClick.AddListener(onClick);

        var t = Txt("Text", img.transform, text, 18f, textCream, TextAlignmentOptions.Center, false);
        t.enableAutoSizing = true;
        t.fontSizeMin = 10f;
        t.fontSizeMax = 18f;
        Stretch(t.rectTransform, 3f);
        return img;
    }

    void SetTab(int t)
    {
        tab = t;
        Color active = Color.Lerp(buttonColor, bronze, 0.65f);
        tabRoundImg.color = t == 0 ? active : buttonColor;
        tabStatsImg.color = t == 1 ? active : buttonColor;
        infoTimer = RefreshEvery;
        RefreshInfo();
    }

    void RefreshInfo()
    {
        if (infoBorder == null) return;
        if (tab == 0) RefreshRoundTab(); else RefreshStatsTab();
    }

    // Pestanya Ronda: número de ronda + vida de tots els jugadors
    void RefreshRoundTab()
    {
        int round = roundMgr != null ? roundMgr.Round : 1;
        infoHeader.text = "RONDA " + round;
        infoHeaderRight.text = "";

        localEntry.name = localPlayerName;
        localEntry.lives = stats.Lives;
        localEntry.maxLives = stats.MaxLives;

        entries.Clear();
        entries.Add(localEntry);
        entries.AddRange(opponents);
        entries.Sort(entryCmp);

        SetRowCount(entries.Count);
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            var r = rows[i];
            bool local = e == localEntry;
            bool dead = e.lives <= 0;
            bool low = !dead && e.lives <= Mathf.CeilToInt(e.maxLives * lowLifeThreshold);
            float frac = e.maxLives > 0 ? Mathf.Clamp01((float)e.lives / e.maxLives) : 0f;

            r.left.text = (i + 1) + ". " + e.name;
            r.left.color = dead ? labelTan : (local ? goldText : textCream);
            r.right.text = e.lives.ToString();
            r.right.color = dead ? labelTan : (low ? danger : textCream);
            SetFill(r, frac, low ? danger : (local ? bronze : heartRed));
        }
    }

    // Pestanya Estadístiques: dany fet per cada personatge a l'últim combat (o al que està en curs)
    void RefreshStatsTab()
    {
        statBuf.Clear();
        if (combatMgr != null) combatMgr.GetDamageStats(statBuf);

        if (statBuf.Count == 0)
        {
            infoHeader.text = "SENSE COMBATS";
            infoHeaderRight.text = "";
            SetRowCount(0);
            return;
        }

        statBuf.Sort(statCmp);

        float playerTotal = 0f, enemyTotal = 0f, max = 0f;
        foreach (var s in statBuf)
        {
            if (s.Team == CombatTeam.Player) playerTotal += s.Damage; else enemyTotal += s.Damage;
            if (s.Damage > max) max = s.Damage;
        }

        infoHeader.text = "RONDA " + combatMgr.StatsRound;
        infoHeaderRight.text = "<color=#" + ColorUtility.ToHtmlStringRGB(gain) + ">" + Mathf.RoundToInt(playerTotal)
                             + "</color>  <color=#" + ColorUtility.ToHtmlStringRGB(danger) + ">" + Mathf.RoundToInt(enemyTotal) + "</color>";

        int n = Mathf.Min(statBuf.Count, Mathf.Max(1, maxStatRows));
        SetRowCount(n);
        for (int i = 0; i < n; i++)
        {
            var s = statBuf[i];
            var r = rows[i];
            r.left.text = s.Name + " " + Roman(s.Star);
            r.left.color = s.Alive ? textCream : labelTan;
            r.right.text = Mathf.RoundToInt(s.Damage).ToString();
            r.right.color = s.Alive ? textCream : labelTan;
            SetFill(r, max > 0f ? s.Damage / max : 0f, s.Team == CombatTeam.Player ? gain : danger);
        }
    }

    static string Roman(int star)
    {
        switch (star)
        {
            case 1: return "I";
            case 2: return "II";
            case 3: return "III";
            default: return star.ToString();
        }
    }

    int CompareEntries(PlayerEntry a, PlayerEntry b)
    {
        int c = b.lives.CompareTo(a.lives);
        if (c != 0) return c;
        if (a == localEntry) return -1;
        if (b == localEntry) return 1;
        return string.CompareOrdinal(a.name, b.name);
    }

    static int CompareStats(UnitDamageStat a, UnitDamageStat b)
    {
        int c = b.Damage.CompareTo(a.Damage);
        if (c != 0) return c;
        if (a.Team != b.Team) return a.Team == CombatTeam.Player ? -1 : 1;
        c = string.CompareOrdinal(a.Name, b.Name);
        return c != 0 ? c : b.Star.CompareTo(a.Star);
    }

    void SetFill(Row r, float frac, Color c)
    {
        r.fill.enabled = frac > 0.001f;
        r.fill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(frac), 1f);
        c.a = 0.85f;
        r.fill.color = c;
    }

    // Mostra n files i ajusta l'alçada del panell
    void SetRowCount(int n)
    {
        while (rows.Count < n) rows.Add(MakeRow(rows.Count));
        for (int i = 0; i < rows.Count; i++) rows[i].go.SetActive(i < n);
        infoBorder.rectTransform.sizeDelta = new Vector2(InfoW, RowsTop + n * RowStep + 6f);
    }

    Row MakeRow(int i)
    {
        var r = new Row();
        r.bg = Img("Row" + i, infoBorder.transform, sRound, buttonColor, true);
        r.go = r.bg.gameObject;
        Place(r.bg.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(12f, -(RowsTop + i * RowStep)), new Vector2(InfoW - 24f, RowH));

        r.fill = Img("Fill", r.go.transform, sRound, heartRed, true);
        var fr = r.fill.rectTransform;
        fr.anchorMin = Vector2.zero;
        fr.anchorMax = Vector2.one;
        fr.offsetMin = fr.offsetMax = Vector2.zero;

        r.left = Txt("Name", r.go.transform, "", 17f, textCream, TextAlignmentOptions.Left, false);
        r.left.overflowMode = TextOverflowModes.Ellipsis;
        StretchH(r.left.rectTransform, 8f, 70f);

        r.right = Txt("Value", r.go.transform, "", 18f, textCream, TextAlignmentOptions.Right, true);
        StretchH(r.right.rectTransform, 8f, 8f);
        return r;
    }

    static void StretchH(RectTransform rt, float left, float right)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(left, 0f);
        rt.offsetMax = new Vector2(-right, 0f);
    }

    // ---------------------------------------------------------------- construcció
    void BuildCanvas()
    {
        var go = new GameObject("HUD_Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.transform.SetParent(transform, false);
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        var sc = go.GetComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920f, 1080f);
        sc.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        sc.matchWidthOrHeight = 0.5f;
        canvasRoot = go.transform;
    }

    void BuildGold()
    {
        var p = Panel("GoldPanel", canvasRoot, new Vector2(0f, 0f), goldPosition, new Vector2(290f, 100f), bronze, panelDark, out gold.border);

        var coin = Img("Coin", p, sCoin, Color.white);
        Place(coin.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(20f, 0f), new Vector2(60f, 60f));

        var label = Txt("Label", p, "OR", 20f, labelTan, TextAlignmentOptions.TopLeft, false);
        Place(label.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(98f, -8f), new Vector2(120f, 24f));

        gold.number = Txt("Number", p, "0", 48f, goldText, TextAlignmentOptions.Left, true);
        Place(gold.number.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0.5f), new Vector2(98f, 36f), new Vector2(170f, 56f));

        gold.popup = MakePopup(p);
    }

    void BuildLives()
    {
        var p = Panel("LivesPanel", canvasRoot, new Vector2(1f, 1f), livesPosition, new Vector2(InfoW, LivesPanelH), bronze, panelDark, out lives.border);

        var heart = Img("Heart", p, sHeart, heartRed);
        Place(heart.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(18f, 2f), new Vector2(48f, 48f));

        var label = Txt("Label", p, "VIDES", 18f, labelTan, TextAlignmentOptions.TopLeft, false);
        Place(label.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(88f, -8f), new Vector2(120f, 22f));

        lives.number = Txt("Number", p, "0", 42f, textCream, TextAlignmentOptions.Left, true);
        Place(lives.number.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0.5f), new Vector2(88f, 42f), new Vector2(100f, 48f));

        livesMax = Txt("Max", p, "/ 0", 24f, labelTan, TextAlignmentOptions.Right, false);
        Place(livesMax.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0.5f), new Vector2(-20f, 40f), new Vector2(90f, 30f));

        var barBg = Img("BarBg", p, sRound, buttonColor, true);
        Place(barBg.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(88f, 8f), new Vector2(192f, 8f));
        livesFillImg = Img("BarFill", barBg.transform, sRound, heartRed, true);
        livesFill = livesFillImg.rectTransform;
        livesFill.anchorMin = Vector2.zero;
        livesFill.anchorMax = Vector2.one;
        livesFill.offsetMin = livesFill.offsetMax = Vector2.zero;

        lives.popup = MakePopup(p);
    }

    void BuildDebug()
    {
        var dim = new Color(bronze.r, bronze.g, bronze.b, 0.5f);
        var p = Panel("DebugPanel", canvasRoot, new Vector2(1f, 0f), debugPosition, new Vector2(340f, 196f), dim, new Color(0.1f, 0.08f, 0.06f, 0.9f), out _);
        debugRoot = p.gameObject;

        var label = Txt("Label", p, "DEBUG", 20f, labelTan, TextAlignmentOptions.TopLeft, false);
        Place(label.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -10f), new Vector2(160f, 26f));

        DebugButton(p, "+" + debugGoldStep + " or", new Vector2(12f, -46f), () => stats.AddGold(debugGoldStep));
        DebugButton(p, "-" + debugGoldStep + " or", new Vector2(178f, -46f), () => stats.AddGold(-debugGoldStep));
        DebugButton(p, "+" + debugLifeStep + " vida", new Vector2(12f, -120f), () => stats.AddLives(debugLifeStep));
        DebugButton(p, "-" + debugLifeStep + " vida", new Vector2(178f, -120f), () => stats.AddLives(-debugLifeStep));
    }

    void DebugButton(Transform parent, string text, Vector2 pos, UnityAction onClick)
    {
        var img = Img("Btn_" + text, parent, sRound, Color.white, true, true);
        Place(img.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), pos, new Vector2(150f, 60f));
        img.color = buttonColor;

        var b = img.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        var cb = b.colors;
        cb.normalColor = new Color(0.85f, 0.85f, 0.85f, 1f);
        cb.highlightedColor = Color.white;
        cb.pressedColor = new Color(0.6f, 0.6f, 0.6f, 1f);
        b.colors = cb;
        b.onClick.AddListener(onClick);

        var t = Txt("Text", img.transform, text, 28f, textCream, TextAlignmentOptions.Center, false);
        Stretch(t.rectTransform, 0f);
    }

    TextMeshProUGUI MakePopup(Transform parent)
    {
        var t = Txt("Popup", parent, "", 28f, gain, TextAlignmentOptions.Right, true);
        Place(t.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 0f), PopupPos, new Vector2(120f, 36f));
        var c = t.color; c.a = 0f; t.color = c;
        return t;
    }

    // ---------------------------------------------------------------- helpers UI
    // Panell amb vora de bronze i fons fosc. El pivot coincideix amb l'ancoratge.
    Transform Panel(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, Color border, Color fill, out Image borderImg)
    {
        borderImg = Img(name, parent, sRound, border, true);
        Place(borderImg.rectTransform, anchor, anchor, pos, size);
        var inner = Img("Inner", borderImg.transform, sRound, fill, true);
        Stretch(inner.rectTransform, 3f);
        return borderImg.transform;
    }

    Image Img(string name, Transform parent, Sprite sprite, Color color, bool sliced = false, bool raycast = false)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
        img.raycastTarget = raycast;
        return img;
    }

    TextMeshProUGUI Txt(string name, Transform parent, string text, float size, Color color, TextAlignmentOptions align, bool number)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        var f = number && fontNumber != null ? fontNumber : fontLabel;
        if (f != null) t.font = f;
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.overflowMode = TextOverflowModes.Overflow;
        t.raycastTarget = false;
        return t;
    }

    static void Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    static void Stretch(RectTransform rt, float m)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(m, m);
        rt.offsetMax = new Vector2(-m, -m);
    }

    static void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() != null) return;
        var go = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        go.AddComponent<InputSystemUIInputModule>();
#else
        go.AddComponent<StandaloneInputModule>();
#endif
    }

    // ---------------------------------------------------------------- sprites procedurals
    // Sense assets externs: rectangle arrodonit (9-slice), moneda i cor.
    void BuildSprites()
    {
        sRound = MakeSprite(32, new Vector4(10, 10, 10, 10), (x, y) =>
        {
            float px = x + 0.5f - 16f, py = y + 0.5f - 16f, r = 10f;
            float qx = Mathf.Abs(px) - (16f - r), qy = Mathf.Abs(py) - (16f - r);
            float d = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
            return new Color(1f, 1f, 1f, Cov(d));
        });

        sCoin = MakeSprite(64, Vector4.zero, (x, y) =>
        {
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(32f, 32f));
            Color c = new Color32(138, 100, 23, 255);
            c = Color.Lerp(c, new Color32(224, 178, 65, 255), Cov(d - 28f));
            c = Color.Lerp(c, new Color32(246, 217, 122, 255), Cov(Mathf.Abs(d - 17f) - 1.2f));
            c.a = Cov(d - 31.5f);
            return c;
        });

        sHeart = MakeSprite(64, Vector4.zero, (x, y) =>
        {
            float outer = HeartCov(x, y, 1f), inner = HeartCov(x, y, 1.18f);
            Color c = Color.Lerp(new Color32(107, 29, 23, 255), Color.white, inner);
            c.a = outer;
            return c;
        });
    }

    static float Cov(float d) => Mathf.Clamp01(0.5f - d);

    // Corba implícita del cor: (x²+y²-1)³ - x²y³ <= 0, amb 3x3 de suavitzat
    static float HeartCov(int x, int y, float k)
    {
        int hit = 0;
        for (int sy = 0; sy < 3; sy++)
            for (int sx = 0; sx < 3; sx++)
            {
                float u = ((x + (sx + 0.5f) / 3f) / 64f * 2f - 1f) * 1.4f * k;
                float v = ((y + (sy + 0.5f) / 3f) / 64f * 2f - 1f) * 1.4f * k + 0.12f;
                float a = u * u + v * v - 1f;
                if (a * a * a - u * u * v * v * v <= 0f) hit++;
            }
        return hit / 9f;
    }

    static Sprite MakeSprite(int size, Vector4 border, System.Func<int, int, Color> px)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave
        };
        var cols = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                cols[y * size + x] = px(x, y);
        tex.SetPixels(cols);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
    }
}