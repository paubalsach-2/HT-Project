using System;
using UnityEngine;
using UnityEngine.EventSystems;

// Punt d'entrada del mode un jugador: crea la botiga, el banc 3D i la UI.
// Ús: posar-lo al mateix GameObject que PlayerStats (i PlayerHUD).
[RequireComponent(typeof(PlayerStats))]
public class ShopManager : MonoBehaviour
{
    [Header("Escena")]
    public Camera cam;                      // si és buida, Camera.main
    public Transform benchRootOverride;     // si és buit, es busca "PlayerBench" / "EnemyBench"

    [Header("Opcions")]
    public bool reserveCameraBottom = true; // deixa lliure la part baixa de la pantalla per a la botiga
    public float shopBarHeight = 240f;      // alçada de la barra (canvas de referència 1920x1080)
    public int seed = -1;                   // >= 0 per a sorteigs repetibles

    public ShopSystem Shop { get; private set; }
    public event Action<string> OnMessage;
    public BenchView BoardView => benchView;   // la fa servir CombatManager
    public bool LockInput;                     // CombatManager el posa a true durant el combat

    PlayerStats stats;
    BenchView benchView;

    void Awake()
    {
        stats = GetComponent<PlayerStats>();
        Shop = new ShopSystem(new PlayerWallet(stats), seed);
    }

    void Start()
    {
        if (cam == null) cam = Camera.main;

        var root = benchRootOverride != null ? benchRootOverride : FindPlayerBench();
        if (root == null || cam == null)
        {
            Debug.LogError("[ShopManager] No s'ha trobat el banc (PlayerBench) o la càmera.");
        }
        else
        {
            benchView = new GameObject("BenchView").AddComponent<BenchView>();
            benchView.Init(Shop.Bench, root, cam);
            benchView.OnMessage += Say;
        }

        if (GetComponent<GameOverUI>() == null) gameObject.AddComponent<GameOverUI>(); // pantalla de derrota
        if (GetComponent<CombatManager>() == null) gameObject.AddComponent<CombatManager>(); // combat
        new GameObject("ShopUI").AddComponent<ShopUI>().Init(this, stats, cam, reserveCameraBottom, shopBarHeight);
        Shop.NewRound(); // primera botiga gratuïta
    }

    void Update()
    {
        if (benchView != null) benchView.Locked = stats.IsDead || LockInput;
        if (stats.IsDead) return; // derrota: la botiga queda bloquejada
        if (InputUtil.Pressed(Hotkey.Reroll)) Reroll();
        if (InputUtil.Pressed(Hotkey.BuyXp)) BuyXp();

        if (benchView != null && (InputUtil.RightClickDown || InputUtil.Pressed(Hotkey.Sell)))
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
            Sell(benchView.UnderMouse());
        }
    }

    // ---- accions (les usen la UI i les dreceres)
    public void Reroll() => Report(Shop.Reroll());
    public void BuyXp() => Report(Shop.BuyXp());
    public void Buy(int slot) => Report(Shop.Buy(slot));

    public void Sell(UnitInstance u)
    {
        if (u == null) return;
        int gold = Shop.Sell(u);
        if (gold > 0) Say("Venut: +" + gold + " or");
    }

    void Report(ShopResult r)
    {
        switch (r)
        {
            case ShopResult.NotEnoughGold: Say("No tens prou or"); break;
            case ShopResult.BenchFull: Say("El banc és ple"); break;
            case ShopResult.MaxLevel: Say("Ja ets al nivell màxim"); break;
        }
    }

    void Say(string msg) => OnMessage?.Invoke(msg);

    // La banqueta del jugador és la més propera a la càmera (la de baix a la pantalla)
    Transform FindPlayerBench()
    {
        var a = GameObject.Find("PlayerBench");
        var b = GameObject.Find("EnemyBench");
        if (a == null) return b != null ? b.transform : null;
        if (b == null || cam == null) return a.transform;
        float da = (Center(a.transform) - cam.transform.position).sqrMagnitude;
        float db = (Center(b.transform) - cam.transform.position).sqrMagnitude;
        return da <= db ? a.transform : b.transform;
    }

    static Vector3 Center(Transform t)
    {
        var rs = t.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return t.position;
        Bounds bounds = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) bounds.Encapsulate(rs[i].bounds);
        return bounds.center;
    }
}