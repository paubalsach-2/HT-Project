using Unity.Netcode;

// PlayerSnapshot (Core, sense Netcode) empaquetat per enviar-lo per RPC.
// Es converteix amb From() abans d'enviar i amb ToSnapshot() en rebre.
public struct NetSnapshot : INetworkSerializable
{
    public int Gold, Level, Xp;
    public int[] ShopDefIds;            // ShopConfig.ShopSlots (-1 = buit)
    public int[] Uids, DefIds, Stars;   // Bench.SnapshotSize (tauler + banc)

    public static NetSnapshot From(PlayerSnapshot s)
    {
        int n = s.Cells.Length;
        var r = new NetSnapshot
        {
            Gold = s.Gold, Level = s.Level, Xp = s.Xp,
            ShopDefIds = (int[])s.ShopDefIds.Clone(),
            Uids = new int[n], DefIds = new int[n], Stars = new int[n]
        };
        for (int i = 0; i < n; i++)
        {
            r.Uids[i] = s.Cells[i].Uid;
            r.DefIds[i] = s.Cells[i].DefId;
            r.Stars[i] = s.Cells[i].Star;
        }
        return r;
    }

    public PlayerSnapshot ToSnapshot()
    {
        var s = new PlayerSnapshot { Gold = Gold, Level = Level, Xp = Xp };

        for (int i = 0; i < s.ShopDefIds.Length; i++)
            s.ShopDefIds[i] = ShopDefIds != null && i < ShopDefIds.Length ? ShopDefIds[i] : -1;

        for (int i = 0; i < s.Cells.Length; i++)
        {
            bool ok = Uids != null && DefIds != null && Stars != null
                      && i < Uids.Length && i < DefIds.Length && i < Stars.Length;
            s.Cells[i] = ok
                ? new UnitSnapshot { Uid = Uids[i], DefId = DefIds[i], Star = Stars[i] }
                : UnitSnapshot.Empty;
        }
        return s;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Gold);
        serializer.SerializeValue(ref Level);
        serializer.SerializeValue(ref Xp);
        serializer.SerializeValue(ref ShopDefIds);
        serializer.SerializeValue(ref Uids);
        serializer.SerializeValue(ref DefIds);
        serializer.SerializeValue(ref Stars);
    }
}
