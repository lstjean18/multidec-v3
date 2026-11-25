using System;
using System.Collections.Generic;
using System.Linq;
using MultiDEC.Core.Geometrie;

namespace MultiDEC.Core.Facade;

/// <summary>Mur du bâtiment devant une face : segment au sol (repère de la structure) et hauteur.</summary>
public sealed class MurFacade
{
    public MurFacade(string nomFace, Point3 debut, Point3 fin, double hauteur, IReadOnlyList<OuvertureMur>? ouvertures = null)
    {
        NomFace = nomFace;
        Debut = debut;
        Fin = fin;
        Hauteur = hauteur;
        Ouvertures = ouvertures ?? Array.Empty<OuvertureMur>();
    }

    /// <summary>Passages laissés sous les poutres de franchissement (distances comptées depuis <see cref="Debut"/>).</summary>
    public IReadOnlyList<OuvertureMur> Ouvertures { get; }

    /// <summary>
    /// Blocs pleins du mur : pans entre les ouvertures sur toute la hauteur, et linteau au-dessus de chaque ouverture.
    /// </summary>
    public IReadOnlyList<BlocMur> Blocs()
    {
        var blocs = new List<BlocMur>();
        double s = 0;
        foreach (var o in Ouvertures.OrderBy(o => o.Debut))
        {
            if (o.Debut - s > Cotes.Tolerance)
            {
                blocs.Add(new BlocMur(s, Cotes.Arrondi(o.Debut - s), 0, Hauteur));
            }

            if (Hauteur - o.Hauteur > Cotes.Tolerance)
            {
                blocs.Add(new BlocMur(o.Debut, Cotes.Arrondi(o.Fin - o.Debut), o.Hauteur, Cotes.Arrondi(Hauteur - o.Hauteur)));
            }

            s = Math.Max(s, o.Fin);
        }

        if (Longueur - s > Cotes.Tolerance)
        {
            blocs.Add(new BlocMur(s, Cotes.Arrondi(Longueur - s), 0, Hauteur));
        }

        return blocs;
    }

    public string NomFace { get; }

    /// <summary>Extrémités au sol ; le bâtiment est à gauche quand on va de <see cref="Debut"/> à <see cref="Fin"/>.</summary>
    public Point3 Debut { get; }

    public Point3 Fin { get; }

    public double Hauteur { get; }

    public double Longueur => Math.Sqrt(Math.Pow(Fin.X - Debut.X, 2) + Math.Pow(Fin.Y - Debut.Y, 2));

    /// <summary>Angle du mur autour de Z, en radians.</summary>
    public double Angle => Math.Atan2(Fin.Y - Debut.Y, Fin.X - Debut.X);

    public override string ToString() => FormattableString.Invariant($"{NomFace} ({Debut.X:0.###};{Debut.Y:0.###}) → ({Fin.X:0.###};{Fin.Y:0.###}) h {Hauteur:0.##}");
}

/// <summary>Ouverture dans un mur : de <see cref="Debut"/> à <see cref="Fin"/> le long du mur, du sol à <see cref="Hauteur"/>.</summary>
public readonly struct OuvertureMur
{
    public OuvertureMur(double debut, double fin, double hauteur)
    {
        Debut = debut;
        Fin = fin;
        Hauteur = hauteur;
    }

    public double Debut { get; }

    public double Fin { get; }

    public double Hauteur { get; }
}

/// <summary>Bloc plein d'un mur : position le long du mur, longueur, cote du bas et hauteur.</summary>
public readonly struct BlocMur
{
    public BlocMur(double position, double longueur, double z, double hauteur)
    {
        Position = position;
        Longueur = longueur;
        Z = z;
        Hauteur = hauteur;
    }

    public double Position { get; }

    public double Longueur { get; }

    public double Z { get; }

    public double Hauteur { get; }
}

/// <summary>
/// Façade du bâtiment (case « Dessiner façade ») : pour chaque face, un mur parallèle à la file intérieure,
/// à la distance « Distance à la façade » côté bâtiment (Y locaux positifs), aussi haut que l'échafaudage.
/// Aux angles, les murs de deux faces raccordées sont prolongés ou raccourcis jusqu'à leur intersection,
/// ce qui vaut pour les structures normales (angle sortant) comme inversées (angle rentrant).
/// Structure volumique : seul M1 est contre la façade.
/// </summary>
public static class MursFacade
{
    public static IReadOnlyList<MurFacade> Calculer(ProjetStructure projet)
    {
        var murs = new List<MurFacade>();
        var poses = GenerateurStructure.Poses(projet);
        int n = projet.EstVolumique ? Math.Min(1, projet.Faces.Count) : projet.Faces.Count;
        for (int i = 0; i < n; i++)
        {
            var face = projet.Faces[i];
            if (face.Mailles.Count == 0)
            {
                continue;
            }

            double debut = 0;
            double fin = face.LongueurObtenue;
            if (GenerateurStructure.RaccordeeAvant(projet, i))
            {
                debut = Intersection(projet, poses, i, (i + projet.Faces.Count - 1) % projet.Faces.Count) ?? debut;
            }

            if (GenerateurStructure.RaccordeeApres(projet, i))
            {
                fin = Intersection(projet, poses, i, (i + 1) % projet.Faces.Count) ?? fin;
            }

            if (fin - debut > Cotes.Tolerance)
            {
                double d = face.Parametres.DistanceFacade;
                // Passage sous chaque poutre : ouverture dans le mur, recalculée à chaque dessin (refermée si la poutre est retirée).
                var ouvertures = GenerateurFacade.Passages(face)
                    .Select(p => (A: Math.Max(p.X0, debut), B: Math.Min(p.X1, fin), p.HauteurLibre))
                    .Where(p => p.B - p.A > Cotes.Tolerance)
                    .Select(p => new OuvertureMur(Cotes.Arrondi(p.A - debut), Cotes.Arrondi(p.B - debut), p.HauteurLibre))
                    .ToList();
                murs.Add(new MurFacade(face.Nom, Monde(poses[i], debut, d), Monde(poses[i], fin, d), face.HauteurTotale, ouvertures));
            }
        }

        return murs;
    }

    /// <summary>Abscisse, sur le mur de la face <paramref name="i"/>, du point où il coupe le mur de la face <paramref name="j"/>.</summary>
    private static double? Intersection(ProjetStructure projet, IReadOnlyList<PoseFace> poses, int i, int j)
    {
        var a = Monde(poses[i], 0, projet.Faces[i].Parametres.DistanceFacade);
        var b = Monde(poses[j], 0, projet.Faces[j].Parametres.DistanceFacade);
        var (ux, uy) = Direction(poses[i]);
        var (vx, vy) = Direction(poses[j]);
        double croix = ux * vy - uy * vx;
        if (Math.Abs(croix) < 1e-9)
        {
            return null;
        }

        double t = ((b.X - a.X) * vy - (b.Y - a.Y) * vx) / croix;
        return Cotes.Arrondi(t);
    }

    private static (double, double) Direction(PoseFace pose)
    {
        double a = pose.Angle * Math.PI / 180;
        return (Math.Round(Math.Cos(a), 12), Math.Round(Math.Sin(a), 12));
    }

    private static Point3 Monde(PoseFace pose, double x, double y)
    {
        var (c, s) = Direction(pose);
        return new Point3(Cotes.Arrondi(pose.Origine.X + x * c - y * s), Cotes.Arrondi(pose.Origine.Y + x * s + y * c), 0);
    }
}
