using System;

// Ordres que un jugador pot enviar. La UI i la vista només parlen amb aquesta interfície:
//  - LocalCommands (mode un jugador): executa l'ordre a l'instant sobre el ShopSystem.
//  - NetCommands (multijugador): envia l'ordre al host i espera el snapshot.
// Els resultats arriben com a missatges de text (OnMessage), perquè en xarxa són asíncrons.
public interface IPlayerCommands
{
    event Action<string> OnMessage;

    void Buy(int slot);
    void Reroll();
    void BuyXp();
    void Sell(int uid);
    void MoveToBoard(int uid, int boardIndex);
    void MoveToBench(int uid, int benchIndex);

    // Només per a proves. En xarxa el servidor només les accepta del host i en build de desenvolupament.
    void DebugAddGold(int amount);
    void DebugAddLives(int amount);
}
