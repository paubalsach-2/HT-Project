using System;

// Constants i taules de la botiga (copiades del TFT normal).
public static class ShopConfig
{
    public const int ShopSlots = 5;
    public const int RerollCost = 2;
    public const int BuyXpCost = 4;
    public const int BuyXpAmount = 4;
    public const int BenchSize = 9;
    public const int StartLevel = 1;
    public const int MaxLevel = 10;
    public const int MaxTier = 5;

    // Còpies de cada personatge a la reserva compartida (índex = tier)
    public static readonly int[] CopiesPerTier = { 0, 29, 22, 18, 12, 10 };

    // XP necessària per passar de nivell N a N+1 (índex = nivell)
    static readonly int[] xpToNext = { 0, 2, 2, 6, 10, 20, 36, 48, 76, 84, 0 };

    // Probabilitat (%) de cada tier (columnes 1..5) segons el nivell (files 1..10)
    static readonly int[][] odds =
    {
        new[] {   0,   0,  0,  0,  0 }, // (sense ús)
        new[] { 100,   0,  0,  0,  0 }, // 1
        new[] { 100,   0,  0,  0,  0 }, // 2
        new[] {  75,  25,  0,  0,  0 }, // 3
        new[] {  55,  30, 15,  0,  0 }, // 4
        new[] {  45,  33, 20,  2,  0 }, // 5
        new[] {  30,  40, 25,  5,  0 }, // 6
        new[] {  19,  30, 35, 15,  1 }, // 7
        new[] {  16,  20, 35, 25,  4 }, // 8
        new[] {   9,  15, 30, 30, 16 }, // 9
        new[] {   5,  10, 20, 40, 25 }, // 10
    };

    public static int CostOfTier(int tier) => tier;
    public static int XpToNext(int level) => level >= MaxLevel ? 0 : xpToNext[Math.Max(level, 1)];
    public static int[] Odds(int level) => odds[Math.Min(Math.Max(level, 1), MaxLevel)];
}
