using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;

// Punt d'entrada del jugador LOCAL: crea la botiga, el banc 3D i la UI.
// Ús: posar-lo al mateix GameObject que PlayerStats (i PlayerHUD).
// Tot el que fa el jugador passa per Commands (IPlayerCommands):
//  - Offline: LocalCommands i un ShopSystem real amb el PlayerStats com a moneder.
//  - En xarxa: NetCommands i un ShopSystem RÈPLICA, que només s'actualitza amb els snapshots del
//    servidor (l'or i les vides de PlayerStats són un mirall del que diu el servidor).
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
    public IPlayerCommands Commands { get; private set; }
    public event Action<string> OnMessage;
    public BenchView BoardView => benchView;   // la fa servir CombatManager
    public bool LockInput;                     // CombatManager el posa a true durant el combat

    // true si estem dins d'una sessió de xarxa (host o client)
    public static bool Networked => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
    // Debug (or/vides a mà): offline sempre; en xarxa només el host i només en Editor / Development build
    public static bool DebugAllowed => !Networked || (Debug.isDebugBuild && NetworkManager.Singleton.IsHost);
    public bool IsNetworked { get; private set; }

    NetCommands netCommands;

    PlayerStats stats;
    BenchView benchView;

    void Awake()
    {
        stats = GetComponent<PlayerStats>();
        IsNetworked = Networked;
        Shop = new ShopSystem(new PlayerWallet(stats), seed); // en xarxa és una rèplica: no es fa servir el seu pool

        if (IsNetworked)
        {
            netCommands = new NetCommands(Shop);
            Commands = netCommands;
        }
        else
        {
            Commands = new LocalCommands(Shop, stats.AddGold, stats.AddLives);
        }
        Commands.OnMessage += Say;
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
            benchView.Init(Shop, Commands, root, cam);
            benchView.OnMessage += Say;
        }

        if (GetComponent<GameOverUI>() == null)
            gameObject.AddComponent<GameOverUI>();     // pantalla de derrota
        if (GetComponent<CombatManager>() == null)
            gameObject.AddComponent<CombatManager>();  // combat
        if (GetComponent<UnitInspector>() == null)
            gameObject.AddComponent<UnitInspector>();  // aura + panell d'informació

        new GameObject("ShopUI").AddComponent<ShopUI>()
            .Init(this, stats, cam, reserveCameraBottom, shopBarHeight);

        if (!IsNetworked) Shop.NewRound(); // en xarxa, la botiga la reparteix el servidor
    }

    void OnDestroy()
    {
        if (Commands != null) Commands.OnMessage -= Say;
    }

    void Update()
    {
        if (benchView != null) benchView.Locked = stats.IsDead || LockInput;
        if (stats.IsDead) return; // derrota: la botiga queda bloquejada
        if (InputUtil.Pressed(Hotkey.Reroll)) Reroll();
        if (InputUtil.Pressed(Hotkey.BuyXp)) BuyXp();

        if (benchView != null && InputUtil.Pressed(Hotkey.Sell)) // el clic dret ara obre el panell d'informació
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
            Sell(benchView.UnderMouse());
        }
    }

    // ---- accions (les usen la UI i les dreceres): només reenvien l'ordre
    public void Reroll() => Commands.Reroll();
    public void BuyXp() => Commands.BuyXp();
    public void Buy(int slot)
    {
        var d = Shop.GetSlot(slot);
        Debug.Log("[Buy] unitat=" + (d != null ? d.Name : "-") + " cost=" + (d != null ? d.Cost : -1)
            + " or=" + stats.Gold + " statsId=" + stats.GetInstanceID()
            + " totalPlayerStats=" + FindObjectsByType<PlayerStats>(FindObjectsSortMode.None).Length);
        Commands.Buy(slot);
    }

    public void Sell(UnitInstance u)
    {
        if (u == null || !Shop.Bench.Contains(u)) return; // només es venen unitats reals (no còpies de combat)
        Commands.Sell(u.Uid);
    }

    void Say(string msg) => OnMessage?.Invoke(msg);

    // ---- xarxa (els crida PlayerNet del jugador local)
    public void BindNet(PlayerNet net)
    {
        if (netCommands != null) netCommands.Bind(net);
    }

    public void UnbindNet()
    {
        if (netCommands != null) netCommands.Bind(null);
    }

    // Estat privat enviat pel servidor. L'or primer: la UI el llegeix quan la botiga avisa que ha canviat.
    public void ApplyNetSnapshot(PlayerSnapshot snap, string msg)
    {
        stats.SetGold(snap.Gold);
        Shop.ApplySnapshot(snap);
        if (!string.IsNullOrEmpty(msg)) Say(msg);
    }

    public void ApplyNetLives(int lives) => stats.SetLives(lives);

    // Surt de la partida de xarxa i torna al menú (tanca també la sala de Relay)
    public static void LeaveMatch()
    {
        if (NetBootstrap.Instance != null) NetBootstrap.Instance.LeaveToMenu();
    }

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