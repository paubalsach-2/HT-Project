using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

/// <summary>
/// Fase 2b: Relay + Lobby amb el paquet "Multiplayer Services" (com.unity.services.multiplayer).
/// Una "sessió" = Lobby + Relay junts. Amb WithRelayNetwork() el SDK reserva l'allocation de Relay,
/// configura UnityTransport i fa StartHost()/StartClient() al NetworkManager de l'escena.
/// Per tant, aquí NO cal tocar manualment ni el transport ni el join code de Relay.
/// </summary>
public static class LobbyService
{
    public const int MaxPlayers = 8;
    public const string PlayerNameKey = "name";

    public static ISession Session { get; private set; }
    public static bool InSession => Session != null;
    public static bool IsHost => Session != null && Session.IsHost;
    public static string JoinCode => Session?.Code;
    public static int PlayerCount => Session?.Players?.Count ?? 0;

    /// <summary>Canvia la llista de jugadors (entra/surt algú, canvia una propietat).</summary>
    public static event Action PlayersChanged;
    /// <summary>La sessió s'ha acabat (el host l'ha tancada, t'han expulsat...). Paràmetre: motiu.</summary>
    public static event Action<string> SessionEnded;

    // ───────────────────────── Inicialització ─────────────────────────

    public static async Task EnsureSignedInAsync()
    {
        if (UnityServices.State == ServicesInitializationState.Uninitialized)
        {
            var opts = new InitializationOptions();
#if UNITY_EDITOR
            // Dues instàncies de l'editor (Multiplayer Play Mode / ParrelSync) compartirien
            // la mateixa identitat anònima en cau. Un perfil aleatori les separa.
            opts.SetProfile("ed" + Guid.NewGuid().ToString("N").Substring(0, 12));
#endif
            await UnityServices.InitializeAsync(opts);
        }

        if (!AuthenticationService.Instance.IsSignedIn)
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
    }

    // ───────────────────────── Crear / unir-se ─────────────────────────

    /// <summary>Crea sala (Lobby + Relay) i arrenca el host. Retorna el codi de sala.</summary>
    public static async Task<string> CreateAsync(string playerName)
    {
        await EnsureSignedInAsync();
        await LeaveAsync();

        var options = new SessionOptions
        {
            MaxPlayers = MaxPlayers,
            IsPrivate = true, // només s'hi entra amb codi
            PlayerProperties = NameProps(playerName)
        }.WithRelayNetwork();

        var session = await MultiplayerService.Instance.CreateSessionAsync(options);
        Attach(session);
        return session.Code;
    }

    /// <summary>Entra a una sala amb codi i arrenca el client.</summary>
    public static async Task JoinAsync(string code, string playerName)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Codi buit");

        await EnsureSignedInAsync();
        await LeaveAsync();

        var options = new JoinSessionOptions { PlayerProperties = NameProps(playerName) };
        var session = await MultiplayerService.Instance
            .JoinSessionByCodeAsync(code.Trim().ToUpperInvariant(), options);
        Attach(session);
    }

    // ───────────────────────── Partida ─────────────────────────

    /// <summary>
    /// Només host. Bloqueja la sala perquè ningú més hi pugui entrar.
    /// Després cal cridar NetBootstrap.StartMatch(), que carrega l'escena Match.
    /// </summary>
    public static async Task LockAsync()
    {
        if (!IsHost) return;
        var host = Session.AsHost();
        host.IsLocked = true;
        await host.SavePropertiesAsync();
    }

    /// <summary>Surt de la sala. El host la tanca per a tothom. Atura el NetworkManager.</summary>
    public static async Task LeaveAsync()
    {
        var s = Session;
        Session = null;

        if (s != null)
        {
            try
            {
                if (s.IsHost) await s.AsHost().DeleteAsync();
                else await s.LeaveAsync();
            }
            catch (Exception e) { Debug.LogWarning($"[Lobby] Error en sortir: {e.Message}"); }
        }

        var nm = NetworkManager.Singleton;
        if (nm != null && nm.IsListening) nm.Shutdown();
    }

    // ───────────────────────── Jugadors ─────────────────────────

    /// <summary>Llista (nom, esHost) per pintar al lobby.</summary>
    public static List<(string name, bool isHost)> GetPlayers()
    {
        var list = new List<(string, bool)>();
        if (Session == null) return list;

        foreach (var p in Session.Players)
        {
            string n = "?";
            if (p.Properties != null && p.Properties.TryGetValue(PlayerNameKey, out var prop))
                n = prop.Value;
            list.Add((n, p.Id == Session.Host));
        }
        return list;
    }

    // ───────────────────────── Interns ─────────────────────────

    static Dictionary<string, PlayerProperty> NameProps(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) name = "Jugador";
        return new Dictionary<string, PlayerProperty>
        {
            { PlayerNameKey, new PlayerProperty(name.Trim(), VisibilityPropertyOptions.Member) }
        };
    }

    static void Attach(ISession session)
    {
        Session = session;
        session.Changed += () => { if (Session == session) PlayersChanged?.Invoke(); };
        session.RemovedFromSession += () => End(session, "Has sortit de la sala.");
        session.Deleted += () => End(session, "El host ha tancat la sala.");
        PlayersChanged?.Invoke();
    }

    static void End(ISession session, string reason)
    {
        if (Session != session) return; // ja hem sortit nosaltres
        Session = null;
        var nm = NetworkManager.Singleton;
        if (nm != null && nm.IsListening) nm.Shutdown();
        SessionEnded?.Invoke(reason);
    }
}