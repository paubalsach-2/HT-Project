using System;

// Regles d'economia i dany de la partida (Core, sense Unity). Mateixos valors que RoundManager,
// però en enters i en un sol lloc, perquè el servidor i el client calculin igual.
public static class Economy
{
    public const int GoldPerRound = 5;      // ingrés base en acabar cada ronda
    public const int InterestStep = 10;     // cada 10 d'or guardats...
    public const int InterestPerStep = 1;   // ...donen 1 d'or d'interès
    public const int MaxInterest = 5;       // límit (amb 50 d'or)

    public const int BaseDamage = 2;        // dany base a la ronda 1
    public const int DamagePerSurvivor = 1; // dany extra per cada unitat enemiga que sobrevisqui

    // El dany base creix +1 cada 2 rondes
    public static int BaseDamageForRound(int round)
    {
        return BaseDamage + Math.Max(0, round - 1) / 2;
    }

    public static int DamageFor(int round, int enemySurvivors)
    {
        return Math.Max(0, BaseDamageForRound(round) + enemySurvivors * DamagePerSurvivor);
    }

    public static int InterestFor(int gold)
    {
        int v = (Math.Max(0, gold) / InterestStep) * InterestPerStep;
        return Math.Min(v, MaxInterest);
    }

    public static int IncomeFor(int gold) => GoldPerRound + InterestFor(gold);
}
