using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

// Pantalla final de la partida: guanyador i classificació. Es mostra amb MatchEndUI.Show(...)
// quan el servidor envia el final (CombatNet). Format del text: una línia per jugador "posició;nom;vides".
public class MatchEndUI : MonoBehaviour
{
    public static string MenuScene = "MainMenu";   // escena a la qual es torna en sortir

    static readonly Color PanelDark = new Color32(35, 26, 18, 245);
    static readonly Color Btn = new Color32(58, 44, 29, 255);
    static readonly Color Bronze = new Color32(184, 137, 63, 255);
    static readonly Color Cream = new Color32(239, 227, 198, 255);
    static readonly Color Tan = new Color32(168, 137, 90, 255);
    static readonly Color GoldText = new Color32(242, 210, 122, 255);
    static readonly Color Danger = new Color32(224, 88, 74, 255);

    TMP_FontAsset fLabel, fNumber;
    CanvasGroup group;
    string notice;          // != null: no és el final de partida, sinó un avís (host tancat, connexió perduda)
    static MatchEndUI current;

    public static void Show(string ranking, bool localWon, float delay = 1.5f)
    {
        if (current != null) return;
        var go = new GameObject("MatchEndUI");
        current = go.AddComponent<MatchEndUI>();
        current.StartCoroutine(current.Run(ranking, localWon, delay));
    }

    // Fase 6: avís amb botó per sortir (el host ha tancat, s'ha perdut la connexió...)
    public static void ShowNotice(string message)
    {
        if (current != null) return;   // ja hi ha una pantalla (final de partida o un altre avís)
        var go = new GameObject("MatchEndUI");
        current = go.AddComponent<MatchEndUI>();
        current.notice = string.IsNullOrEmpty(message) ? "S'ha perdut la connexió amb el host." : message;
        current.StartCoroutine(current.Run("", false, 0.2f));
    }

    IEnumerator Run(string ranking, bool won, float delay)
    {
        yield return new WaitForSecondsRealtime(delay); // deixa veure el final del combat

        var hud = FindFirstObjectByType<PlayerHUD>();
        if (hud != null) { fLabel = hud.fontLabel; fNumber = hud.fontNumber != null ? hud.fontNumber : hud.fontLabel; }

        UIKit.EnsureEventSystem();
        Build(ranking, won);

        for (float t = 0f; t < 0.5f; t += Time.unscaledDeltaTime)
        {
            group.alpha = t / 0.5f;
            yield return null;
        }
        group.alpha = 1f;
    }

    void Build(string ranking, bool won)
    {
        var rows = new List<string[]>();
        foreach (var line in (notice != null ? "" : ranking).Split('\n'))
        {
            var p = line.Split(';');
            if (p.Length == 3) rows.Add(p);
        }

        var canvas = UIKit.MakeCanvas("MatchEndCanvas", 60);
        canvas.transform.SetParent(transform, false);
        group = canvas.gameObject.AddComponent<CanvasGroup>();

        var dim = UIKit.Img("Dim", canvas.transform, null, new Color(0f, 0f, 0f, 0.75f), false, true);
        UIKit.Stretch(dim.rectTransform, 0f);

        float h = notice != null ? 340f : 250f + rows.Count * 36f;
        var panel = UIKit.Img("Panel", canvas.transform, UIKit.Round, Bronze, true, true);
        UIKit.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(680f, h));
        var inner = UIKit.Img("Inner", panel.transform, UIKit.Round, PanelDark, true);
        UIKit.Stretch(inner.rectTransform, 3f);

        var top = new Vector2(0.5f, 1f);
        string titleText = notice != null ? "PARTIDA INTERROMPUDA" : (won ? "HAS GUANYAT!" : "FI DE LA PARTIDA");
        var title = UIKit.Txt("Title", panel.transform, titleText, notice != null ? 48f : 64f,
                              won ? GoldText : Cream, TextAlignmentOptions.Center, fNumber);
        UIKit.Place(title.rectTransform, top, top, new Vector2(0f, -30f), new Vector2(640f, 84f));

        if (notice != null)
        {
            var msg = UIKit.Txt("Message", panel.transform, notice, 28f, Tan, TextAlignmentOptions.Center, fLabel);
            UIKit.Place(msg.rectTransform, top, top, new Vector2(0f, -140f), new Vector2(600f, 120f));
        }

        for (int i = 0; i < rows.Count; i++)
        {
            string place = rows[i][0], name = rows[i][1], lives = rows[i][2];
            bool first = place == "1";
            var left = UIKit.Txt("Row" + i, panel.transform, place + ". " + name, 28f,
                                 first ? GoldText : Cream, TextAlignmentOptions.Left, fLabel);
            UIKit.Place(left.rectTransform, top, new Vector2(0f, 1f), new Vector2(-280f, -130f - i * 36f), new Vector2(380f, 32f));
            var right = UIKit.Txt("Lives" + i, panel.transform, lives + " vides", 24f,
                                  lives == "0" ? Danger : Tan, TextAlignmentOptions.Right, fLabel);
            UIKit.Place(right.rectTransform, top, new Vector2(1f, 1f), new Vector2(280f, -132f - i * 36f), new Vector2(180f, 30f));
        }

        var bottom = new Vector2(0.5f, 0f);
        var btn = UIKit.Img("ExitButton", panel.transform, UIKit.Round, Btn, true, true);
        UIKit.Place(btn.rectTransform, bottom, bottom, new Vector2(0f, 32f), new Vector2(300f, 66f));
        var outline = btn.gameObject.AddComponent<UnityEngine.UI.Outline>();
        outline.effectColor = Bronze;
        outline.effectDistance = new Vector2(2f, -2f);
        var b = btn.gameObject.AddComponent<UnityEngine.UI.Button>();
        b.targetGraphic = btn;
        b.onClick.AddListener(Exit);
        var label = UIKit.Txt("Label", btn.transform, "SORTIR", 30f, Cream, TextAlignmentOptions.Center, fLabel);
        UIKit.Stretch(label.rectTransform, 0f);
    }

    void Exit()
    {
        if (group != null) group.interactable = false;   // evita dobles clics

        // Camí normal: NetBootstrap tanca la sessió (també la de Relay/Lobby) i carrega el menú
        var boot = NetBootstrap.Instance;
        if (boot != null)
        {
            current = null;
            boot.LeaveToMenu();
            return;
        }

        var nm = Unity.Netcode.NetworkManager.Singleton;
        if (nm != null && nm.IsListening) nm.Shutdown();
        current = null;
        if (Application.CanStreamedLevelBeLoaded(MenuScene)) SceneManager.LoadScene(MenuScene);
        else Destroy(gameObject);
    }

    void OnDestroy()
    {
        if (current == this) current = null;
    }
}
