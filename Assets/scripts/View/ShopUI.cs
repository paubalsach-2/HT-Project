using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// Barra de la botiga a la part baixa de la pantalla: 5 targetes, reroll, comprar XP,
// nivell, probabilitats i missatges. Només llegeix l'estat i crida el ShopManager.
public class ShopUI : MonoBehaviour
{
    static readonly Color Wood = new Color32(30, 22, 15, 245);
    static readonly Color PanelDark = new Color32(35, 26, 18, 235);
    static readonly Color Btn = new Color32(58, 44, 29, 255);
    static readonly Color Bronze = new Color32(184, 137, 63, 255);
    static readonly Color Cream = new Color32(239, 227, 198, 255);
    static readonly Color Tan = new Color32(168, 137, 90, 255);
    static readonly Color GoldText = new Color32(242, 210, 122, 255);
    static readonly Color Danger = new Color32(224, 88, 74, 255);
    static readonly Color[] TierColor =
    {
        Color.clear,
        new Color32(150, 150, 160, 255), // 1
        new Color32(80, 180, 90, 255),   // 2
        new Color32(70, 130, 220, 255),  // 3
        new Color32(170, 90, 220, 255),  // 4
        new Color32(240, 180, 50, 255),  // 5
    };

    const float CenterWidth = 1180f, CardW = 180f, CardH = 180f, CardGap = 10f;
    static readonly Vector2 TL = new Vector2(0f, 1f);

    class Card
    {
        public Button button;
        public Image border, icon, coin;
        public TextMeshProUGUI nameText, costText;
        public GameObject content;
    }

    ShopManager mgr;
    PlayerStats stats;
    ShopSystem shop;
    Camera cam;
    TMP_FontAsset fLabel, fNumber;
    bool reserveCamera;
    float barHeight;

    Canvas canvas;
    Card[] cards;
    TextMeshProUGUI levelText, xpText, oddsText, toast, rerollLabel, xpLabel;
    Image toastBg;
    RectTransform xpFill;
    Coroutine toastRoutine;

    public void Init(ShopManager manager, PlayerStats playerStats, Camera camera, bool reserveCameraBottom, float height)
    {
        mgr = manager; stats = playerStats; shop = manager.Shop; cam = camera;
        reserveCamera = reserveCameraBottom; barHeight = height;

        // Mateixes fonts que el HUD (si existeix)
        var hud = FindFirstObjectByType<PlayerHUD>();
        if (hud != null) { fLabel = hud.fontLabel; fNumber = hud.fontNumber != null ? hud.fontNumber : hud.fontLabel; }

        UIKit.EnsureEventSystem();
        Build();

        shop.OnChanged += Refresh;
        shop.Progress.OnChanged += Refresh;
        stats.OnGoldChanged += OnGold;
        mgr.OnMessage += ShowToast;
        Refresh();
    }

    void OnDestroy()
    {
        if (shop != null) { shop.OnChanged -= Refresh; shop.Progress.OnChanged -= Refresh; }
        if (stats != null) stats.OnGoldChanged -= OnGold;
        if (mgr != null) mgr.OnMessage -= ShowToast;
        if (cam != null) cam.rect = new Rect(0f, 0f, 1f, 1f);
    }

    void OnGold(int v, int d) => Refresh();

    // Deixa lliure la part baixa de la pantalla perquè la barra no tapi la banqueta
    void LateUpdate()
    {
        if (!reserveCamera || cam == null || canvas == null) return;
        float h = Mathf.Clamp01(barHeight * canvas.scaleFactor / Mathf.Max(1, Screen.height));
        cam.rect = new Rect(0f, h, 1f, 1f - h);
    }

    // ---------------------------------------------------------------- construcció
    void Build()
    {
        canvas = UIKit.MakeCanvas("ShopCanvas", 5);
        canvas.transform.SetParent(transform, false);

        var bar = UIKit.Img("Bar", canvas.transform, null, Wood);
        var barRt = bar.rectTransform;
        barRt.anchorMin = new Vector2(0f, 0f);
        barRt.anchorMax = new Vector2(1f, 0f);
        barRt.pivot = new Vector2(0.5f, 0f);
        barRt.anchoredPosition = Vector2.zero;
        barRt.sizeDelta = new Vector2(0f, barHeight);

        var line = UIKit.Img("TopLine", bar.transform, null, Bronze);
        line.rectTransform.anchorMin = new Vector2(0f, 1f);
        line.rectTransform.anchorMax = new Vector2(1f, 1f);
        line.rectTransform.pivot = new Vector2(0.5f, 1f);
        line.rectTransform.anchoredPosition = Vector2.zero;
        line.rectTransform.sizeDelta = new Vector2(0f, 3f);

        var center = new GameObject("Center", typeof(RectTransform)).GetComponent<RectTransform>();
        center.SetParent(bar.transform, false);
        UIKit.Place(center, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(CenterWidth, barHeight));

        // Franja superior: nivell, XP i probabilitats
        levelText = UIKit.Txt("Level", center, "NIVELL 1", 28f, Cream, TextAlignmentOptions.Left, fNumber);
        UIKit.Place(levelText.rectTransform, TL, TL, new Vector2(0f, -8f), new Vector2(200f, 34f));

        var xpBg = UIKit.Img("XpBg", center, UIKit.Round, Btn, true);
        UIKit.Place(xpBg.rectTransform, TL, TL, new Vector2(210f, -12f), new Vector2(240f, 26f));
        var fill = UIKit.Img("XpFill", xpBg.transform, UIKit.Round, new Color32(70, 130, 220, 255), true);
        xpFill = fill.rectTransform;
        xpFill.anchorMin = Vector2.zero; xpFill.anchorMax = Vector2.one;
        xpFill.offsetMin = xpFill.offsetMax = Vector2.zero;
        xpText = UIKit.Txt("XpText", xpBg.transform, "0/2", 20f, Cream, TextAlignmentOptions.Center, fNumber);
        UIKit.Stretch(xpText.rectTransform, 0f);

        oddsText = UIKit.Txt("Odds", center, "", 24f, Cream, TextAlignmentOptions.Right, fNumber);
        UIKit.Place(oddsText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, -8f), new Vector2(640f, 34f));

        // Botons
        MakeButton(center, "Reroll", new Vector2(0f, -52f), new Vector2(200f, 86f), mgr.Reroll, out rerollLabel);
        MakeButton(center, "BuyXp", new Vector2(0f, -146f), new Vector2(200f, 86f), mgr.BuyXp, out xpLabel);

        // Targetes
        cards = new Card[ShopConfig.ShopSlots];
        for (int i = 0; i < cards.Length; i++)
            cards[i] = MakeCard(center, i, new Vector2(216f + i * (CardW + CardGap), -52f));

        // Missatges i ajuda, per sobre de la barra (amb fons fosc perquè es llegeixin sobre l'escena)
        var dim = new Color(0.08f, 0.06f, 0.04f, 0.85f);

        toastBg = UIKit.Img("ToastBg", bar.transform, UIKit.Round, dim, true);
        UIKit.Place(toastBg.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0f), new Vector2(0f, 62f), new Vector2(880f, 54f));
        toast = UIKit.Txt("Toast", toastBg.transform, "", 28f, Cream, TextAlignmentOptions.Center, fLabel);
        UIKit.Stretch(toast.rectTransform, 8f);
        toast.enableAutoSizing = true;
        toast.fontSizeMin = 16f;
        toast.fontSizeMax = 28f;
        SetToastAlpha(0f);

        var hintBg = UIKit.Img("HintBg", bar.transform, UIKit.Round, dim, true);
        UIKit.Place(hintBg.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(380f, 8f), new Vector2(780f, 38f));
        var hint = UIKit.Txt("Hint", hintBg.transform, "Clic dret / E: vendre   |   D: reroll   |   F: comprar XP",
                             22f, Cream, TextAlignmentOptions.Center, fNumber);
        UIKit.Stretch(hint.rectTransform, 0f);
    }

    Image MakeButton(Transform parent, string name, Vector2 pos, Vector2 size, UnityAction onClick, out TextMeshProUGUI label)
    {
        var img = UIKit.Img(name, parent, UIKit.Round, Btn, true, true);
        UIKit.Place(img.rectTransform, TL, TL, pos, size);
        var outline = img.gameObject.AddComponent<Outline>();
        outline.effectColor = Bronze;
        outline.effectDistance = new Vector2(2f, -2f);

        var b = img.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        var cb = b.colors;
        cb.normalColor = new Color(0.85f, 0.85f, 0.85f, 1f);
        cb.highlightedColor = Color.white;
        cb.pressedColor = new Color(0.6f, 0.6f, 0.6f, 1f);
        b.colors = cb;
        b.onClick.AddListener(onClick);

        label = UIKit.Txt("Label", img.transform, "", 26f, Cream, TextAlignmentOptions.Center, fLabel);
        UIKit.Stretch(label.rectTransform, 0f);
        return img;
    }

    Card MakeCard(Transform parent, int index, Vector2 pos)
    {
        var c = new Card();
        c.border = UIKit.Img("Card" + index, parent, UIKit.Round, TierColor[1], true, true);
        UIKit.Place(c.border.rectTransform, TL, TL, pos, new Vector2(CardW, CardH));
        var inner = UIKit.Img("Inner", c.border.transform, UIKit.Round, PanelDark, true);
        UIKit.Stretch(inner.rectTransform, 3f);

        c.button = c.border.gameObject.AddComponent<Button>();
        c.button.targetGraphic = c.border;
        var cb = c.button.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(1f, 1f, 1f, 1f);
        cb.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
        cb.disabledColor = new Color(1f, 1f, 1f, 0.25f);
        c.button.colors = cb;
        int slot = index;
        c.button.onClick.AddListener(() => mgr.Buy(slot));

        c.content = new GameObject("Content", typeof(RectTransform));
        c.content.transform.SetParent(c.border.transform, false);
        UIKit.Stretch((RectTransform)c.content.transform, 0f);

        c.icon = UIKit.Img("Icon", c.content.transform, null, Color.white);
        UIKit.Place(c.icon.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -12f), new Vector2(92f, 92f));

        c.nameText = UIKit.Txt("Name", c.content.transform, "", 24f, Cream, TextAlignmentOptions.Center, fNumber);
        UIKit.Place(c.nameText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -106f), new Vector2(CardW - 8f, 30f));
        c.nameText.enableAutoSizing = true;
        c.nameText.fontSizeMin = 16f;
        c.nameText.fontSizeMax = 24f;

        c.coin = UIKit.Img("Coin", c.content.transform, UIKit.Circle, new Color32(224, 178, 65, 255));
        UIKit.Place(c.coin.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-18f, 12f), new Vector2(26f, 26f));

        c.costText = UIKit.Txt("Cost", c.content.transform, "", 28f, GoldText, TextAlignmentOptions.Left, fNumber);
        UIKit.Place(c.costText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 0f), new Vector2(-2f, 8f), new Vector2(60f, 34f));
        return c;
    }

    // ---------------------------------------------------------------- refresc
    void Refresh()
    {
        if (cards == null) return;
        int gold = stats.Gold;

        for (int i = 0; i < cards.Length; i++)
        {
            var c = cards[i];
            var d = shop.GetSlot(i);
            c.content.SetActive(d != null);
            c.button.interactable = d != null;
            if (d == null) { c.border.color = new Color(1f, 1f, 1f, 0.12f); continue; }

            c.border.color = TierColor[d.Tier];
            c.icon.sprite = UnitShapes.Icon(d.Shape);
            c.icon.color = UnitShapes.ColorOf(d);
            c.nameText.text = d.Name;
            c.costText.text = d.Cost.ToString();
            c.costText.color = gold >= d.Cost ? GoldText : Danger;
        }

        var p = shop.Progress;
        levelText.text = "NIVELL " + p.Level;
        xpFill.anchorMax = new Vector2(p.IsMax ? 1f : (float)p.Xp / Mathf.Max(1, p.XpToNext), 1f);
        xpText.text = p.IsMax ? "MÀX" : p.Xp + "/" + p.XpToNext;

        var o = ShopConfig.Odds(p.Level);
        var sb = new StringBuilder("<color=#A8895A>PROBABILITATS</color>  ");
        for (int t = 1; t <= ShopConfig.MaxTier; t++)
            if (o[t - 1] > 0)
                sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(TierColor[t])).Append('>')
                  .Append(o[t - 1]).Append("%</color>  ");
        oddsText.text = sb.ToString();

        rerollLabel.text = "REROLL <size=70%>[D]</size>\n<color=#" + CostHex(gold, ShopConfig.RerollCost) + ">"
                         + ShopConfig.RerollCost + " or</color>";
        xpLabel.text = "COMPRAR XP <size=70%>[F]</size>\n<color=#" + CostHex(gold, ShopConfig.BuyXpCost) + ">"
                     + ShopConfig.BuyXpCost + " or  +" + ShopConfig.BuyXpAmount + " XP</color>";
    }

    static string CostHex(int gold, int cost) =>
        ColorUtility.ToHtmlStringRGB(gold >= cost ? GoldText : Danger);

    // ---------------------------------------------------------------- missatges
    void ShowToast(string msg)
    {
        if (toastRoutine != null) StopCoroutine(toastRoutine);
        toastRoutine = StartCoroutine(ToastRoutine(msg));
    }

    IEnumerator ToastRoutine(string msg)
    {
        toast.text = msg;
        SetToastAlpha(1f);
        yield return new WaitForSecondsRealtime(1.1f);
        for (float t = 0f; t < 0.4f; t += Time.unscaledDeltaTime)
        {
            SetToastAlpha(1f - t / 0.4f);
            yield return null;
        }
        SetToastAlpha(0f);
        toastRoutine = null;
    }

    void SetToastAlpha(float a)
    {
        var c = toast.color; c.a = a; toast.color = c;
        var b = toastBg.color; b.a = 0.85f * a; toastBg.color = b;
    }
}