using System;

// Coordenada d'una casella de la graella hexagonal ("odd-r": les files senars van mitja casella
// a la dreta). C = columna, R = fila. Sense Unity: es pot usar al servidor i als tests.
public readonly struct HexCoord : IEquatable<HexCoord>
{
    public readonly int C, R;

    public HexCoord(int c, int r) { C = c; R = r; }

    public bool Equals(HexCoord o) => C == o.C && R == o.R;
    public override bool Equals(object obj) => obj is HexCoord o && Equals(o);
    public override int GetHashCode() => C * 397 ^ R;
    public static bool operator ==(HexCoord a, HexCoord b) => a.Equals(b);
    public static bool operator !=(HexCoord a, HexCoord b) => !a.Equals(b);
    public override string ToString() => "(" + C + "," + R + ")";
}
