using System;
using Unity.Netcode;
using UnityEngine;

// Client: fa de "RoundManager" a la xarxa. Només LLEGEIX el rellotge que el servidor publica al
// PlayerNet local (PhaseNet) i el converteix en el que necessita PhaseUI.
// També bloqueja els moviments a la rèplica durant el combat, perquè el client ni ho provi
// (el servidor, de totes maneres, ho torna a comprovar).
public sealed class NetRoundView : MonoBehaviour, IRoundView
{
    PlayerNet net;
    ShopManager mgr;

    public void Init(PlayerNet playerNet, ShopManager manager)
    {
        net = playerNet;
        mgr = manager;
        net.Clock.OnValueChanged += OnClock;
        Apply(net.Clock.Value);
    }

    void OnDestroy()
    {
        if (net != null) net.Clock.OnValueChanged -= OnClock;
    }

    void OnClock(PhaseNet previous, PhaseNet current) => Apply(current);

    void Apply(PhaseNet c)
    {
        if (mgr != null && mgr.Shop != null)
            mgr.Shop.CanRearrange = c.Round == 0 || (MatchPhase)c.Phase == MatchPhase.Prep;
    }

    PhaseNet Clock => net != null ? net.Clock.Value : default(PhaseNet);

    public bool Waiting => Clock.Round == 0;
    public MatchPhase Phase => (MatchPhase)Clock.Phase;
    public int Round => Clock.Round;

    public double SecondsLeft
    {
        get
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || Waiting) return 0.0;
            return Math.Max(0.0, Clock.EndTime - nm.ServerTime.Time);
        }
    }

    // La fase la mana el servidor; encara no hi ha "combatre ara" en xarxa
    public bool CanSkipPrep => false;
    public void SkipPrep() { }

    public float IncomeShownAt => -100f;
    public int LastIncomeBase => 0;
    public int LastIncomeInterest => 0;
    public bool DamageShown => false;
    public int LastDamage => 0;
}
