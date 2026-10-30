using System;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

// Menu principal PROVISIONAL (IMGUI) de la fase 2b.
// Dos modes: "Codi de sala" (Relay + Lobby, per Internet) i "IP directa" (LAN/localhost, fase 2a).
// Va a l'escena MainMenu, en un GameObject qualsevol (no el de NetBootstrap).
public class MainMenuUI : MonoBehaviour
{
    enum Mode { Relay, Direct }

    const string NameKey = "ht_player_name";

    Mode mode = Mode.Relay;
    string ip = "127.0.0.1";
    string code = "";
    string playerName;
    string status = "";
    string localIp = "?";
    bool busy; // hi ha una operacio async en curs
    NetBootstrap boot;
    GUIStyle titleStyle, labelStyle, codeStyle;

    void Start()
    {
        playerName = PlayerPrefs.GetString(NameKey, "Jugador" + UnityEngine.Random.Range(100, 999));

        boot = NetBootstrap.Instance;
        if (boot == null)
        {
            status = "Falta NetBootstrap a l'escena (GameObject 'Network').";
            Debug.LogError("[MainMenuUI] " + status);
            return;
        }
        status = boot.LastStatus;
        boot.OnStatus += OnStatus;
        LobbyService.SessionEnded += OnSessionEnded;
        localIp = FindLocalIp();
    }

    void OnDestroy()
    {
        if (boot != null) boot.OnStatus -= OnStatus;
        LobbyService.SessionEnded -= OnSessionEnded;
    }

    void OnStatus(string msg) { status = msg; }
    void OnSessionEnded(string reason) { status = reason; }

    // ───────────────────────── Accions ─────────────────────────

    async void CreateRoom()
    {
        if (busy) return;
        busy = true;
        SaveName();
        try
        {
            status = "Creant sala...";
            boot.PrepareSession(); // approval + callbacks abans que el SDK arrenqui el host
            await LobbyService.CreateAsync(playerName); // el SDK arrenca el host (NetBootstrap ho detecta)
            status = "Sala creada. Comparteix el codi.";
        }
        catch (Exception e) { Fail("No s'ha pogut crear la sala: ", e); }
        finally { busy = false; }
    }

    async void JoinRoom()
    {
        if (busy) return;
        if (string.IsNullOrWhiteSpace(code)) { status = "Escriu el codi de sala."; return; }
        busy = true;
        SaveName();
        try
        {
            status = "Entrant a la sala...";
            boot.PrepareSession(); // approval + callbacks abans que el SDK arrenqui el client
            await LobbyService.JoinAsync(code, playerName); // el SDK arrenca el client
        }
        catch (Exception e) { Fail("No s'ha pogut entrar: ", e); }
        finally { busy = false; }
    }

    async void StartMatch()
    {
        if (busy) return;
        busy = true;
        try
        {
            if (LobbyService.InSession) await LobbyService.LockAsync(); // ningu mes pot entrar
            boot.StartMatch();                                          // carrega l'escena Match
        }
        catch (Exception e) { Fail("No s'ha pogut comencar: ", e); }
        finally { busy = false; }
    }

    async void LeaveOrCancel()
    {
        if (busy) return;
        busy = true;
        try
        {
            if (LobbyService.InSession) await LobbyService.LeaveAsync(); // tambe atura el NetworkManager
            else boot.Shutdown();
        }
        catch (Exception e) { Fail("Error en sortir: ", e); }
        finally { busy = false; }
    }

    void Fail(string prefix, Exception e)
    {
        status = prefix + e.Message;
        Debug.LogException(e);
    }

    void SaveName()
    {
        playerName = string.IsNullOrWhiteSpace(playerName) ? "Jugador" : playerName.Trim();
        PlayerPrefs.SetString(NameKey, playerName);
    }

    // ───────────────────────── UI ─────────────────────────

    void OnGUI()
    {
        EnsureStyles();

        float s = Mathf.Max(0.75f, Screen.height / 720f);
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float sw = Screen.width / s, sh = Screen.height / s;

        const float W = 460f, H = 520f;
        var r = new Rect((sw - W) * 0.5f, (sh - H) * 0.5f, W, H);
        GUI.Box(r, GUIContent.none);
        GUILayout.BeginArea(new Rect(r.x + 20f, r.y + 16f, W - 40f, H - 32f));

        GUILayout.Label("HISTORICAL TACTICS", titleStyle);
        GUILayout.Space(10f);

        if (boot == null) { }
        else if (!boot.IsActive) DrawIdle();
        else if (boot.IsHost) DrawHost();
        else DrawClient();

        GUILayout.FlexibleSpace();
        GUILayout.Label(status, labelStyle);
        GUILayout.EndArea();

        GUI.matrix = Matrix4x4.identity;
    }

    void DrawIdle()
    {
        GUI.enabled = !busy;

        mode = (Mode)GUILayout.Toolbar((int)mode, new[] { "Codi de sala", "IP directa" }, GUILayout.Height(30f));
        GUILayout.Space(12f);

        if (mode == Mode.Relay)
        {
            GUILayout.Label("El teu nom:", labelStyle);
            playerName = GUILayout.TextField(playerName ?? "", 20, GUILayout.Height(30f));
            GUILayout.Space(10f);

            if (GUILayout.Button("CREAR SALA (host)", GUILayout.Height(44f))) CreateRoom();

            GUILayout.Space(14f);
            GUILayout.Label("O unir-se amb un codi de sala:", labelStyle);
            code = GUILayout.TextField(code ?? "", 8, GUILayout.Height(30f)).ToUpperInvariant();
            if (GUILayout.Button("UNIR-SE", GUILayout.Height(44f))) JoinRoom();
        }
        else
        {
            if (GUILayout.Button("CREAR SALA (host)", GUILayout.Height(44f))) boot.StartHost();
            GUILayout.Space(14f);
            GUILayout.Label("O unir-se a una sala (IP del host):", labelStyle);
            ip = GUILayout.TextField(ip, 45, GUILayout.Height(30f));
            if (GUILayout.Button("UNIR-SE", GUILayout.Height(44f))) boot.StartClient(ip);
        }

        GUI.enabled = true;
    }

    void DrawHost()
    {
        if (LobbyService.InSession)
        {
            GUILayout.Label("Codi de sala (comparteix-lo):", labelStyle);
            GUILayout.Label(LobbyService.JoinCode ?? "...", codeStyle);
            if (GUILayout.Button("COPIAR CODI", GUILayout.Height(28f)))
                GUIUtility.systemCopyBuffer = LobbyService.JoinCode ?? "";
            GUILayout.Space(8f);
            DrawPlayerList();
        }
        else
        {
            GUILayout.Label("Sala creada  -  port " + boot.port, labelStyle);
            GUILayout.Label("IP perquè s'hi connectin (LAN): " + localIp, labelStyle);
            GUILayout.Label("Jugadors connectats: " + boot.PlayerCount + " / " + NetBootstrap.MaxPlayers, labelStyle);
        }

        GUILayout.Space(14f);
        GUI.enabled = !busy;
        if (GUILayout.Button("COMENÇAR PARTIDA", GUILayout.Height(44f))) StartMatch();
        if (GUILayout.Button("CANCEL·LAR SALA", GUILayout.Height(34f))) LeaveOrCancel();
        GUI.enabled = true;
    }

    void DrawClient()
    {
        if (LobbyService.InSession)
        {
            GUILayout.Label("Sala: " + LobbyService.JoinCode, labelStyle);
            DrawPlayerList();
        }

        GUILayout.Label(boot.IsConnected
            ? "Connectat. Esperant que el host comenci la partida..."
            : "Connectant...", labelStyle);
        GUILayout.Space(14f);
        GUI.enabled = !busy;
        if (GUILayout.Button("SORTIR", GUILayout.Height(34f))) LeaveOrCancel();
        GUI.enabled = true;
    }

    void DrawPlayerList()
    {
        var players = LobbyService.GetPlayers();
        GUILayout.Label("Jugadors (" + players.Count + " / " + LobbyService.MaxPlayers + "):", labelStyle);
        foreach (var (name, isHost) in players)
            GUILayout.Label("  •  " + name + (isHost ? "  (host)" : ""), labelStyle);
    }

    void EnsureStyles()
    {
        if (titleStyle != null) return;
        titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true };
        codeStyle = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
    }

    static string FindLocalIp()
    {
        try
        {
            foreach (var a in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                if (a.AddressFamily == AddressFamily.InterNetwork) return a.ToString();
        }
        catch { }
        return "?";
    }
}
