using System;
using UnityEngine;

// Compta les rondes i aplica el dany de vida quan perds un combat.
// Dany en perdre = dany base de la ronda + supervivents enemics * damagePerSurvivor.
// De moment el combat es continua llançant amb el botó "Provar combat" de CombatManager;
// quan s'acaba un combat, la ronda avança sola.
[RequireComponent(typeof(CombatManager))]
public class RoundManager : MonoBehaviour
{
    [Header("Dany en perdre un combat")]
    public int baseDamage = 2;                 // dany base a la ronda 1
    public float extraDamagePerRound = 0.5f;   // quant creix el dany base cada ronda (0.5 = +1 cada 2 rondes)
    public int damagePerSurvivor = 1;          // dany extra per cada unitat enemiga que sobrevisqui

    [Header("Entre rondes")]
    public bool newShopEachRound = true;      // refresca la botiga en començar cada ronda

    [Header("Economia")]
    public int goldPerRound = 5;              // ingrés base en acabar cada ronda
    public int interestStep = 10;             // cada 'interestStep' d'or guardat...
    public int interestPerStep = 1;           // ...donen aquest or extra d'interès
    public int maxInterest = 5;               // límit de l'interès (amb 50 d'or)

    [Header("Interfície")]
    public bool showRoundGUI = true;

    public int Round { get; private set; } = 1;
    public int LastDamage { get; private set; }

    public event Action<int> OnRoundChanged;   // nova ronda
    public event Action<int> OnDamageTaken;    // vides perdudes en una derrota
    public event Action<int, int> OnIncome;    // (ingrés base, interès) en acabar la ronda

    PlayerStats stats;
    ShopManager shop;
    CombatManager combat;
    bool showDamage;
    int lastBase, lastInterest;
    float incomeShownAt = -100f;
    bool wasDead;
    GUIStyle roundStyle, damageStyle;

    void Awake()
    {
        stats = GetComponent<PlayerStats>();
        shop = GetComponent<ShopManager>();
        combat = GetComponent<CombatManager>();
    }

    void OnEnable()
    {
        if (combat != null)
        {
            combat.OnCombatEnded += HandleCombatEnded;
            combat.OnCombatCleared += HandleCombatCleared;
        }
        if (stats != null) stats.OnLivesChanged += HandleLivesChanged;
    }

    void OnDisable()
    {
        if (combat != null)
        {
            combat.OnCombatEnded -= HandleCombatEnded;
            combat.OnCombatCleared -= HandleCombatCleared;
        }
        if (stats != null) stats.OnLivesChanged -= HandleLivesChanged;
    }

    // ---------------------------------------------------------------- dany
    public int BaseDamageForRound(int round)
    {
        return baseDamage + Mathf.FloorToInt(extraDamagePerRound * Mathf.Max(0, round - 1));
    }

    public int DamageFor(int round, int enemySurvivors)
    {
        return Mathf.Max(0, BaseDamageForRound(round) + enemySurvivors * damagePerSurvivor);
    }

    // ---------------------------------------------------------------- economia
    // Interès de l'or guardat: +interestPerStep per cada interestStep, amb límit maxInterest
    public int InterestFor(int gold)
    {
        if (interestStep <= 0) return 0;
        return Mathf.Clamp((gold / interestStep) * interestPerStep, 0, maxInterest);
    }

    // ---------------------------------------------------------------- esdeveniments
    void HandleCombatEnded(CombatResult result)
    {
        LastDamage = 0;
        showDamage = false;

        // només es perd vida si perds (un empat no fa mal)
        if (result.Outcome != CombatOutcome.EnemyWon) return;

        int dmg = DamageFor(Round, result.EnemySurvivors);
        dmg = Mathf.Min(dmg, stats.Lives);   // les vides no baixen de 0
        if (dmg <= 0) return;

        stats.AddLives(-dmg);
        LastDamage = dmg;
        showDamage = true;
        OnDamageTaken?.Invoke(dmg);
    }

    void HandleCombatCleared()
    {
        showDamage = false;
        if (stats.IsDead) return;   // derrota total: no hi ha ronda següent

        Round++;

        int interest = InterestFor(stats.Gold);
        int total = goldPerRound + interest;
        if (total > 0) stats.AddGold(total);
        lastBase = goldPerRound;
        lastInterest = interest;
        incomeShownAt = Time.unscaledTime;
        OnIncome?.Invoke(goldPerRound, interest);

        if (newShopEachRound) shop.Shop.NewRound();
        OnRoundChanged?.Invoke(Round);
    }

    // Si després de morir es tornen a tenir vides (reinici de partida), tornem a la ronda 1
    void HandleLivesChanged(int lives, int delta)
    {
        if (stats.IsDead) { wasDead = true; return; }
        if (wasDead)
        {
            wasDead = false;
            Round = 1;
            LastDamage = 0;
            showDamage = false;
            OnRoundChanged?.Invoke(Round);
        }
    }

    // ---------------------------------------------------------------- interfície
    void OnGUI()
    {
        if (!showRoundGUI) return;

        if (roundStyle == null)
        {
            roundStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };
            roundStyle.normal.textColor = Color.white;
            damageStyle = new GUIStyle(GUI.skin.label) { fontSize = 28, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            damageStyle.normal.textColor = new Color(1f, 0.35f, 0.35f);
        }

        GUI.Label(new Rect(12f, 8f, 320f, 30f), "Ronda " + Round, roundStyle);
        GUI.Label(new Rect(12f, 32f, 320f, 24f), "Dany si perds: " + BaseDamageForRound(Round) + " + supervivents enemics");

        int pending = InterestFor(stats.Gold);
        GUI.Label(new Rect(12f, 54f, 320f, 24f), "Or: " + stats.Gold + "   Interès: +" + pending + (pending >= maxInterest ? " (màx.)" : ""));

        if (Time.unscaledTime - incomeShownAt < 3f)
        {
            damageStyle.normal.textColor = new Color(1f, 0.85f, 0.25f);
            GUI.Label(new Rect(0f, 165f, Screen.width, 44f),
                "+" + (lastBase + lastInterest) + " or  (" + lastBase + " base + " + lastInterest + " interès)", damageStyle);
            damageStyle.normal.textColor = new Color(1f, 0.35f, 0.35f);
        }

        if (showDamage && LastDamage > 0)
            GUI.Label(new Rect(0f, 115f, Screen.width, 44f), "-" + LastDamage + " vida", damageStyle);
    }
}
