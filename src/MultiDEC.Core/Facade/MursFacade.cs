using System;
using System.Collections.Generic;
using System.Linq;
using MultiDEC.Core.Catalogue;
using MultiDEC.Core.Geometrie;

namespace MultiDEC.Core.Facade;

/// <summary>Mur du bâtiment devant une face : segment au sol (repère de la structure) et hauteur.</summary>
public sealed class MurFacade
{
    public MurFacade(string nomFace, Point3 debut, Point3 fin, double hauteur, IReadOnlyList<OuvertureMur>? ouvertures = null,
        IReadOnlyList<RenfoncementMur>? renfoncements = null)
    {
        NomFace = nomFace;
        Debut = debut;
        Fin = fin;
        Hauteur = hauteur;
        Ouvertures = ouvertures ?? Array.Empty<OuvertureMur>();
        Renfoncements = renfoncements ?? Array.Empty<RenfoncementMur>();
    }

    /// <summary>Passages laissés sous les poutres de franchissement (distances comptées depuis <see cref="Debut"/>).</summary>
    public IReadOnlyList<OuvertureMur> Ouvertures { get; }

    /// <summary>Renfoncements derrière les consoles (le mur recule de la largeur de la console, Laurent 25/11/2026).</summary>
    public IReadOnlyList<RenfoncementMur> Renfoncements { get; }

    /// <summary>
    /// Blocs pleins du mur : le mur est découpé en colonnes (bords des ouvertures et des renfoncements) ; dans chaque
    /// colonne, pans pleins au-dessus des ouvertures, reculés là où il y a un renfoncement, et retours en travers
    /// entre deux colonnes qui n'ont pas le même recul.
    /// </summary>
    public IReadOnlyList<BlocMur> Blocs()
    {
        var bords = new SortedSet<double> { 0, Cotes.Arrondi(Longueur) };
        foreach (var o in Ouvertures)
        {
            bords.Add(Cotes.Arrondi(Math.Max(0, Math.Min(Longueur, o.Debut))));
            bords.Add(Cotes.Arrondi(Math.Max(0, Math.Min(Longueur, o.Fin))));
        }

        foreach (var r in Renfoncements)
        {
            bords.Add(Cotes.Arrondi(Math.Max(0, Math.Min(Longueur, r.Debut))));
            bords.Add(Cotes.Arrondi(Math.Max(0, Math.Min(Longueur, r.Fin))));
        }

        var b = bords.ToList();
        var blocs = new List<BlocMur>();
        List<(double Z0, double Z1, double Recul)>? precedent = null;
        for (int k = 0; k + 1 < b.Count; k++)
        {
            double s0 = b[k], s1 = b[k + 1];
            if (s1 - s0 <= Cotes.Tolerance)
            {
                continue;
            }

            var profil = Profil((s0 + s1) / 2);
            foreach (var (z0, z1, recul) in profil)
            {
                blocs.Add(new BlocMur(s0, Cotes.Arrondi(s1 - s0), z0, Cotes.Arrondi(z1 - z0), recul));
            }

            // Retours en travers à la limite gauche de la colonne : là où le recul change.
            if (precedent != null)
            {
                blocs.AddRange(Retours(s0, precedent, profil));
            }

            precedent = profil;
        }

        return blocs;
    }

    /// <summary>Tranches pleines de la colonne à l'abscisse <paramref name="s"/> : (bas, haut, recul).</summary>
    private List<(double Z0, double Z1, double Recul)> Profil(double s)
    {
        double bas = Ouvertures.Where(o => o.Debut < s && s < o.Fin).Select(o => o.Hauteur).DefaultIfEmpty(0).Max();
        var coupes = new SortedSet<double> { Cotes.Arrondi(bas), Cotes.Arrondi(Hauteur) };
        var ici = Renfoncements.Where(r => r.Debut < s && s < r.Fin).ToList();
        foreach (var r in ici)
        {
            coupes.Add(Cotes.Arrondi(Math.Max(bas, Math.Min(Hauteur, r.Z0))));
            coupes.Add(Cotes.Arrondi(Math.Max(bas, Math.Min(Hauteur, r.Z1))));
        }

        var c = coupes.Where(z => z >= bas - Cotes.Tolerance && z <= Hauteur + Cotes.Tolerance).ToList();
        var tranches = new List<(double, double, double)>();
        for (int k = 0; k + 1 < c.Count; k++)
        {
            double z0 = c[k], z1 = c[k + 1];
            if (z1 - z0 <= Cotes.Tolerance)
            {
                continue;
            }

            double zm = (z0 + z1) / 2;
            double recul = ici.Where(r => r.Z0 < zm && zm < r.Z1).Select(r => r.Recul).DefaultIfEmpty(0).Max();

            // Tranches voisines de même recul fusionnées.
            if (tranches.Count > 0 && Cotes.Egal(tranches[tranches.Count - 1].Item3, recul) && Cotes.Egal(tranches[tranches.Count - 1].Item2, z0))
            {
                tranches[tranches.Count - 1] = (tranches[tranches.Count - 1].Item1, z1, recul);
            }
            else
            {
                tranches.Add((z0, z1, recul));
            }
        }

        return tranches;
    }

    /// <summary>Retours en travers à l'abscisse <paramref name="s"/> entre deux colonnes de reculs différents.</summary>
    private static IEnumerable<BlocMur> Retours(double s, List<(double Z0, double Z1, double Recul)> gauche, List<(double Z0, double Z1, double Recul)> droite)
    {
        var coupes = new SortedSet<double>(gauche.SelectMany(t => new[] { t.Z0, t.Z1 }).Concat(droite.SelectMany(t => new[] { t.Z0, t.Z1 })));
        var c = coupes.ToList();
        for (int k = 0; k + 1 < c.Count; k++)
        {
            double zm = (c[k] + c[k + 1]) / 2;
            double? rg = gauche.Where(t => t.Z0 < zm && zm < t.Z1).Select(t => (double?)t.Recul).FirstOrDefault();
            double? rd = droite.Where(t => t.Z0 < zm && zm < t.Z1).Select(t => (double?)t.Recul).FirstOrDefault();
            if (rg is double a && rd is double b2 && !Cotes.Egal(a, b2))
            {
                double r0 = Math.Min(a, b2), r1 = Math.Max(a, b2);
                // Épaisseur du retour du côté le moins reculé (hors du renfoncement).
                yield return new BlocMur(s, Cotes.Arrondi(r1 - r0), c[k], Cotes.Arrondi(c[k + 1] - c[k]), r0, enTravers: true, epaisseurVersDebut: a < b2);
            }
        }
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

/// <summary>
/// Renfoncement du mur derrière une maille à consoles : de <see cref="Debut"/> à <see cref="Fin"/> le long du mur,
/// de <see cref="Z0"/> à <see cref="Z1"/>, le mur recule de <see cref="Recul"/> vers le bâtiment.
/// </summary>
public readonly struct RenfoncementMur
{
    public RenfoncementMur(double debut, double fin, double z0, double z1, double recul)
    {
        Debut = debut;
        Fin = fin;
        Z0 = z0;
        Z1 = z1;
        Recul = recul;
    }

    public double Debut { get; }

    public double Fin { get; }

    public double Z0 { get; }

    public double Z1 { get; }

    public double Recul { get; }

    public override string ToString() => FormattableString.Invariant($"{Debut:0.##}–{Fin:0.##} z {Z0:0.##}–{Z1:0.##} recul {Recul:0.##}");
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

/// <summary>
/// Bloc plein d'un mur : position le long du mur, longueur, cote du bas, hauteur et recul vers le bâtiment.
/// Bloc en travers (retour d'un renfoncement) : posé perpendiculairement au mur à <see cref="Position"/>, de
/// <see cref="Recul"/> à <see cref="Recul"/> + <see cref="Longueur"/> vers le bâtiment.
/// </summary>
public readonly struct BlocMur
{
    public BlocMur(double position, double longueur, double z, double hauteur, double recul = 0, bool enTravers = false, bool epaisseurVersDebut = true)
    {
        EpaisseurVersDebut = epaisseurVersDebut;
        Position = position;
        Longueur = longueur;
        Z = z;
        Hauteur = hauteur;
        Recul = recul;
        EnTravers = enTravers;
    }

    public double Position { get; }

    public double Longueur { get; }

    public double Z { get; }

    public double Hauteur { get; }

    public double Recul { get; }

    public bool EnTravers { get; }

    /// <summary>Retour en travers : épaisseur du côté de <see cref="MurFacade.Debut"/> (sinon du côté de la fin).</summary>
    public bool EpaisseurVersDebut { get; }

    public override string ToString() =>
        FormattableString.Invariant($"{(EnTravers ? "retour " : string.Empty)}{Position:0.##}+{Longueur:0.##} z {Z:0.##}+{Hauteur:0.##} recul {Recul:0.##}");
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
                var renfoncements = Renfoncements(face)
                    .Select(r => (A: Math.Max(r.X0, debut), B: Math.Min(r.X1, fin), r.Z0, r.Z1, r.Recul))
                    .Where(r => r.B - r.A > Cotes.Tolerance)
                    .Select(r => new RenfoncementMur(Cotes.Arrondi(r.A - debut), Cotes.Arrondi(r.B - debut), r.Z0, r.Z1, r.Recul))
                    .ToList();
                murs.Add(new MurFacade(face.Nom, Monde(poses[i], debut, d), Monde(poses[i], fin, d), face.HauteurTotale, ouvertures, renfoncements));
            }
        }

        return murs;
    }

    /// <summary>
    /// Renfoncements derrière les mailles à consoles (Laurent 25/11/2026) : une console ne traverse pas le mur, le mur
    /// suit l'échafaudage côté mur en restant à la distance à la façade du bout de la console. Consoles à tous les niveaux
    /// de la maille : renfoncement sur toute la hauteur ; sinon de 0,10 m sous le point le plus bas de la console à 2 m
    /// au-dessus de son plancher. Abscisses de la face.
    /// </summary>
    public static IReadOnlyList<(double X0, double X1, double Z0, double Z1, double Recul)> Renfoncements(FaceEchafaudage face)
    {
        var res = new List<(double, double, double, double, double)>();
        var p = face.Parametres;
        double h = p.HauteurDernierPlancher;
        double hauteurMur = face.HauteurTotale;
        var x = face.AbscissesFiles();
        for (int j = 0; j < face.Mailles.Count && j + 1 < x.Count; j++)
        {
            var m = face.Mailles[j];
            double hm = m.HauteurMax(h);
            var niveaux = face.Niveaux.Where(n => n.Type == TypePlancher.Complet && n.Hauteur > Cotes.Tolerance && n.Hauteur <= hm + Cotes.Tolerance).ToList();
            var consoles = new List<(double Z, ConsoleInfo Info)>();
            foreach (var n in niveaux)
            {
                string? reference = face.ConsoleDe(m, n);
                if (reference != null && CatalogueConsoles.TryGet(p.Modele, reference, out var info))
                {
                    consoles.Add((n.Hauteur, info));
                }
            }

            if (consoles.Count == 0)
            {
                continue;
            }

            double recul = consoles.Max(c => c.Info.Largeur);
            if (consoles.Count == niveaux.Count)
            {
                res.Add((x[j], x[j + 1], 0, hauteurMur, recul));
                continue;
            }

            foreach (var (z, info) in consoles)
            {
                double z0 = Math.Max(0, Cotes.Arrondi(z - info.HauteurSousPlancher - 0.10));
                double z1 = Math.Min(hauteurMur, Cotes.Arrondi(z + 2.0));
                res.Add((x[j], x[j + 1], z0, z1, info.Largeur));
            }
        }

        return res;
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
