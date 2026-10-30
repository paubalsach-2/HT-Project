using System;

// Multijugador (client): implementa IPlayerCommands enviant les ordres al servidor via PlayerNet.
//  - Compra / reroll / XP / venda: NO es toca res al client; s'espera el snapshot del servidor.
//  - Moviments: s'apliquen ABANS a la rèplica local (optimista) i després s'envien; si el servidor
//    discrepa, el seu snapshot ho corregeix.
public sealed class NetCommands : IPlayerCommands
{
    readonly ShopSystem replica;
    PlayerNet net;

    public event Action<string> OnMessage;

    public NetCommands(ShopSystem replica) { this.replica = replica; }

    // PlayerNet del jugador local; no existeix fins que el servidor l'ha creat
    public void Bind(PlayerNet playerNet) { net = playerNet; }

    bool Ready()
    {
        if (net != null && net.IsSpawned) return true;
        Say("Connectant amb el servidor...");
        return false;
    }

    public void Buy(int slot) { if (Ready()) net.BuyServerRpc(slot); }
    public void Reroll() { if (Ready()) net.RerollServerRpc(); }
    public void BuyXp() { if (Ready()) net.BuyXpServerRpc(); }
    public void Sell(int uid) { if (Ready()) net.SellServerRpc(uid); }

    public void MoveToBoard(int uid, int boardIndex)
    {
        if (!Ready()) return;
        var r = replica.MoveToBoard(uid, boardIndex);
        if (r != MoveResult.Ok) { ReportMove(r); return; }
        net.MoveToBoardServerRpc(uid, boardIndex);
    }

    public void MoveToBench(int uid, int benchIndex)
    {
        if (!Ready()) return;
        var r = replica.MoveToBench(uid, benchIndex);
        if (r != MoveResult.Ok) { ReportMove(r); return; }
        net.MoveToBenchServerRpc(uid, benchIndex);
    }

    public void DebugAddGold(int amount) { if (Ready()) net.DebugAddGoldServerRpc(amount); }
    public void DebugAddLives(int amount) { if (Ready()) net.DebugAddLivesServerRpc(amount); }

    void ReportMove(MoveResult r)
    {
        switch (r)
        {
            case MoveResult.BoardFull:
                int l = replica.BoardLimit;
                Say("Nivell " + l + ": màxim " + l + (l == 1 ? " personatge" : " personatges") + " al tauler");
                break;
            case MoveResult.WrongPhase:
                Say("Ara no pots moure unitats");
                break;
        }
    }

    public void Say(string msg) { OnMessage?.Invoke(msg); }
}
