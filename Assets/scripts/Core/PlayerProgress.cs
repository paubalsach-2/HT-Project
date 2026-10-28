using System;

// Nivell i experiència del jugador. De moment només condiciona els tiers de la botiga.
public sealed class PlayerProgress
{
    public int Level { get; private set; } = ShopConfig.StartLevel;
    public int Xp { get; private set; }
    public int XpToNext => ShopConfig.XpToNext(Level);
    public bool IsMax => Level >= ShopConfig.MaxLevel;

    public event Action OnChanged;

    public void AddXp(int amount)
    {
        if (IsMax || amount <= 0) return;
        Xp += amount;
        while (!IsMax && Xp >= XpToNext) { Xp -= XpToNext; Level++; }
        if (IsMax) Xp = 0;
        OnChanged?.Invoke();
    }
}
