using System;

// Mode un jugador: les ordres s'apliquen directament al ShopSystem.
public sealed class LocalCommands : IPlayerCommands
{
    readonly ShopSystem shop;
    readonly Action<int> debugGold, debugLives; // l'or i les vides viuen a PlayerStats (fora de Core)

    public event Action<string> OnMessage;

    public LocalCommands(ShopSystem shop, Action<int> debugGold = null, Action<int> debugLives = null)
    {
        this.shop = shop;
        this.debugGold = debugGold;
        this.debugLives = debugLives;
    }

    public void Buy(int slot) => ReportShop(shop.Buy(slot));
    public void Reroll() => ReportShop(shop.Reroll());
    public void BuyXp() => ReportShop(shop.BuyXp());

    public void Sell(int uid)
    {
        int gold = shop.Sell(uid);
        if (gold > 0) Say("Venut: +" + gold + " or");
    }

    public void MoveToBoard(int uid, int boardIndex) => ReportMove(shop.MoveToBoard(uid, boardIndex));
    public void MoveToBench(int uid, int benchIndex) => ReportMove(shop.MoveToBench(uid, benchIndex));

    public void DebugAddGold(int amount) => debugGold?.Invoke(amount);
    public void DebugAddLives(int amount) => debugLives?.Invoke(amount);

    void ReportShop(ShopResult r)
    {
        switch (r)
        {
            case ShopResult.NotEnoughGold: Say("No tens prou or"); break;
            case ShopResult.BenchFull: Say("El banc és ple"); break;
            case ShopResult.MaxLevel: Say("Ja ets al nivell màxim"); break;
        }
    }

    void ReportMove(MoveResult r)
    {
        switch (r)
        {
            case MoveResult.BoardFull:
                int l = shop.BoardLimit;
                Say("Nivell " + l + ": màxim " + l + (l == 1 ? " personatge" : " personatges") + " al tauler");
                break;
            case MoveResult.WrongPhase:
                Say("Ara no pots moure unitats");
                break;
        }
    }

    void Say(string msg) => OnMessage?.Invoke(msg);
}
