// Generador pseudoaleatori propi (xorshift32): el mateix seed dona la mateixa seqüència a
// qualsevol màquina i versió de Unity/.NET (System.Random i UnityEngine.Random no ho garanteixen).
public sealed class SimRandom
{
    uint s;

    public SimRandom(int seed)
    {
        unchecked
        {
            s = (uint)seed * 2654435761u + 0x9E3779B9u;
        }
        if (s == 0) s = 0x1234567u;
    }

    public uint Next()
    {
        s ^= s << 13;
        s ^= s >> 17;
        s ^= s << 5;
        return s;
    }

    // Enter en [0, n)
    public int Range(int n) => n <= 1 ? 0 : (int)(Next() % (uint)n);
}
