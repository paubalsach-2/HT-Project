using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

// ---- Missatges de xarxa (serialitzen les dades pures de Core/Match/CombatMessages)
public struct SimUnitNet : INetworkSerializable
{
    public int Uid, DefId;
    public byte Star, Team, Col, Row;

    public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
    {
        s.SerializeValue(ref Uid);
        s.SerializeValue(ref DefId);
        s.SerializeValue(ref Star);
        s.SerializeValue(ref Team);
        s.SerializeValue(ref Col);
        s.SerializeValue(ref Row);
    }
}

public struct CombatSetupNet : INetworkSerializable
{
    public int Round;
    public bool Flipped;
    public SimUnitNet[] Units;

    public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
    {
        s.SerializeValue(ref Round);
        s.SerializeValue(ref Flipped);
        s.SerializeValue(ref Units);
    }
}

public struct CombatEndNet : INetworkSerializable
{
    public byte Outcome;                 // SimOutcome
    public int SurvivorsA, SurvivorsB, Ticks, DamageTaken;
    public bool Flipped;

    public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
    {
        s.SerializeValue(ref Outcome);
        s.SerializeValue(ref SurvivorsA);
        s.SerializeValue(ref SurvivorsB);
        s.SerializeValue(ref Ticks);
        s.SerializeValue(ref DamageTaken);
        s.SerializeValue(ref Flipped);
    }
}

// Una fila de la llista de jugadors (vides de tothom): la veuen tots els clients.
public struct PlayerInfoNet : INetworkSerializable, IEquatable<PlayerInfoNet>
{
    public ulong ClientId;
    public FixedString64Bytes Name;
    public int Lives, MaxLives;

    public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
    {
        s.SerializeValue(ref ClientId);
        s.SerializeValue(ref Name);
        s.SerializeValue(ref Lives);
        s.SerializeValue(ref MaxLives);
    }

    public bool Equals(PlayerInfoNet o)
    {
        return ClientId == o.ClientId && Name.Equals(o.Name) && Lives == o.Lives && MaxLives == o.MaxLives;
    }
}

// Fase 5: el SERVIDOR decideix i simula els combats (MatchState) i aquí només s'envien als clients:
//   començar -> CombatSetup (formació) als dos rivals, final -> CombatEnd (resultat oficial) i,
//   en acabar la partida, la classificació a tothom.
// Posar-lo en un NetworkObject de l'escena Match. El servidor crida ServerBind(match) un cop
// creat el MatchState (i amb match.BoardMap posat). Usa ClientRpc (val per a NGO 1.x i 2.x).
public class CombatNet : NetworkBehaviour
{
    MatchState match;

    // Vides de tots els jugadors (només l'escriu el servidor; tots la llegeixen)
    readonly NetworkList<PlayerInfoNet> roster = new NetworkList<PlayerInfoNet>();
    bool rosterDirty;
    PlayerHUD hud;

    // ---------------------------------------------------------------- servidor
    public void ServerBind(MatchState m)
    {
        if (!IsServer) return;
        Unbind();
        match = m;
        match.OnPhaseChanged += OnPhase;
        match.OnCombatResolved += OnResolved;
        match.OnMatchOver += OnOver;
        match.OnEliminated += OnEliminated;
        match.OnRosterChanged += PublishRoster;
        PublishRoster();
    }

    public override void OnNetworkSpawn()
    {
        roster.OnListChanged += OnRosterChanged;
        rosterDirty = true;
    }

    public override void OnNetworkDespawn()
    {
        roster.OnListChanged -= OnRosterChanged;
        Unbind();
    }

    void Unbind()
    {
        if (match == null) return;
        match.OnPhaseChanged -= OnPhase;
        match.OnCombatResolved -= OnResolved;
        match.OnMatchOver -= OnOver;
        match.OnEliminated -= OnEliminated;
        match.OnRosterChanged -= PublishRoster;
        match = null;
    }

    // Bots i clients desconnectats no tenen a qui enviar-los res
    bool Remote(ulong id)
    {
        var s = match != null ? match.Get(id) : null;
        return s != null && s.IsRemote;
    }

    static ClientRpcParams To(ulong clientId)
    {
        return new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } } };
    }

    void OnPhase(MatchPhase phase, int round)
    {
        PublishRoster();
        if (phase != MatchPhase.Combat) return;
        foreach (var f in match.Fights)
        {
            if (Remote(f.A)) StartCombatClientRpc(ToNet(CombatMessages.SetupFor(f, round, false)), To(f.A));
            if (f.RealB && Remote(f.B)) StartCombatClientRpc(ToNet(CombatMessages.SetupFor(f, round, true)), To(f.B));
        }
    }

    void OnResolved()
    {
        PublishRoster(); // les vides ja estan actualitzades
        foreach (var f in match.Fights)
        {
            if (Remote(f.A)) EndCombatClientRpc(ToNet(CombatMessages.EndFor(f, false)), To(f.A));
            if (f.RealB && Remote(f.B)) EndCombatClientRpc(ToNet(CombatMessages.EndFor(f, true)), To(f.B));
        }
    }

    void OnEliminated(PlayerSession s)
    {
        PublishRoster();
    }

    void OnOver()
    {
        PublishRoster();
        MatchOverClientRpc(match.RankingText(), match.WinnerId, match.HasWinner);
    }

    // Publica les vides de tots els jugadors perquè cadascú les vegi al seu HUD
    void PublishRoster()
    {
        if (match == null || !IsServer || !IsSpawned) return;
        roster.Clear();
        foreach (var s in match.Sessions)
        {
            roster.Add(new PlayerInfoNet
            {
                ClientId = s.ClientId,
                Name = new FixedString64Bytes(s.Name + (s.IsBot ? " [bot]" : (s.Disconnected ? " [desc.]" : ""))),
                Lives = s.State.Lives,
                MaxLives = PlayerState.MaxLives
            });
        }
    }

    // ---------------------------------------------------------------- clients
    void OnRosterChanged(NetworkListEvent<PlayerInfoNet> e)
    {
        rosterDirty = true;
    }

    void Update()
    {
        if (!rosterDirty || !IsSpawned || !NetworkManager.IsClient) return;

        // Un sol panell: la pestanya Ronda del PlayerHUD. Si el HUD encara no existeix, es reintenta el frame següent.
        if (hud == null) hud = FindFirstObjectByType<PlayerHUD>();
        if (hud == null) return;
        rosterDirty = false;

        var list = new List<PlayerEntry>(roster.Count);
        for (int i = 0; i < roster.Count; i++)
        {
            var p = roster[i];
            list.Add(new PlayerEntry
            {
                id = p.ClientId,
                name = p.Name.ToString(),
                lives = p.Lives,
                maxLives = p.MaxLives,
                local = p.ClientId == NetworkManager.LocalClientId
            });
        }
        hud.SetRoster(list);
    }
    [ClientRpc]
    void StartCombatClientRpc(CombatSetupNet msg, ClientRpcParams p = default)
    {
        GetView().Play(FromNet(msg));
    }

    [ClientRpc]
    void EndCombatClientRpc(CombatEndNet msg, ClientRpcParams p = default)
    {
        GetView().End(FromNet(msg));
    }

    [ClientRpc]
    void MatchOverClientRpc(string ranking, ulong winnerId, bool hasWinner)
    {
        bool won = hasWinner && NetworkManager.LocalClientId == winnerId;
        MatchEndUI.Show(ranking, won);
    }

    static CombatView GetView()
    {
        var v = FindFirstObjectByType<CombatView>();
        if (v == null) v = new GameObject("CombatView").AddComponent<CombatView>();
        return v;
    }

    // ---------------------------------------------------------------- conversions
    static CombatSetupNet ToNet(CombatSetup s)
    {
        var arr = new SimUnitNet[s.Units.Count];
        for (int i = 0; i < arr.Length; i++)
        {
            var u = s.Units[i];
            arr[i] = new SimUnitNet
            {
                Uid = u.Uid,
                DefId = u.DefId,
                Star = (byte)u.Star,
                Team = (byte)u.Team,
                Col = (byte)u.Col,
                Row = (byte)u.Row
            };
        }
        return new CombatSetupNet { Round = s.Round, Flipped = s.Flipped, Units = arr };
    }

    static CombatSetup FromNet(CombatSetupNet n)
    {
        var s = new CombatSetup { Round = n.Round, Flipped = n.Flipped };
        if (n.Units != null)
            foreach (var u in n.Units)
                s.Units.Add(new SimUnitSetup
                {
                    Uid = u.Uid,
                    DefId = u.DefId,
                    Star = u.Star,
                    Team = (SimTeam)u.Team,
                    Col = u.Col,
                    Row = u.Row
                });
        return s;
    }

    static CombatEndNet ToNet(CombatEnd e)
    {
        return new CombatEndNet
        {
            Outcome = (byte)e.Outcome,
            SurvivorsA = e.SurvivorsA,
            SurvivorsB = e.SurvivorsB,
            Ticks = e.Ticks,
            DamageTaken = e.DamageTaken,
            Flipped = e.Flipped
        };
    }

    static CombatEnd FromNet(CombatEndNet n)
    {
        return new CombatEnd
        {
            Outcome = (SimOutcome)n.Outcome,
            SurvivorsA = n.SurvivorsA,
            SurvivorsB = n.SurvivorsB,
            Ticks = n.Ticks,
            DamageTaken = n.DamageTaken,
            Flipped = n.Flipped
        };
    }
}