using System;
using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

// Només al HOST. Viu al mateix GameObject que NetBootstrap (sobreviu al canvi d'escena).
// Quan tots els clients han carregat l'escena de partida: crea el MatchState (pool únic) i, per a cada
// jugador connectat, una PlayerSession i el seu PlayerNet (Player Prefab del NetworkManager).
// Després en fa de DIRECTOR: fa avançar el rellotge (Prep -> Combat -> Resolve) a 20 ticks/s sobre
// NetworkManager.ServerTime i publica cada canvi de fase a tots els clients (PlayerNet.Clock).
public sealed class MatchServer : MonoBehaviour
{
    public static MatchServer Instance { get; private set; }
    public MatchState Match { get; private set; }

    // Durades (s). Es poden canviar (p. ex. des de NetBootstrap) abans de començar la partida.
    public static int PrepSeconds = 30;
    public static int CombatSeconds = 6;    // placeholder fins a la fase 5 (combat real)
    public static int ResolveSeconds = 3;
    public static int DisconnectGraceSeconds = 30;  // fase 6: temps abans d'eliminar un client desconnectat
    public static int BotFillTo = 0;                // fase 6: omple la partida amb bots fins a N jugadors (proves)
    const float StartDelay = 2f;            // marge perquè tots els PlayerNet estiguin creats

    // Rellotge actual (el que es publica als clients). Round = 0 si la partida encara no ha començat.
    public PhaseNet CurrentClock { get; private set; }

    NetworkManager nm;
    string sceneName;
    bool spawned;
    bool hooked;
    float waitView;         // temps esperant que existeixi el BenchView (el crea el jugador local)

    float startAt = -1f;    // temps (Time.unscaledTime) en què arrenca el rellotge
    bool running;
    double t0;              // ServerTime en què van començar els ticks
    long ticksDone;

    // El crida NetBootstrap.StartMatch() just abans de carregar l'escena
    public static MatchServer Begin(GameObject host, NetworkManager nm, string sceneName)
    {
        var s = host.GetComponent<MatchServer>();
        if (s == null) s = host.AddComponent<MatchServer>();
        s.Init(nm, sceneName);
        return s;
    }

    void Init(NetworkManager manager, string scene)
    {
        Unhook();
        nm = manager;
        sceneName = scene;
        spawned = false;
        running = false;
        startAt = -1f;
        waitView = 0f;
        ticksDone = 0;
        CurrentClock = default(PhaseNet);

        if (Match != null) Match.OnPhaseChanged -= OnPhase;
        Match = new MatchState();

        Match.Configure(PrepSeconds, CombatSeconds, ResolveSeconds);
        Match.DisconnectGraceTicks = Math.Max(1, DisconnectGraceSeconds) * SimConfig.TicksPerSecond;
        Match.OnPhaseChanged += OnPhase;
        Instance = this;

        nm.SceneManager.OnLoadEventCompleted += OnLoadCompleted;
        nm.OnClientDisconnectCallback += OnClientDisconnect;
        hooked = true;
    }

    // El crida NetBootstrap.Shutdown()
    public void End()
    {
        Unhook();
        if (Instance == this) Instance = null;
        if (Match != null) Match.OnPhaseChanged -= OnPhase;
        Match = null;
        Destroy(this);
    }

    void OnDestroy()
    {
        Unhook();
        if (Instance == this) Instance = null;
    }

    void Unhook()
    {
        if (!hooked) return;
        hooked = false;
        try
        {
            if (nm != null)
            {
                if (nm.SceneManager != null) nm.SceneManager.OnLoadEventCompleted -= OnLoadCompleted;
                nm.OnClientDisconnectCallback -= OnClientDisconnect;
            }
        }
        catch (Exception) { }
    }

    // ---------------------------------------------------------------- creació de jugadors
    void OnLoadCompleted(string scene, LoadSceneMode mode, List<ulong> completed, List<ulong> timedOut)
    {
        if (scene != sceneName || spawned) return;
        spawned = true;

        var prefab = nm.NetworkConfig.PlayerPrefab;
        if (prefab == null || prefab.GetComponent<PlayerNet>() == null)
        {
            Debug.LogError("[MatchServer] El Player Prefab del NetworkManager ha de tenir NetworkObject + PlayerNet.");
            return;
        }

        var ids = new List<ulong>(nm.ConnectedClientsIds);
        foreach (var id in ids)
        {
            Match.AddPlayer(id, "Jugador " + (id + 1));
            var go = Instantiate(prefab);
            go.GetComponent<NetworkObject>().SpawnAsPlayerObject(id);
        }

        // Fase 6: bots fins a BotFillTo (mai més del màxim de la sala)
        int target = Math.Min(BotFillTo, NetBootstrap.MaxPlayers);
        int bots = 0;
        while (Match.PlayerCount < target) Match.AddBot("Bot " + (++bots));

        Debug.Log("[MatchServer] Partida creada amb " + ids.Count + " jugadors i " + bots + " bots.");

        startAt = Time.unscaledTime + StartDelay; // el rellotge arrenca d'aquí a un moment
    }

    // ---------------------------------------------------------------- rellotge de la partida
    void Update()
    {
        if (Match == null || nm == null || !nm.IsServer) return;

        if (!running)
        {
            if (startAt < 0f || Time.unscaledTime < startAt) return;

            // Fase 5: el combat necessita la geometria de l'arena (BenchView) i el CombatNet de l'escena
            var view = FindFirstObjectByType<BenchView>();
            if (view == null && waitView < 10f)
            {
                waitView += Time.unscaledDeltaTime; // espera (màxim 10 s) que el jugador local creï el BenchView
                return;
            }
            if (view != null) Match.BoardMap = new BenchBoardMap(view);
            else Debug.LogWarning("[MatchServer] No hi ha BenchView: el combat serà de prova, sense danys.");

            var combatNet = FindFirstObjectByType<CombatNet>();
            if (combatNet != null) combatNet.ServerBind(Match);
            else Debug.LogError("[MatchServer] Falta el CombatNet a l'escena Match.");

            startAt = -1f;
            running = true;
            ticksDone = 0;
            t0 = nm.ServerTime.Time;
            Match.Start(); // dispara (Prep, ronda 1) -> OnPhase
            return;
        }

        // Els ticks se segueixen de ServerTime (no d'un acumulador propi): si el host va a trompades,
        // el compte enrere dels clients i el canvi real de fase continuen coincidint.
        long target = (long)Math.Floor((nm.ServerTime.Time - t0) * SimConfig.TicksPerSecond);
        int guard = 0;
        while (ticksDone < target && guard++ < 400)
        {
            ticksDone++;
            Match.Tick();
        }
    }

    // Després d'aplicar les regles de la fase (ingressos, botigues noves, bloqueig de moviments):
    // publica el rellotge i envia a cada jugador el seu estat nou amb el missatge pendent.
    void OnPhase(MatchPhase phase, int round)
    {
        double end = t0 + (ticksDone + Match.TicksLeftInPhase) / (double)SimConfig.TicksPerSecond;
        CurrentClock = new PhaseNet { Phase = (byte)phase, Round = round, EndTime = end };

        foreach (var s in Match.Sessions)
        {
            var net = NetOf(s.ClientId);
            if (net == null) continue;
            net.Clock.Value = CurrentClock;
            net.ServerPush(s.TakeMessage());
        }

        if (Debug.isDebugBuild)
            Debug.Log("[Server] Ronda " + round + " - " + phase + " (" + Match.TicksLeftInPhase / SimConfig.TicksPerSecond + " s)");
    }

    PlayerNet NetOf(ulong clientId)
    {
        NetworkClient c;
        if (nm != null && nm.ConnectedClients.TryGetValue(clientId, out c) && c.PlayerObject != null)
            return c.PlayerObject.GetComponent<PlayerNet>();
        return null;
    }

    void OnClientDisconnect(ulong id)
    {
        if (Match == null) return;
        // Fase 6: període de gràcia (el controla la IA) i després queda eliminat; la sessió es manté
        // perquè surti a la classificació. Abans de començar la partida simplement desapareix.
        Match.Disconnect(id);
    }

    // Només per veure al log del host que el pool és compartit
    public void LogPool(PlayerSession who)
    {
        if (!Debug.isDebugBuild || Match == null) return;
        var sb = new StringBuilder("[Server] ").Append(who.Name).Append(": or=").Append(who.State.Gold)
            .Append("  pool per tier:");
        for (int t = 1; t <= ShopConfig.MaxTier; t++) sb.Append(' ').Append('T').Append(t).Append('=').Append(Match.Pool.TierStock(t));
        Debug.Log(sb.ToString());
    }
}