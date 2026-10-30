using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

// Objecte de xarxa d'UN jugador (Player Prefab del NetworkManager). El crea MatchServer en carregar Match.
//  - Dades PÚBLIQUES (nom, vides, nivell): NetworkVariable que llegeix tothom i només escriu el servidor.
//  - Dades PRIVADES (or, botiga, banc, tauler, XP): snapshot que el servidor envia NOMÉS al propietari.
//  - Ordres: ServerRpc. El servidor mai es fia dels paràmetres: tot passa per la PlayerSession del
//    remitent (només veu les seves pròpies unitats i el seu or).
// Usa [ServerRpc]/[ClientRpc] (compatible amb Netcode for GameObjects 1.x i 2.x).
public class PlayerNet : NetworkBehaviour
{
    public const string NamePrefKey = "ht_player_name";

    public NetworkVariable<FixedString64Bytes> PlayerName = new NetworkVariable<FixedString64Bytes>();
    public NetworkVariable<int> Lives = new NetworkVariable<int>(PlayerState.StartLives);
    public NetworkVariable<int> Level = new NetworkVariable<int>(ShopConfig.StartLevel);

    // Rellotge de la partida (fase, ronda, fi de fase). El servidor l'escriu a TOTS els PlayerNet;
    // cada client llegeix el seu (així no cal registrar cap prefab ni objecte d'escena nou).
    public NetworkVariable<PhaseNet> Clock = new NetworkVariable<PhaseNet>();

    public static PlayerNet Local { get; private set; }

    PlayerSession session;   // només al servidor
    ShopManager mgr;         // només al propietari
    NetRoundView roundView;  // només al propietari
    PhaseUI phaseUI;         // només al propietari

    // ---------------------------------------------------------------- cicle de vida
    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            session = MatchServer.Instance != null ? MatchServer.Instance.Match.Get(OwnerClientId) : null;
            if (session == null)
            {
                Debug.LogError("[PlayerNet] No hi ha sessió al servidor per al client " + OwnerClientId);
            }
            else
            {
                PlayerName.Value = new FixedString64Bytes(session.Name ?? "Jugador");
                Lives.Value = session.State.Lives;
                Level.Value = session.Shop.Progress.Level;
                if (MatchServer.Instance != null) Clock.Value = MatchServer.Instance.CurrentClock; // si la partida ja corre
            }
        }

        if (IsOwner)
        {
            Local = this;
            mgr = FindFirstObjectByType<ShopManager>();
            if (mgr == null)
            {
                Debug.LogError("[PlayerNet] No hi ha ShopManager a l'escena de partida.");
            }
            else
            {
                mgr.BindNet(this);
                mgr.ApplyNetLives(Lives.Value);

                // Indicador de fase i compte enrere (llegeix el rellotge del servidor)
                var stats = mgr.GetComponent<PlayerStats>();
                if (stats != null)
                {
                    roundView = gameObject.AddComponent<NetRoundView>();
                    roundView.Init(this, mgr);
                    phaseUI = gameObject.AddComponent<PhaseUI>();
                    phaseUI.Init(roundView, stats);
                }
            }
            Lives.OnValueChanged += OnLivesChanged;

            SetNameServerRpc(PlayerPrefs.GetString(NamePrefKey, "Jugador"));
            RequestSnapshotServerRpc(); // l'estat inicial el demana el propietari quan ja està llest
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsOwner)
        {
            Lives.OnValueChanged -= OnLivesChanged;
            if (Local == this) Local = null;
            if (mgr != null) mgr.UnbindNet();
            if (phaseUI != null) Destroy(phaseUI);
            if (roundView != null) Destroy(roundView);
        }
        session = null;
    }

    void OnLivesChanged(int previous, int current)
    {
        if (mgr != null) mgr.ApplyNetLives(current);
    }

    // ---------------------------------------------------------------- ordres (client -> servidor)
    bool Valid(ServerRpcParams p)
    {
        return IsServer && session != null && p.Receive.SenderClientId == OwnerClientId;
    }

    [ServerRpc]
    void SetNameServerRpc(string playerName, ServerRpcParams p = default)
    {
        if (!Valid(p)) return;
        playerName = string.IsNullOrWhiteSpace(playerName) ? "Jugador" : playerName.Trim();
        if (playerName.Length > 20) playerName = playerName.Substring(0, 20);
        session.Name = playerName;
        PlayerName.Value = new FixedString64Bytes(playerName);
    }

    [ServerRpc]
    void RequestSnapshotServerRpc(ServerRpcParams p = default)
    {
        if (!Valid(p)) return;
        Push(null);
    }

    [ServerRpc] public void BuyServerRpc(int slot, ServerRpcParams p = default)
    { if (Valid(p)) Push(session.Buy(slot)); }

    [ServerRpc] public void RerollServerRpc(ServerRpcParams p = default)
    { if (Valid(p)) Push(session.Reroll()); }

    [ServerRpc] public void BuyXpServerRpc(ServerRpcParams p = default)
    { if (Valid(p)) Push(session.BuyXp()); }

    [ServerRpc] public void SellServerRpc(int uid, ServerRpcParams p = default)
    { if (Valid(p)) Push(session.Sell(uid)); }

    [ServerRpc] public void MoveToBoardServerRpc(int uid, int boardIndex, ServerRpcParams p = default)
    { if (Valid(p)) Push(session.MoveToBoard(uid, boardIndex)); }

    [ServerRpc] public void MoveToBenchServerRpc(int uid, int benchIndex, ServerRpcParams p = default)
    { if (Valid(p)) Push(session.MoveToBench(uid, benchIndex)); }

    // Només el host i només en Editor / Development build
    [ServerRpc] public void DebugAddGoldServerRpc(int amount, ServerRpcParams p = default)
    { if (Valid(p) && DebugOk()) { session.DebugAddGold(amount); Push(null); } }

    [ServerRpc] public void DebugAddLivesServerRpc(int amount, ServerRpcParams p = default)
    { if (Valid(p) && DebugOk()) { session.DebugAddLives(amount); Push(null); } }

    bool DebugOk() => Debug.isDebugBuild && OwnerClientId == NetworkManager.ServerClientId;

    // ---------------------------------------------------------------- servidor -> client
    // El crida MatchServer en cada canvi de fase (or dels ingressos, botiga nova...) amb el missatge pendent.
    public void ServerPush(string msg)
    {
        if (IsServer && session != null) Push(msg);
    }

    // Es crida després de CADA ordre (acceptada o no): així un moviment optimista rebutjat es corregeix.
    void Push(string msg)
    {
        Level.Value = session.Shop.Progress.Level;
        Lives.Value = session.State.Lives;

        var target = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
        };
        SnapshotClientRpc(NetSnapshot.From(session.Snapshot()), msg ?? "", target);

        if (MatchServer.Instance != null) MatchServer.Instance.LogPool(session);
    }

    [ClientRpc]
    void SnapshotClientRpc(NetSnapshot snap, string msg, ClientRpcParams p = default)
    {
        if (mgr != null) mgr.ApplyNetSnapshot(snap.ToSnapshot(), msg);
    }

    // ---------------------------------------------------------------- prova de seguretat (Editor)
    // Clic dret al component PlayerNet (en Play, al jugador local) -> aquesta opció.
    // El servidor ha de respondre "Ordre no vàlida" a tot i NO canviar or ni unitats.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [ContextMenu("TEST: enviar ordres manipulades")]
    void TestTamperedCommands()
    {
        if (!IsOwner) return;
        Debug.Log("[Test] Enviant ordres manipulades: el servidor no ha de canviar res.");
        BuyServerRpc(-1);
        BuyServerRpc(99);
        SellServerRpc(-5);
        SellServerRpc(999999);
        MoveToBoardServerRpc(999999, 5);
        MoveToBoardServerRpc(0, 9999);
        MoveToBenchServerRpc(0, -3);
    }
#endif
}
