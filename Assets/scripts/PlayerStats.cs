using System;
using UnityEngine;

// Estat del jugador (or + vides). Sense lògica de joc: només dades i events.
public class PlayerStats : MonoBehaviour
{
    [Header("Inici")]
    [SerializeField] int startGold = 10;
    [SerializeField] int maxLives = 20;
    [SerializeField] int startLives = 20;

    public int Gold { get; private set; }
    public int Lives { get; private set; }
    public int MaxLives => maxLives;
    public bool IsDead => Lives <= 0;

    public event Action<int, int> OnGoldChanged;
    public event Action<int, int> OnLivesChanged;

    void Awake()
    {
        Gold = Mathf.Max(0, startGold);
        Lives = Mathf.Min(startLives, maxLives);
    }

    // ---------------------------------------------------------------- or
    public void AddGold(int amount) => SetGold(Gold + amount);

    public bool TrySpend(int amount)
    {
        if (amount < 0 || Gold < amount) return false;
        SetGold(Gold - amount);
        return true;
    }

    public void SetGold(int value)
    {
        value = Mathf.Max(0, value);
        if (value == Gold) return;
        int delta = value - Gold;
        Gold = value;
        OnGoldChanged?.Invoke(Gold, delta);
    }

    // ---------------------------------------------------------------- vides
    public void AddLives(int amount) => SetLives(Lives + amount);

    public void SetLives(int value)
    {
        value = Mathf.Min(value, maxLives);
        if (value == Lives) return;
        int delta = value - Lives;
        Lives = value;
        OnLivesChanged?.Invoke(Lives, delta);
    }

    public void ResetStats()
    {
        SetGold(startGold);
        SetLives(startLives);
    }
}
