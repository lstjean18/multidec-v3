using System;
using System.Collections.Generic;
using System.Linq;
using MultiDEC.Core.Facade.Regles;
using MultiDEC.Core.Geometrie;

namespace MultiDEC.Core.Facade;

/// <summary>Position d'une face dans la structure : rotation autour de Z et point où tombe son origine.</summary>
public sealed class PoseFace
{
    public PoseFace(double angle, Point3 origine)
    {
        Angle = angle;
        Origine = origine;
    }

    /// <summary>Degrés : 0 pour S1, puis +90 à chaque angle (le bâtiment reste à gauche en parcourant les faces).</summary>
    public double Angle { get; }

    public Point3 Origine { get; }
}

/// <summary>
/// Génère toute la structure : chaque face par <see cref="GenerateurFacade"/>, puis mise en place des faces
/// en équerre selon la forme (en L, en U, fermée).
/// Convention de jonction (comme la v2) : à partir de S2, la 1re maille d'une face est le carré d'angle,
/// de longueur = largeur de maille, posé au bout de la face précédente côté extérieur.
/// La face suivante tourne de +90° : le bâtiment (Y positifs) reste à gauche quand on parcourt S1, S2…
/// </summary>
public static class GenerateurStructure
{
    public static StructureFacade Generer(ProjetStructure projet)
    {
        projet.AppliquerGeneraux();
        var poses = Poses(projet);
        var elements = new List<ElementPlace>();
        var amarrages = new List<Amarrage>();
        var alertes = new List<string>();
        double surface = 0;
        int mini = 0;
        bool note = false;
        bool plusieurs = projet.Faces.Count > 1;

        for (int i = 0; i < projet.Faces.Count; i++)
        {
            var face = projet.Faces[i];
            if (face.Mailles.Count == 0)
            {
                alertes.Add($"{face.Nom} : grille des mailles vide, face ignorée.");
                continue;
            }

            var p = face.Parametres;
            bool aboutGauche = p.AboutGauche;
            bool aboutDroit = p.AboutDroit;
            if (projet.EstVolumique)
            {
                p.ModuleVolumique = i;
                p.NombreModules = projet.Faces.Count;
                p.PlanchersDamier = projet.PlanchersDamier;
                if (i > 0 && !MemesMailles(face, projet.S1))
                {
                    alertes.Add($"{face.Nom} : mailles différentes de M1, les modules ne tombent pas sur les mêmes files.");
                }
            }
            else if (plusieurs)
            {
                // Pas d'about là où la face se raccorde à la précédente ou à la suivante.
                p.AboutGauche = aboutGauche && !RaccordeeAvant(projet, i);
                p.AboutDroit = aboutDroit && !RaccordeeApres(projet, i);
            }

            StructureFacade s;
            try
            {
                s = GenerateurFacade.Generer(face);
            }
            finally
            {
                p.AboutGauche = aboutGauche;
                p.AboutDroit = aboutDroit;
                p.ModuleVolumique = null;
                p.NombreModules = 1;
                p.PlanchersDamier = false;
            }

            var pose = poses[i];
            var places = s.Elements.Select(e => e.DansLaStructure(face.Nom, pose.Angle, pose.Origine)).ToList();
            if (i > 0)
            {
                places = SansPoteauxCommuns(places, elements);
                if (projet.EstVolumique)
                {
                    places = SansDoublons(places, elements);
                }
            }

            elements.AddRange(places);
            amarrages.AddRange(s.Amarrages.Amarrages);
            surface += s.Amarrages.Surface;
            mini += s.Amarrages.MinimumParSurface;
            note |= s.Amarrages.NoteDeCalculObligatoire;
            alertes.AddRange(plusieurs ? s.Alertes.Select(a => face.Nom + " : " + a) : s.Alertes);
        }

        if (projet.Forme == FormeStructure.Fermee && projet.Faces.Count == 4)
        {
            var f = projet.Faces;
            if (!Cotes.Egal(f[0].LongueurObtenue, f[2].LongueurObtenue) || !Cotes.Egal(f[1].LongueurObtenue, f[3].LongueurObtenue))
            {
                alertes.Add(FormattableString.Invariant(
                    $"Structure fermée : S1 ({f[0].LongueurObtenue:0.00} m) doit égaler S3 ({f[2].LongueurObtenue:0.00} m) et S2 ({f[1].LongueurObtenue:0.00} m) égaler S4 ({f[3].LongueurObtenue:0.00} m), sinon la structure ne se referme pas."));
            }
        }

        return new StructureFacade(elements, new BilanAmarrages(amarrages, Cotes.Arrondi(surface), mini, note), alertes.Distinct().ToList());
    }

    /// <summary>Position de chaque face : S1 à l'origine, chaque face suivante au bout de la précédente, tournée de +90°.</summary>
    public static IReadOnlyList<PoseFace> Poses(ProjetStructure projet)
    {
        var poses = new List<PoseFace>();
        if (projet.EstVolumique)
        {
            // Modules côte à côte en profondeur : M1 contre la façade, chacun derrière le précédent.
            double y = 0;
            foreach (var f in projet.Faces)
            {
                poses.Add(new PoseFace(0, new Point3(0, Cotes.Arrondi(y), 0)));
                y -= f.Parametres.LargeurMaille;
            }

            return poses;
        }

        double angle = 0;
        var origine = new Point3(0, 0, 0);
        for (int i = 0; i < projet.Faces.Count; i++)
        {
            if (i > 0)
            {
                // Bout de la face précédente, sur sa file extérieure (Y local = -largeur) : origine de la suivante.
                var prec = projet.Faces[i - 1];
                var bout = new ElementPlace(string.Empty, Famille.Poteau, new Point3(prec.LongueurObtenue, -prec.Parametres.LargeurMaille, 0), Axe.Z)
                    .DansLaStructure(prec.Nom, angle, origine).Origine;
                angle += 90;
                origine = new Point3(bout.X, bout.Y, 0);
            }

            poses.Add(new PoseFace(angle % 360, origine));
        }

        return poses;
    }

    /// <summary>La face <paramref name="i"/> se raccorde à la précédente (ou à S4 pour S1 d'une structure fermée).</summary>
    public static bool RaccordeeAvant(ProjetStructure projet, int i) =>
        !projet.EstVolumique && (i > 0 || (projet.Forme == FormeStructure.Fermee && projet.Faces.Count == 4));

    /// <summary>La face <paramref name="i"/> se raccorde à la suivante (ou à S1 pour S4 d'une structure fermée).</summary>
    public static bool RaccordeeApres(ProjetStructure projet, int i) =>
        !projet.EstVolumique && (i < projet.Faces.Count - 1 || (projet.Forme == FormeStructure.Fermee && projet.Faces.Count == 4));

    private static bool MemesMailles(FaceEchafaudage a, FaceEchafaudage b) =>
        a.Mailles.Count == b.Mailles.Count && a.Mailles.Zip(b.Mailles, (x, y) => Cotes.Egal(x.Longueur, y.Longueur)).All(ok => ok);

    /// <summary>Volumique : retire les pièces identiques déjà posées par le module voisin (lisses sur la file commune…).</summary>
    private static List<ElementPlace> SansDoublons(List<ElementPlace> face, List<ElementPlace> dejaPoses)
    {
        var poses = new HashSet<(string, double, double, double, Axe)>(dejaPoses.Select(CleComplete));
        return face.Where(e => !poses.Contains(CleComplete(e))).ToList();
    }

    private static (string, double, double, double, Axe) CleComplete(ElementPlace e) =>
        (e.Reference, Math.Round(e.Origine.X, 3), Math.Round(e.Origine.Y, 3), Math.Round(e.Origine.Z, 3), e.Axe);

    /// <summary>
    /// Retire d'une face les pièces verticales (calage, pied, poteaux…) d'une file déjà posée par une face
    /// précédente au même endroit : les poteaux d'angle sont communs.
    /// </summary>
    private static List<ElementPlace> SansPoteauxCommuns(List<ElementPlace> face, List<ElementPlace> dejaPoses)
    {
        var occupes = new HashSet<(double, double)>(dejaPoses.Where(EstVertical).Select(Cle));
        return face.Where(e => !(EstVertical(e) && occupes.Contains(Cle(e)))).ToList();
    }

    private static bool EstVertical(ElementPlace e) => e.Axe == Axe.Z && e.Famille switch
    {
        Famille.Calage or Famille.Socle or Famille.Embase or Famille.Poteau or Famille.Goupille => true,
        _ => false,
    };

    private static (double, double) Cle(ElementPlace e) => (Math.Round(e.Origine.X, 3), Math.Round(e.Origine.Y, 3));
}
