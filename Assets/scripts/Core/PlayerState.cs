using System;

// Estat de joc d'un jugador al SERVIDOR: or i vides. Implementa IWallet perquè la botiga el gasti.
// Lògica pura (sense Unity).
public sealed class PlayerState : IWallet
{
    // Han de coincidir amb PlayerStats (startGold / startLives / maxLives) a l'inspector.
    public const int StartGold = 10;
    public const int StartLives = 20;
    public const int MaxLives = 20;

    public int Gold { get; private set; } = StartGold;
    public int Lives { get; private set; } = StartLives;
    public bool IsDead => Lives <= 0;

    public bool TrySpend(int amount)
    {
        if (amount < 0 || Gold < amount) return false;
        Gold -= amount;
        return true;
    }

    public void AddGold(int amount) { Gold = Math.Max(0, Gold + amount); }

    public void AddLives(int amount) { Lives = Math.Max(0, Math.Min(MaxLives, Lives + amount)); }
}
