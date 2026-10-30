using UnityEngine;

// Indicador de fase i ronda (a dalt, al centre) amb compte enrere, botó "Combatre ara" durant la
// preparació, i els avisos de dany i ingressos. Només LLEGEIX d'un IRoundView:
// RoundManager (un jugador) o NetRoundView (multijugador). Els crea ells: no cal posar res a l'editor.
public class PhaseUI : MonoBehaviour
{
    static readonly Color Panel = new Color(0.08f, 0.06f, 0.04f, 0.88f);
    static readonly Color Cream = new Color(0.94f, 0.89f, 0.77f, 1f);
    static readonly Color Urgent = new Color(0.88f, 0.34f, 0.29f, 1f);
    static readonly Color Gold = new Color(1f, 0.85f, 0.25f, 1f);
    static readonly Color Red = new Color(1f, 0.35f, 0.35f, 1f);

    IRoundView round;
    PlayerStats stats;
    GUIStyle phaseStyle, popupStyle, labelStyle, buttonStyle;

    public void Init(IRoundView r, PlayerStats s)
    {
        round = r;
        stats = s;
    }

    void OnGUI()
    {
        if (round == null || stats == null) return;
        EnsureStyles();

        // ---- partida encara no començada (xarxa): esperant que tothom estigui a punt
        if (round.Waiting)
        {
            var wbox = new Rect(Screen.width * 0.5f - 180f, 8f, 360f, 40f);
            Fill(wbox, Panel);
            phaseStyle.normal.textColor = Cream;
            GUI.Label(wbox, "ESPERANT JUGADORS...", phaseStyle);
            return;
        }

        // ---- caixa central: ronda + fase + compte enrere
        string phase;
        bool urgent = false;
        switch (round.Phase)
        {
            case MatchPhase.Prep:
                int s = Mathf.CeilToInt((float)round.SecondsLeft);
                phase = "PREPARACIÓ  " + (s / 60) + ":" + (s % 60).ToString("00");
                urgent = s <= 5;
                break;
            case MatchPhase.Combat: phase = "COMBAT"; break;
            default: phase = "RESULTAT"; break;
        }

        float w = 360f, h = 40f;
        var box = new Rect(Screen.width * 0.5f - w * 0.5f, 8f, w, h);
        Fill(box, Panel);
        phaseStyle.normal.textColor = urgent ? Urgent : Cream;
        GUI.Label(box, "RONDA " + round.Round + "   ·   " + phase, phaseStyle);

        // ---- botó per començar el combat abans d'hora
        if (round.CanSkipPrep && round.Phase == MatchPhase.Prep && !stats.IsDead)
        {
            if (GUI.Button(new Rect(Screen.width * 0.5f - 90f, 54f, 180f, 30f), "Combatre ara", buttonStyle))
                round.SkipPrep();
        }

        // ---- text a la dreta de l'engranatge del PlayerHUD (segueix l'escala del canvas 1920x1080)
        float k = Mathf.Pow(2f, 0.5f * (Mathf.Log(Screen.width / 1920f, 2f) + Mathf.Log(Screen.height / 1080f, 2f)));
        float x = 96f * k + 12f;

        GUI.Label(new Rect(x, 10f, 340f, 24f),
                  "Dany si perds: " + Economy.BaseDamageForRound(round.Round) + " + supervivents enemics", labelStyle);
        int pending = Economy.InterestFor(stats.Gold);
        GUI.Label(new Rect(x, 32f, 340f, 24f),
                  "Or: " + stats.Gold + "   Interès: +" + pending + (pending >= Economy.MaxInterest ? " (màx.)" : ""), labelStyle);

        // ---- avisos
        if (Time.unscaledTime - round.IncomeShownAt < 3f)
        {
            popupStyle.normal.textColor = Gold;
            GUI.Label(new Rect(0f, 165f, Screen.width, 44f),
                "+" + (round.LastIncomeBase + round.LastIncomeInterest) + " or  (" + round.LastIncomeBase + " base + " + round.LastIncomeInterest + " interès)",
                popupStyle);
        }

        if (round.DamageShown && round.LastDamage > 0)
        {
            popupStyle.normal.textColor = Red;
            GUI.Label(new Rect(0f, 115f, Screen.width, 44f), "-" + round.LastDamage + " vida", popupStyle);
        }
    }

    void EnsureStyles()
    {
        if (phaseStyle != null) return;
        phaseStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        popupStyle = new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 14 };
        labelStyle.normal.textColor = Color.white;
        buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 14, fontStyle = FontStyle.Bold };
    }

    static void Fill(Rect r, Color c)
    {
        var old = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = old;
    }
}
