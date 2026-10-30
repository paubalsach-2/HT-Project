using System;
using System.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

// Connexio directa (fase 2a): el host crea la sala i els clients s'hi connecten per IP.
// Va a l'escena MainMenu, en un GameObject ARREL amb NetworkManager + UnityTransport.
// Sobreviu als canvis d'escena; el host carrega l'escena de partida per a tothom amb
// NetworkManager.SceneManager.
// Els scripts del joc (ShopManager, RoundManager...) no en depenen: l'escena Match
// continua funcionant sola si la proves directament.
[RequireComponent(typeof(NetworkManager))]
[RequireComponent(typeof(UnityTransport))]
public class NetBootstrap : MonoBehaviour
{
    public const int MaxPlayers = 8;

    [Header("Escenes (han d'estar a Build Profiles > Scene List)")]
    public string menuSceneName = "MainMenu";
    public string matchSceneName = "Match";

    [Header("Xarxa")]
    public ushort port = 7777;

    [Header("Proves (només Editor / Development build)")]
    [Range(0, MaxPlayers)] public int fillWithBotsTo = 0;   // 0 = sense bots; p. ex. 8 = omple fins a 8 jugadors

    public static NetBootstrap Instance { get; private set; }

    public string LastStatus { get; private set; } = "";
    public bool MatchStarted { get; private set; }

    public event Action<string> OnStatus;

    NetworkManager nm;
    UnityTransport transport;
    bool hooked;

    public bool IsActive => nm != null && (nm.IsClient || nm.IsServer);
    public bool IsHost => nm != null && nm.IsHost;
    public bool IsConnected => nm != null && nm.IsConnectedClient;
    public int PlayerCount => nm != null && nm.IsServer ? nm.ConnectedClientsIds.Count : 0;

    // ---------------------------------------------------------------- cicle de vida
    void Awake()
    {
        // Si tornem al menu, l'escena porta un segon NetBootstrap: es descarta
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        nm = GetComponent<NetworkManager>();
        transport = GetComponent<UnityTransport>();
        DontDestroyOnLoad(gameObject);
        LobbyService.SessionEnded += OnLobbySessionEnded;

        if (GetComponent<NetStatusOverlay>() == null) gameObject.AddComponent<NetStatusOverlay>();
    }

    void OnDestroy()
    {
        if (Instance == this) LobbyService.SessionEnded -= OnLobbySessionEnded;
        Unhook();
        if (Instance == this) Instance = null;
    }

    // ---------------------------------------------------------------- crear / unir-se
    public bool StartHost()
    {
        if (IsActive) { Status("Ja hi ha una sessio activa."); return false; }

        transport.SetConnectionData("127.0.0.1", port, "0.0.0.0"); // escolta a totes les targetes de xarxa
        Configure();
        Hook();
        MatchStarted = false;

        if (!nm.StartHost())
        {
            Unhook();
            Status("No s'ha pogut crear la sala (port " + port + " ocupat?).");
            return false;
        }
        Status("Sala creada. Esperant jugadors...");
        return true;
    }

    public bool StartClient(string ip)
    {
        if (IsActive) { Status("Ja hi ha una sessio activa."); return false; }
        ip = string.IsNullOrWhiteSpace(ip) ? "127.0.0.1" : ip.Trim();

        transport.SetConnectionData(ip, port);
        Configure();
        Hook();
        MatchStarted = false;

        if (!nm.StartClient())
        {
            Unhook();
            Status("No s'ha pogut iniciar el client.");
            return false;
        }
        Status("Connectant a " + ip + ":" + port + "...");
        return true;
    }

    // Ruta Relay/Lobby: el SDK arrenca el host/client per dins, aixi que cal preparar l'approval
    // i els callbacks ABANS de crear o unir-se a la sala (MainMenuUI ho crida).
    public void PrepareSession()
    {
        Configure();
        Hook();
        MatchStarted = false;
    }

    // Nomes el host: carrega l'escena de partida per a tots els connectats
    public bool StartMatch()
    {
        if (!IsHost || MatchStarted) return false;

        MatchStarted = true; // a partir d'aqui, ningu mes pot entrar
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        MatchServer.BotFillTo = fillWithBotsTo;
#else
        MatchServer.BotFillTo = 0;   // build final: mai hi ha bots
#endif
        MatchServer.Begin(gameObject, nm, matchSceneName); // pool compartit + jugadors (fase 3)
        var result = nm.SceneManager.LoadScene(matchSceneName, LoadSceneMode.Single);
        if (result != SceneEventProgressStatus.Started)
        {
            if (MatchServer.Instance != null) MatchServer.Instance.End();
            MatchStarted = false;
            Status("No s'ha pogut carregar l'escena '" + matchSceneName
                   + "'. Esta a Build Profiles > Scene List?");
            return false;
        }
        Status("Carregant la partida...");
        return true;
    }

    // Tanca la sessio (host o client). L'escena no canvia.
    public void Shutdown()
    {
        if (MatchServer.Instance != null) MatchServer.Instance.End();
        Unhook();

        // Host: avisa els clients amb un motiu (NGO envia el missatge abans de tancar)
        if (nm != null && nm.IsServer)
        {
            try
            {
                foreach (var id in new System.Collections.Generic.List<ulong>(nm.ConnectedClientsIds))
                    if (id != nm.LocalClientId) nm.DisconnectClient(id, "El host ha tancat la partida");
            }
            catch (Exception e) { Debug.LogWarning("[Net] No s'ha pogut avisar els clients: " + e.Message); }
        }

        if (nm != null && (nm.IsClient || nm.IsServer)) nm.Shutdown();
        MatchStarted = false;
    }

    // Tanca la sessio i torna al menu
    public void LeaveToMenu()
    {
        if (LobbyService.InSession) { LeaveRelayThenMenu(); return; }
        Shutdown();
        if (SceneManager.GetActiveScene().name != menuSceneName) StartCoroutine(LoadMenuWhenStopped());
    }

    // Sala de Relay: el host la tanca per a tothom, el client en surt. LeaveAsync tambe atura el NetworkManager.
    async void LeaveRelayThenMenu()
    {
        if (MatchServer.Instance != null) MatchServer.Instance.End();
        try { await LobbyService.LeaveAsync(); }
        catch (Exception e) { Debug.LogWarning("[Net] Error en sortir de la sala: " + e.Message); }
        Shutdown();
        if (SceneManager.GetActiveScene().name != menuSceneName) StartCoroutine(LoadMenuWhenStopped());
    }

    // ---------------------------------------------------------------- interns
    void Configure()
    {
        nm.NetworkConfig.ConnectionApproval = true; // ha de ser igual al host i als clients
        nm.ConnectionApprovalCallback = Approve;
    }

    // El host decideix qui entra: sala plena o partida ja començada = rebutjat
    void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
    {
        response.Pending = false;
        response.CreatePlayerObject = false; // el PlayerNet el crea MatchServer quan l'escena Match esta carregada

        if (MatchStarted)
        {
            response.Approved = false;
            response.Reason = "La partida ja ha començat";
            return;
        }
        if (nm.ConnectedClientsIds.Count >= MaxPlayers)
        {
            response.Approved = false;
            response.Reason = "La sala és plena";
            return;
        }
        response.Approved = true;
    }

    void Hook()
    {
        if (hooked || nm == null) return;
        nm.OnClientConnectedCallback += OnClientConnected;
        nm.OnClientDisconnectCallback += OnClientDisconnected;
        hooked = true;
    }

    void Unhook()
    {
        if (!hooked || nm == null) return;
        nm.OnClientConnectedCallback -= OnClientConnected;
        nm.OnClientDisconnectCallback -= OnClientDisconnected;
        hooked = false;
    }

    void OnClientConnected(ulong id)
    {
        if (nm.IsServer)
        {
            if (id != nm.LocalClientId) Status("Un jugador s'ha connectat.");
        }
        else
        {
            Status("Connectat al host. Esperant que comenci la partida...");
        }
    }

    void OnClientDisconnected(ulong id)
    {
        if (nm.IsServer)
        {
            if (id != nm.LocalClientId) Status("Un jugador ha sortit.");
            return;
        }

        // Som client: hem perdut la connexio, el host ha tancat o ens han rebutjat
        string reason = string.IsNullOrEmpty(nm.DisconnectReason)
            ? "S'ha perdut la connexió amb el host"
            : nm.DisconnectReason;
        ConnectionLost(reason);
    }

    // La sala de Relay s'ha acabat (el host l'ha tancada, t'han expulsat...)
    void OnLobbySessionEnded(string reason)
    {
        if (SceneManager.GetActiveScene().name == menuSceneName) return; // al menú ho gestiona MainMenuUI
        ConnectionLost(reason);
    }

    // Fase 6: el host ha tancat o la connexió s'ha perdut. Si som dins la partida no et tira al menú
    // d'un cop: mostra un missatge (amb botó per sortir). Es pot cridar dues vegades (NGO + Lobby).
    void ConnectionLost(string reason)
    {
        Shutdown();
        Status(reason);
        if (SceneManager.GetActiveScene().name != menuSceneName) MatchEndUI.ShowNotice(reason);
    }

    // NGO tanca la xarxa al final del frame: esperem abans de canviar d'escena
    IEnumerator LoadMenuWhenStopped()
    {
        for (int i = 0; i < 120 && nm != null && (nm.ShutdownInProgress || nm.IsClient || nm.IsServer); i++)
            yield return null;
        SceneManager.LoadScene(menuSceneName);
    }

    void Status(string msg)
    {
        LastStatus = msg;
        Debug.Log("[Net] " + msg);
        OnStatus?.Invoke(msg);
    }
}
