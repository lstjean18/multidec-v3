using System;
using System.Globalization;

namespace MultiDEC.Core.Geometrie;

/// <summary>
/// Point 3D en mètres. Repère MultiDEC :
/// X le long de la façade (0 = première file de poteaux),
/// Y = 0 sur la file intérieure (côté mur), Y négatif vers l'extérieur (file ext. à -largeur),
/// Z vers le haut (0 = dessus des embases / niveau de départ).
/// </summary>
public readonly struct Point3 : IEquatable<Point3>
{
    public Point3(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public double X { get; }
    public double Y { get; }
    public double Z { get; }

    public Point3 Decale(double dx = 0, double dy = 0, double dz = 0) => new Point3(X + dx, Y + dy, Z + dz);

    public bool Equals(Point3 other) =>
        Cotes.Egal(X, other.X) && Cotes.Egal(Y, other.Y) && Cotes.Egal(Z, other.Z);

    public override bool Equals(object? obj) => obj is Point3 p && Equals(p);

    public override int GetHashCode() =>
        (Math.Round(X, 3), Math.Round(Y, 3), Math.Round(Z, 3)).GetHashCode();

    public override string ToString() =>
        string.Format(CultureInfo.InvariantCulture, "({0:0.###}; {1:0.###}; {2:0.###})", X, Y, Z);
}
