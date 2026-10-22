using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem.UI;
#endif

// Crea per codi el HUD d'un jugador (Canvas, panells, textos i botons de prova).
// Només LLEGEIX de PlayerStats i escolta els seus events: no té cap lògica de joc.
// Ús: posar PlayerStats + PlayerHUD al mateix GameObject i assignar les fonts TMP.
[RequireComponent(typeof(PlayerStats))]
public class PlayerHUD : MonoBehaviour
{
    [Header("Tipografia (TMP Font Asset)")]
    public TMP_FontAsset fontLabel;
    public TMP_FontAsset fontNumber;

    [Header("Opcions")]
    public bool isLocalPlayer = true;
    public bool showDebugPanel = true;
    public int debugGoldStep = 5;
    public int debugLifeStep = 1;
    [Range(0f, 1f)] public float lowLifeThreshold = 0.25f;

    [Header("Posició (canvas de referència 1920x1080)")]
    public Vector2 goldPosition = new Vector2(24f, 0f);
    public Vector2 livesPosition = new Vector2(-24f, -24f);
    public Vector2 debugPosition = new Vector2(-24f, 24f);

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

    const float AnimTime = 0.6f;
    static readonly Vector2 PopupPos = new Vector2(-16f, 4f);

    PlayerStats stats;
    Stat gold = new Stat(), lives = new Stat();
    TextMeshProUGUI livesMax;
    RectTransform livesFill;
    Image livesFillImg;
    Sprite sRound, sCoin, sHeart;
    Transform canvasRoot;
    GameObject debugRoot;
    GameObject gameOverRoot;
    bool gameOverShown;
    GameObject settingsRoot;
    TextMeshProUGUI settingsStatus;

    void Start()
    {
        if (!isLocalPlayer) return;
        stats = GetComponent<PlayerStats>();

        EnsureEventSystem();
        BuildSprites();
        BuildCanvas();
        BuildGold();
        BuildLives();
        if (showDebugPanel) BuildDebug();
        BuildSettingsMenu();
        BuildGameOver();

        RefreshGold(stats.Gold, 0, false);
        RefreshLives(stats.Lives, 0, false);

        stats.OnGoldChanged += OnGold;
        stats.OnLivesChanged += OnLives;

        if (stats.Lives <= 0)
            ShowGameOver();
    }

    void OnDestroy()
    {
        if (stats == null) return;
        stats.OnGoldChanged -= OnGold;
        stats.OnLivesChanged -= OnLives;
    }

    public void SetDebugVisible(bool v)
    {
        showDebugPanel = v;
        if (debugRoot != null) debugRoot.SetActive(v);
    }

    void OnGold(int v, int d) => RefreshGold(v, d, true);

    void OnLives(int v, int d)
    {
        RefreshLives(v, d, true);
        if (v <= 0)
            ShowGameOver();
    }

    void RefreshGold(int v, int delta, bool animate)
    {
        Run(gold, v, delta, delta > 0 ? gain : textCream, goldText, bronze, animate);
    }

    void RefreshLives(int v, int delta, bool animate)
    {
        int max = stats.MaxLives;
        bool low = v <= Mathf.CeilToInt(max * lowLifeThreshold);
        livesMax.text = "/ " + max;
        livesFill.anchorMax = new Vector2(max > 0 ? Mathf.Clamp01((float)v / max) : 0f, 1f);
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
        var p = Panel("LivesPanel", canvasRoot, new Vector2(1f, 1f), livesPosition, new Vector2(300f, 108f), bronze, panelDark, out lives.border);
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


    void BuildSettingsMenu()
    {
        var openImage = Img("SettingsButton", canvasRoot, sRound, panelDark, true, true);
        Place(openImage.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(24f, -24f), new Vector2(190f, 58f));
        var openText = Txt("SettingsButtonText", openImage.transform, "CONFIGURACIÓ", 20f,
            goldText, TextAlignmentOptions.Center, false);
        Stretch(openText.rectTransform, 4f);
        var openButton = openImage.gameObject.AddComponent<Button>();
        openButton.targetGraphic = openImage;
        SetButtonColors(openButton);
        openButton.onClick.AddListener(() => settingsRoot.SetActive(!settingsRoot.activeSelf));

        var overlay = Img("SettingsOverlay", canvasRoot, null, new Color(0f, 0f, 0f, 0.65f), false, true);
        Stretch(overlay.rectTransform, 0f);
        settingsRoot = overlay.gameObject;

        var panel = Panel("SettingsPanel", overlay.transform, new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(650f, 500f), bronze, panelDark, out _);

        var title = Txt("SettingsTitle", panel, "CONFIGURACIÓ", 42f, goldText,
            TextAlignmentOptions.Center, true);
        Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -28f), new Vector2(580f, 60f));

        var description = Txt("SettingsDescription", panel, "Tria la qualitat gràfica", 24f,
            textCream, TextAlignmentOptions.Center, false);
        Place(description.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -85f), new Vector2(580f, 40f));

        settingsStatus = Txt("SettingsStatus", panel, "", 22f, labelTan,
            TextAlignmentOptions.Center, false);
        Place(settingsStatus.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -125f), new Vector2(580f, 36f));

        CreateSettingsOption(panel, "MOLT BAIX", new Vector2(0f, -175f), 0);
        CreateSettingsOption(panel, "BAIX", new Vector2(0f, -230f), 1);
        CreateSettingsOption(panel, "MITJÀ", new Vector2(0f, -285f), 2);
        CreateSettingsOption(panel, "ALT", new Vector2(0f, -340f), 3);

        var closeImage = Img("CloseSettings", panel, sRound, buttonColor, true, true);
        Place(closeImage.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 15f), new Vector2(220f, 45f));
        var closeButton = closeImage.gameObject.AddComponent<Button>();
        closeButton.targetGraphic = closeImage;
        SetButtonColors(closeButton);
        closeButton.onClick.AddListener(() => settingsRoot.SetActive(false));
        var closeText = Txt("CloseSettingsText", closeImage.transform, "TANCAR", 20f,
            textCream, TextAlignmentOptions.Center, false);
        Stretch(closeText.rectTransform, 4f);

        settingsRoot.SetActive(false);
        UpdateSettingsStatus(2);
    }

    void CreateSettingsOption(Transform parent, string label, Vector2 position, int preset)
    {
        var image = Img("Quality_" + label, parent, sRound, buttonColor, true, true);
        Place(image.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            position, new Vector2(420f, 45f));
        var button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        SetButtonColors(button);
        button.onClick.AddListener(() => ApplyQualityPreset(preset));

        var text = Txt("Text", image.transform, label, 21f, textCream,
            TextAlignmentOptions.Center, false);
        Stretch(text.rectTransform, 3f);
    }

    void ApplyQualityPreset(int preset)
    {
        int levels = QualitySettings.names.Length;
        if (levels > 0)
        {
            int index = Mathf.RoundToInt((preset / 3f) * (levels - 1));
            QualitySettings.SetQualityLevel(Mathf.Clamp(index, 0, levels - 1), true);
        }

        switch (preset)
        {
            case 0:
                QualitySettings.shadows = ShadowQuality.Disable;
                QualitySettings.shadowDistance = 0f;
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = 60;
                break;
            case 1:
                QualitySettings.shadows = ShadowQuality.HardOnly;
                QualitySettings.shadowDistance = 20f;
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = 60;
                break;
            case 2:
                QualitySettings.shadows = ShadowQuality.All;
                QualitySettings.shadowDistance = 40f;
                QualitySettings.vSyncCount = 1;
                Application.targetFrameRate = 60;
                break;
            default:
                QualitySettings.shadows = ShadowQuality.All;
                QualitySettings.shadowDistance = 80f;
                QualitySettings.vSyncCount = 1;
                Application.targetFrameRate = -1;
                break;
        }

        UpdateSettingsStatus(preset);
    }

    void UpdateSettingsStatus(int preset)
    {
        if (settingsStatus == null) return;
        string[] names = { "Molt baix", "Baix", "Mitjà", "Alt" };
        settingsStatus.text = "Nivell actual: " + names[Mathf.Clamp(preset, 0, 3)];
    }

    void SetButtonColors(Button button)
    {
        var colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = Color.white;
        colors.pressedColor = Color.white;
        colors.selectedColor = Color.white;
        colors.disabledColor = Color.white;
        colors.colorMultiplier = 1f;
        button.colors = colors;
    }

    void BuildGameOver()
    {
        var overlay = Img("GameOverOverlay", canvasRoot, null, new Color(0f, 0f, 0f, 0.65f), false, true);
        Stretch(overlay.rectTransform, 0f);
        gameOverRoot = overlay.gameObject;

        var p = Panel("GameOverPanel", overlay.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(600f, 330f), bronze, panelDark, out _);

        var title = Txt("GameOverTitle", p, "HAS PERDUT", 48f, goldText, TextAlignmentOptions.Center, true);
        Place(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 75f), new Vector2(540f, 75f));

        var subtitle = Txt("GameOverSubtitle", p, "T'has quedat sense vides", 24f, textCream, TextAlignmentOptions.Center, false);
        Place(subtitle.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(520f, 45f));

        CreateGameOverButton(p, "TORNAR A JUGAR", new Vector2(-145f, -75f), RestartGame);
        CreateGameOverButton(p, "TANCAR EL JOC", new Vector2(145f, -75f), QuitGame);

        gameOverRoot.SetActive(false);
    }

    void CreateGameOverButton(Transform parent, string text, Vector2 position, UnityAction action)
    {
        var img = Img("Button_" + text, parent, sRound, buttonColor, true, true);
        Place(img.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), position, new Vector2(260f, 65f));

        var button = img.gameObject.AddComponent<Button>();
        button.targetGraphic = img;
        SetButtonColors(button);
        button.onClick.AddListener(action);

        var label = Txt("Text", img.transform, text, 20f, textCream, TextAlignmentOptions.Center, false);
        Stretch(label.rectTransform, 5f);
    }

    void ShowGameOver()
    {
        if (gameOverShown) return;
        gameOverShown = true;
        gameOverRoot.SetActive(true);
    }

    void RestartGame()
    {
        stats.ResetStats();
        gameOverShown = false;
        gameOverRoot.SetActive(false);
    }

    void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    TextMeshProUGUI MakePopup(Transform parent)
    {
        var t = Txt("Popup", parent, "", 28f, gain, TextAlignmentOptions.Right, true);
        Place(t.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 0f), PopupPos, new Vector2(120f, 36f));
        var c = t.color; c.a = 0f; t.color = c;
        return t;
    }

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
