using System;
using UnityEngine;

// Director de la partida (un jugador): governa el cicle Prep -> Combat -> Resolve amb un
// PhaseTimer (Core), aplica l'economia amb Economy (Core) i el dany de vida en perdre.
//   Prep     : comprar i col·locar (compte enrere). En acabar, comença el combat sol.
//   Combat   : CombatManager reprodueix la simulació; en acabar es decideix el resultat.
//   Resolve  : pausa curta per veure el resultat; en acabar, ronda següent (+ingressos, botiga nova).
[RequireComponent(typeof(CombatManager))]
public class RoundManager : MonoBehaviour, IRoundView
{
    [Header("Fases")]
    public float prepSeconds = 30f;           // temps de preparació per ronda
    public float resolveSeconds = 2f;         // pausa després del combat
    public bool newShopEachRound = true;      // refresca la botiga en començar cada ronda
    public bool showSkipButton = true;        // botó "Combatre ara" durant la preparació

    public PhaseTimer Timer { get; } = new PhaseTimer();
    public int Round => Timer.Round;
    public MatchPhase Phase => Timer.Phase;

    // IRoundView (el que llegeix PhaseUI)
    public bool Waiting => false;
    public double SecondsLeft => Timer.SecondsLeft;
    public bool CanSkipPrep => showSkipButton;
    public void SkipPrep() => StartCombatNow();

    public int LastDamage { get; private set; }
    public bool DamageShown { get; private set; }
    public int LastIncomeBase { get; private set; }
    public int LastIncomeInterest { get; private set; }
    public float IncomeShownAt { get; private set; } = -100f;

    public event Action<int> OnRoundChanged;               // nova ronda
    public event Action<int> OnDamageTaken;                // vides perdudes en una derrota
    public event Action<int, int> OnIncome;                // (ingrés base, interès) en començar la ronda
    public event Action<MatchPhase, int> OnPhaseChanged;   // (fase, ronda)

    PlayerStats stats;
    ShopManager shop;
    CombatManager combat;
    float acc;
    bool halted;      // derrota total: el rellotge s'atura
    bool wasDead;

    void Awake()
    {
        // En xarxa el cicle de rondes el du el servidor (MatchServer + MatchState); el client el veu amb NetRoundView.
        if (ShopManager.Networked) { enabled = false; return; }

        stats = GetComponent<PlayerStats>();
        shop = GetComponent<ShopManager>();
        combat = GetComponent<CombatManager>();

        Timer.PrepTicks = Mathf.Max(1, Mathf.RoundToInt(prepSeconds * SimConfig.TicksPerSecond));
        Timer.ResolveTicks = Mathf.Max(1, Mathf.RoundToInt(resolveSeconds * SimConfig.TicksPerSecond));
        Timer.OnPhaseChanged += HandlePhase;
    }

    void OnEnable()
    {
        if (combat != null) combat.OnCombatEnded += HandleCombatEnded;
        if (stats != null) stats.OnLivesChanged += HandleLivesChanged;
    }

    void OnDisable()
    {
        if (combat != null) combat.OnCombatEnded -= HandleCombatEnded;
        if (stats != null) stats.OnLivesChanged -= HandleLivesChanged;
    }

    void OnDestroy()
    {
        Timer.OnPhaseChanged -= HandlePhase;
    }

    void Start()
    {
        Timer.Start();
        gameObject.AddComponent<PhaseUI>().Init(this, stats);
    }

    void Update()
    {
        if (halted) return;
        acc += Mathf.Min(Time.deltaTime, 0.25f);
        while (acc >= 1f / SimConfig.TicksPerSecond)
        {
            acc -= 1f / SimConfig.TicksPerSecond;
            Timer.Tick();
        }
    }

    // El jugador acaba la preparació abans d'hora
    public void StartCombatNow() => Timer.SkipPrep();

    // ---------------------------------------------------------------- economia i dany (Core/Economy)
    public int BaseDamageForRound(int round) => Economy.BaseDamageForRound(round);
    public int DamageFor(int round, int enemySurvivors) => Economy.DamageFor(round, enemySurvivors);
    public int InterestFor(int gold) => Economy.InterestFor(gold);
    public int MaxInterest => Economy.MaxInterest;

    // ---------------------------------------------------------------- fases
    void HandlePhase(MatchPhase phase, int round)
    {
        switch (phase)
        {
            case MatchPhase.Prep:
                combat.ClearField();       // si venim d'un combat, tornem les unitats reals
                DamageShown = false;
                if (stats.IsDead)          // derrota total: no hi ha ronda següent
                {
                    halted = true;
                    shop.Shop.CanRearrange = false;
                    break;
                }
                shop.Shop.CanRearrange = true;
                if (round > 1)
                {
                    int interest = Economy.InterestFor(stats.Gold);
                    int total = Economy.GoldPerRound + interest;
                    stats.AddGold(total);
                    LastIncomeBase = Economy.GoldPerRound;
                    LastIncomeInterest = interest;
                    IncomeShownAt = Time.unscaledTime;
                    OnIncome?.Invoke(LastIncomeBase, LastIncomeInterest);

                    if (newShopEachRound) shop.Shop.NewRound();
                    OnRoundChanged?.Invoke(round);
                }
                break;

            case MatchPhase.Combat:
                shop.Shop.CanRearrange = false;
                if (!combat.StartCombat(round)) Timer.EndCombat(); // si no es pot lluitar, saltem la fase
                break;

            case MatchPhase.Resolve:
                shop.Shop.CanRearrange = false;
                break;
        }
        OnPhaseChanged?.Invoke(phase, round);
    }

    void HandleCombatEnded(CombatResult result)
    {
        LastDamage = 0;
        DamageShown = false;

        // només es perd vida si perds (un empat no fa mal)
        if (result.Outcome == CombatOutcome.EnemyWon)
        {
            int dmg = Economy.DamageFor(Round, result.EnemySurvivors);
            dmg = Mathf.Min(dmg, stats.Lives);   // les vides no baixen de 0
            if (dmg > 0)
            {
                stats.AddLives(-dmg);
                LastDamage = dmg;
                DamageShown = true;
                OnDamageTaken?.Invoke(dmg);
            }
        }
        Timer.EndCombat();
    }

    // Si després de morir es tornen a tenir vides (reinici de partida), tornem a la ronda 1
    void HandleLivesChanged(int lives, int delta)
    {
        if (stats.IsDead) { wasDead = true; return; }
        if (!wasDead) return;

        wasDead = false;
        halted = false;
        LastDamage = 0;
        DamageShown = false;
        combat.ClearField();
        Timer.Start();
        OnRoundChanged?.Invoke(Round);
    }
}
