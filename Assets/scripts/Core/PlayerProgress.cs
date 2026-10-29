using System;

// Nivell i XP del jugador. Lògica pura.
public sealed class PlayerProgress
{
    public int Level { get; private set; } = ShopConfig.StartLevel;
    public int Xp { get; private set; }
    public bool IsMax => Level >= ShopConfig.MaxLevel;
    public int XpToNext => ShopConfig.XpToNext(Level);

    public event Action OnChanged;

    public void AddXp(int amount)
    {
        if (IsMax || amount <= 0) return;
        Xp += amount;
        while (!IsMax && Xp >= XpToNext)
        {
            Xp -= XpToNext;
            Level++;
        }
        if (IsMax) Xp = 0;
        OnChanged?.Invoke();
    }

    // Per aplicar un snapshot (mirall del client)
    public void Set(int level, int xp)
    {
        Level = Math.Min(Math.Max(level, ShopConfig.StartLevel), ShopConfig.MaxLevel);
        Xp = IsMax ? 0 : Math.Max(0, xp);
        OnChanged?.Invoke();
    }
}
