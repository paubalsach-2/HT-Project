using System;
using Unity.Netcode;

// Rellotge de la partida tal com viatja per la xarxa (una sola NetworkVariable: fase, ronda i fi
// s'actualitzen junts, així el client mai veu una fase amb el temps de l'anterior).
//  Round == 0  -> la partida encara no ha començat.
//  EndTime     -> NetworkManager.ServerTime.Time en què acaba la fase.
public struct PhaseNet : INetworkSerializable, IEquatable<PhaseNet>
{
    public byte Phase;      // MatchPhase
    public int Round;
    public double EndTime;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Phase);
        serializer.SerializeValue(ref Round);
        serializer.SerializeValue(ref EndTime);
    }

    public bool Equals(PhaseNet o) => Phase == o.Phase && Round == o.Round && EndTime == o.EndTime;
    public override bool Equals(object obj) => obj is PhaseNet o && Equals(o);
    public override int GetHashCode() => (Phase * 397) ^ Round;
}
