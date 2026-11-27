using System;
using MultiDEC.Core.Geometrie;

namespace MultiDEC.Core.MiseEnPlan;

/// <summary>Vecteur 3D (mètres), pour les directions de vue.</summary>
public readonly struct Vecteur3
{
    public Vecteur3(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public double X { get; }
    public double Y { get; }
    public double Z { get; }

    public static readonly Vecteur3 Haut = new Vecteur3(0, 0, 1);

    public double Longueur => Math.Sqrt(X * X + Y * Y + Z * Z);

    public Vecteur3 Norme()
    {
        double l = Longueur;
        return l < 1e-12 ? this : new Vecteur3(X / l, Y / l, Z / l);
    }

    public static Vecteur3 Vectoriel(Vecteur3 a, Vecteur3 b) =>
        new Vecteur3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    public double Scalaire(Vecteur3 b) => X * b.X + Y * b.Y + Z * b.Z;

    public double Scalaire(Point3 p) => X * p.X + Y * p.Y + Z * p.Z;

    public override string ToString() => FormattableString.Invariant($"({X:0.###}; {Y:0.###}; {Z:0.###})");
}

/// <summary>Point d'une vue : U vers la droite, V vers le haut (mètres de la structure, ou millimètres sur la feuille).</summary>
public readonly struct Point2
{
    public Point2(double u, double v)
    {
        U = u;
        V = v;
    }

    public double U { get; }
    public double V { get; }

    public static Point2 operator +(Point2 a, Point2 b) => new Point2(a.U + b.U, a.V + b.V);

    public static Point2 operator -(Point2 a, Point2 b) => new Point2(a.U - b.U, a.V - b.V);

    public static Point2 operator *(Point2 a, double k) => new Point2(a.U * k, a.V * k);

    public double Longueur => Math.Sqrt(U * U + V * V);

    public Point2 Norme() => Longueur < 1e-12 ? this : this * (1 / Longueur);

    /// <summary>Normale à gauche (rotation de +90°).</summary>
    public Point2 Normale => new Point2(-V, U);

    public override string ToString() => FormattableString.Invariant($"({U:0.###}; {V:0.###})");
}

/// <summary>Rectangle aligné (U, V).</summary>
public readonly struct Rect2
{
    public Rect2(double uMin, double vMin, double uMax, double vMax)
    {
        UMin = Math.Min(uMin, uMax);
        VMin = Math.Min(vMin, vMax);
        UMax = Math.Max(uMin, uMax);
        VMax = Math.Max(vMin, vMax);
    }

    public double UMin { get; }
    public double VMin { get; }
    public double UMax { get; }
    public double VMax { get; }

    public double Largeur => UMax - UMin;
    public double Hauteur => VMax - VMin;
    public Point2 Centre => new Point2((UMin + UMax) / 2, (VMin + VMax) / 2);

    public Rect2 Agrandi(double marge) => new Rect2(UMin - marge, VMin - marge, UMax + marge, VMax + marge);

    public Rect2 Union(Point2 p) => new Rect2(Math.Min(UMin, p.U), Math.Min(VMin, p.V), Math.Max(UMax, p.U), Math.Max(VMax, p.V));

    public static Rect2 Englobant(System.Collections.Generic.IEnumerable<Point2> points)
    {
        double u0 = double.MaxValue, v0 = double.MaxValue, u1 = double.MinValue, v1 = double.MinValue;
        foreach (var p in points)
        {
            u0 = Math.Min(u0, p.U);
            v0 = Math.Min(v0, p.V);
            u1 = Math.Max(u1, p.U);
            v1 = Math.Max(v1, p.V);
        }

        return u0 > u1 ? new Rect2(0, 0, 0, 0) : new Rect2(u0, v0, u1, v1);
    }

    public override string ToString() => FormattableString.Invariant($"[{UMin:0.##}..{UMax:0.##}] x [{VMin:0.##}..{VMax:0.##}]");
}

/// <summary>
/// Projection orthogonale d'une vue : <see cref="Vers"/> pointe de la structure vers l'observateur, U à droite, V en haut
/// (U = Haut x Vers ; vue de dessus : U = X, V = Y). Les coordonnées de vue sont les produits scalaires avec U et V.
/// </summary>
public sealed class Projection
{
    public Projection(Vecteur3 versObservateur)
    {
        Vers = versObservateur.Norme();
        var u = Vecteur3.Vectoriel(Vecteur3.Haut, Vers);
        U = u.Longueur < 1e-9 ? new Vecteur3(1, 0, 0) : u.Norme();
        V = Vecteur3.Vectoriel(Vers, U).Norme();
    }

    public Vecteur3 Vers { get; }
    public Vecteur3 U { get; }
    public Vecteur3 V { get; }

    public Point2 Projeter(Point3 p) => new Point2(U.Scalaire(p), V.Scalaire(p));

    /// <summary>Profondeur : plus grande vers l'observateur.</summary>
    public double Profondeur(Point3 p) => Vers.Scalaire(p);

    public static Projection Dessus() => new Projection(Vecteur3.Haut);

    /// <summary>Direction de la face (X local) dans la structure.</summary>
    public static Vecteur3 DirectionFace(double angleDegres)
    {
        double a = angleDegres * Math.PI / 180;
        return new Vecteur3(Math.Round(Math.Cos(a), 12), Math.Round(Math.Sin(a), 12), 0);
    }

    /// <summary>Élévation d'une face vue de l'extérieur (Y local négatif) : U = X local.</summary>
    public static Projection Elevation(double angleFace)
    {
        var d = DirectionFace(angleFace);
        return new Projection(new Vecteur3(d.Y, -d.X, 0));
    }

    /// <summary>Vue de bout : about gauche (observateur avant x = 0, extérieur à droite) ou droit (après la fin, mur à droite).</summary>
    public static Projection About(double angleFace, bool droit)
    {
        var d = DirectionFace(angleFace);
        return new Projection(droit ? d : new Vecteur3(-d.X, -d.Y, 0));
    }

    /// <summary>Isométrique : de l'extérieur, à droite, en hauteur (comme la vue Iso de l'écran) ; ou côté bâtiment, à gauche.</summary>
    public static Projection Iso(double angleFace, bool coteBatiment)
    {
        var d = DirectionFace(angleFace);
        var ext = new Vecteur3(d.Y, -d.X, 0);
        double s = coteBatiment ? -1 : 1;
        return new Projection(new Vecteur3(s * (d.X + ext.X), s * (d.Y + ext.Y), 1));
    }
}
