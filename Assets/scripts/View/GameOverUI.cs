using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Pantalla de derrota: surt quan les vides arriben a 0 i permet tornar a jugar.
[RequireComponent(typeof(PlayerStats))]
public class GameOverUI : MonoBehaviour
{
    [Header("Tipografia (si és buit, les del PlayerHUD)")]
    public TMP_FontAsset fontLabel;
    public TMP_FontAsset fontNumber;

    static readonly Color PanelDark = new Color32(35, 26, 18, 245);
    static readonly Color Btn = new Color32(58, 44, 29, 255);
    static readonly Color Bronze = new Color32(184, 137, 63, 255);
    static readonly Color Cream = new Color32(239, 227, 198, 255);
    static readonly Color Tan = new Color32(168, 137, 90, 255);
    static readonly Color Danger = new Color32(224, 88, 74, 255);

    PlayerStats stats;
    GameObject root;
    CanvasGroup group;

    void Start()
    {
        stats = GetComponent<PlayerStats>();
        if (fontLabel == null || fontNumber == null)
        {
            var hud = FindFirstObjectByType<PlayerHUD>();
            if (hud != null)
            {
                if (fontLabel == null) fontLabel = hud.fontLabel;
                if (fontNumber == null) fontNumber = hud.fontNumber != null ? hud.fontNumber : hud.fontLabel;
            }
        }

        stats.OnLivesChanged += OnLives;
        if (stats.IsDead) Show();
    }

    void OnDestroy()
    {
        if (stats != null) stats.OnLivesChanged -= OnLives;
    }

    void OnLives(int lives, int delta)
    {
        if (lives <= 0) Show();
    }

    void Show()
    {
        if (root != null) return;
        UIKit.EnsureEventSystem();

        var canvas = UIKit.MakeCanvas("GameOverCanvas", 50);
        canvas.transform.SetParent(transform, false);
        root = canvas.gameObject;
        group = root.AddComponent<CanvasGroup>();

        // Fons fosc que també bloqueja els clics a la botiga i al HUD
        var dim = UIKit.Img("Dim", canvas.transform, null, new Color(0f, 0f, 0f, 0.72f), false, true);
        UIKit.Stretch(dim.rectTransform, 0f);

        var panel = UIKit.Img("Panel", canvas.transform, UIKit.Round, Bronze, true, true);
        UIKit.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(640f, 340f));
        var inner = UIKit.Img("Inner", panel.transform, UIKit.Round, PanelDark, true);
        UIKit.Stretch(inner.rectTransform, 3f);

        var top = new Vector2(0.5f, 1f);
        var title = UIKit.Txt("Title", panel.transform, "HAS PERDUT", 76f, Danger, TextAlignmentOptions.Center, fontNumber);
        UIKit.Place(title.rectTransform, top, top, new Vector2(0f, -44f), new Vector2(600f, 96f));

        var sub = UIKit.Txt("Subtitle", panel.transform, "T'has quedat sense vides", 28f, Tan, TextAlignmentOptions.Center, fontLabel);
        UIKit.Place(sub.rectTransform, top, top, new Vector2(0f, -148f), new Vector2(600f, 40f));

        var bottom = new Vector2(0.5f, 0f);
        var btn = UIKit.Img("RestartButton", panel.transform, UIKit.Round, Btn, true, true);
        UIKit.Place(btn.rectTransform, bottom, bottom, new Vector2(0f, 40f), new Vector2(340f, 72f));
        var outline = btn.gameObject.AddComponent<Outline>();
        outline.effectColor = Bronze;
        outline.effectDistance = new Vector2(2f, -2f);

        var b = btn.gameObject.AddComponent<Button>();
        b.targetGraphic = btn;
        var cb = b.colors;
        cb.normalColor = new Color(0.85f, 0.85f, 0.85f, 1f);
        cb.highlightedColor = Color.white;
        cb.pressedColor = new Color(0.6f, 0.6f, 0.6f, 1f);
        b.colors = cb;
        b.onClick.AddListener(Restart);

        var label = UIKit.Txt("Label", btn.transform, ShopManager.Networked ? "SORTIR AL MENÚ" : "TORNAR A JUGAR", 30f, Cream, TextAlignmentOptions.Center, fontLabel);
        UIKit.Stretch(label.rectTransform, 0f);

        StartCoroutine(FadeIn());
    }

    IEnumerator FadeIn()
    {
        group.alpha = 0f;
        for (float t = 0f; t < 0.5f; t += Time.unscaledDeltaTime)
        {
            group.alpha = t / 0.5f;
            yield return null;
        }
        group.alpha = 1f;
    }

    void Restart()
    {
        if (ShopManager.Networked) { ShopManager.LeaveMatch(); return; } // en xarxa: sortir de la partida
        var sc = SceneManager.GetActiveScene();
#if UNITY_EDITOR
        // Si l'escena no és a Build Settings, es carrega pel camí (només a l'editor)
        if (sc.buildIndex < 0)
        {
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(
                sc.path, new LoadSceneParameters(LoadSceneMode.Single));
            return;
        }
#endif
        SceneManager.LoadScene(sc.buildIndex);
    }
}
