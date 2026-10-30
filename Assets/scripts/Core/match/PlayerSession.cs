using System;

// Un jugador dins la partida, al SERVIDOR: el seu estat (or/vides) i la seva botiga/banc/tauler.
// Les ordres del client arriben aquí i es VALIDEN aquí. Cada mètode retorna el missatge per
// mostrar al jugador (o null si no cal). Lògica pura: es prova amb tests d'Editor, sense xarxa.
public sealed class PlayerSession
{
    public readonly ulong ClientId;
    public int Place;
    public string Name;
    public readonly PlayerState State = new PlayerState();
    public readonly ShopSystem Shop;

    public const string InvalidMsg = "Ordre no vàlida";
    public const string DeadMsg = "Has estat eliminat";

    public PlayerSession(ulong clientId, string name, UnitPool pool, Func<int> uidGen, int seed)
    {
        ClientId = clientId;
        Name = name;
        Shop = new ShopSystem(State, pool, uidGen, seed);
    }

    public bool IsAlive => !State.IsDead;

    // Fase 6
    public bool IsBot;               // jugador controlat per la IA del servidor (BotBrain); no té client
    public bool Disconnected;        // el client s'ha desconnectat en plena partida (període de gràcia o eliminat)
    public int DisconnectTicksLeft;  // ticks que queden abans d'eliminar-lo
    public bool IsRemote => !IsBot && !Disconnected;   // té un client real al qual enviar missatges

    // Missatge que el servidor té pendent d'enviar al jugador (p. ex. els ingressos d'una ronda nova)
    public string PendingMessage;
    public string TakeMessage() { var m = PendingMessage; PendingMessage = null; return m; }

    // ---------- ordres (client -> servidor) ----------

    public string Buy(int slot) => IsAlive ? ShopMsg(Shop.Buy(slot)) : DeadMsg;
    public string Reroll() => IsAlive ? ShopMsg(Shop.Reroll()) : DeadMsg;
    public string BuyXp() => IsAlive ? ShopMsg(Shop.BuyXp()) : DeadMsg;

    // Només es poden vendre unitats del PROPI banc/tauler (Bench.ByUid no veu les dels altres).
    public string Sell(int uid)
    {
        if (!IsAlive) return DeadMsg;
        int gold = Shop.Sell(uid);
        return gold > 0 ? "Venut: +" + gold + " or" : InvalidMsg;
    }

    public string MoveToBoard(int uid, int boardIndex) => IsAlive ? MoveMsg(Shop.MoveToBoard(uid, boardIndex)) : DeadMsg;
    public string MoveToBench(int uid, int benchIndex) => IsAlive ? MoveMsg(Shop.MoveToBench(uid, benchIndex)) : DeadMsg;

    // Proves (el servidor només les accepta del host i en build de desenvolupament)
    public void DebugAddGold(int amount) => State.AddGold(Clamp(amount));
    public void DebugAddLives(int amount) => State.AddLives(Clamp(amount));

    public PlayerSnapshot Snapshot()
    {
        return Shop.ExportSnapshot();
    }

    // ---------- missatges ----------

    string ShopMsg(ShopResult r)
    {
        switch (r)
        {
            case ShopResult.NotEnoughGold: return "No tens prou or";
            case ShopResult.BenchFull: return "El banc és ple";
            case ShopResult.MaxLevel: return "Ja ets al nivell màxim";
            case ShopResult.EmptySlot: return InvalidMsg;
            default: return null;
        }
    }

    string MoveMsg(MoveResult r)
    {
        switch (r)
        {
            case MoveResult.BoardFull:
                int l = Shop.BoardLimit;
                return "Nivell " + l + ": màxim " + l + (l == 1 ? " personatge" : " personatges") + " al tauler";
            case MoveResult.WrongPhase: return "Ara no pots moure unitats";
            case MoveResult.Invalid: return InvalidMsg;
            default: return null;
        }
    }

    static int Clamp(int v) => Math.Max(-100, Math.Min(100, v));
}
