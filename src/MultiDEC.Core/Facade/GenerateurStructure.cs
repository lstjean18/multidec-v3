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
        if (projet.SansOuvrage)
        {
            throw new InvalidOperationException("Le projet ne contient aucun ouvrage : ajoutez une façade ou une tour (ruban « Ouvrages »).");
        }

        if (projet.AvecFacade)
        {
            VerifierPremiersPlanchers(projet);
            RegleTourRoulante.Verifier(projet);
            RegleMaillesAcces.Verifier(projet);
        }

        var poses = Poses(projet);
        var elements = new List<ElementPlace>();
        var amarrages = new List<Amarrage>();
        var alertes = new List<string>();
        double surface = 0;
        int mini = 0;
        bool note = false;
        bool plusieurs = projet.Faces.Count > 1;

        for (int i = 0; i < projet.FacesGenerees.Count; i++)
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
                p.LongueurModulePrecedent = i > 0 ? projet.Faces[i - 1].LongueurObtenue : 0;
                p.LongueurModuleSuivant = i < projet.Faces.Count - 1 ? projet.Faces[i + 1].LongueurObtenue : 0;
                if (i > 0 && !FilesCommunes(face, projet.Faces[i - 1]))
                {
                    alertes.Add($"{face.Nom} : sur la longueur commune, les mailles ne tombent pas sur les files de {projet.Faces[i - 1].Nom}.");
                }
            }
            else if (plusieurs)
            {
                // Raccords (Laurent, 24/11/2026) : le bout de la face qui touche le carré d'angle de la suivante n'a pas
                // d'about (passage) ; le côté extérieur du carré d'angle donne dans le vide, il a toujours son about.
                p.CarreDAngleEnTete = projet.CommenceParCarreDAngle(i);
                p.CarreDAngleInverse = p.CarreDAngleEnTete && projet.FacesInversees;

                // Structure inversée : le bout de départ du carré d'angle donne sur le mur, pas d'about.
                p.AboutGauche = p.CarreDAngleInverse ? false : aboutGauche || p.CarreDAngleEnTete;
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
                p.CarreDAngleEnTete = false;
                p.CarreDAngleInverse = false;
                p.ModuleVolumique = null;
                p.NombreModules = 1;
                p.PlanchersDamier = false;
                p.LongueurModulePrecedent = 0;
                p.LongueurModuleSuivant = 0;
            }

            var pose = poses[i];
            // Tours accolées : garde-corps extérieur ouvert devant leurs sorties (repère de la face).
            var locaux = TourEscalier.ToursDansLaStructure.OuvrirDevantLesSorties(projet, face, s.Elements, alertes);
            var places = locaux.Select(e => e.DansLaStructure(face.Nom, pose.Angle, pose.Origine)).ToList();
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

        TourEscalier.ToursDansLaStructure.Ajouter(projet, poses, elements, amarrages, alertes);

        if (projet.AvecFacade && !projet.EstVolumique && projet.Faces.Any(f => f.Parametres.LargeurMaille > 1.05 + Cotes.Tolerance))
        {
            alertes.Add("Largeur de maille supérieure à 1,05 m : réservée aux structures volumiques.");
        }

        if (projet.AvecFacade && projet.Forme == FormeStructure.Fermee && projet.Faces.Count == 4)
        {
            var f = projet.Faces;
            if (!Cotes.Egal(f[0].LongueurObtenue, f[2].LongueurObtenue) || !Cotes.Egal(f[1].LongueurObtenue, f[3].LongueurObtenue))
            {
                alertes.Add(FormattableString.Invariant(
                    $"Structure fermée : S1 ({f[0].LongueurObtenue:0.00} m) doit égaler S3 ({f[2].LongueurObtenue:0.00} m) et S2 ({f[1].LongueurObtenue:0.00} m) égaler S4 ({f[3].LongueurObtenue:0.00} m), sinon la structure ne se referme pas."));
            }
        }

        if (projet.PiecesSupprimees.Count > 0)
        {
            int orphelines = PieceSupprimee.Retirer(elements, projet.PiecesSupprimees);
            alertes.Add(orphelines == 0
                ? $"{projet.PiecesSupprimees.Count} pièce(s) supprimée(s) à la main dans le dessin (clic droit > Rétablir les pièces supprimées pour les remettre)."
                : $"{projet.PiecesSupprimees.Count} pièce(s) supprimée(s) à la main, dont {orphelines} qui ne correspondent plus à la structure (paramètres modifiés depuis) : ignorée(s).");
        }

        return new StructureFacade(elements, new BilanAmarrages(amarrages, Cotes.Arrondi(surface), mini, note), alertes.Distinct().ToList());
    }

    /// <summary>
    /// Toutes les faces doivent avoir leur premier plancher à la même hauteur, sinon on ne passe pas de l'une
    /// à l'autre (Laurent, 24/11/2026) : message et refus de dessiner.
    /// </summary>
    public static void VerifierPremiersPlanchers(ProjetStructure projet)
    {
        if (projet.Faces.Count < 2)
        {
            return;
        }

        var premiers = projet.Faces
            .Select(f => (f.Nom, Premier: f.Niveaux.Where(n => n.Hauteur > Cotes.Tolerance).Select(n => n.Hauteur).DefaultIfEmpty(0).Min()))
            .ToList();
        double reference = premiers[0].Premier;
        var ecarts = premiers.Where(f => !Cotes.Egal(f.Premier, reference)).ToList();
        if (ecarts.Count == 0)
        {
            return;
        }

        string detail = string.Join(", ", premiers.Select(f => FormattableString.Invariant($"{f.Nom} {f.Premier:0.00} m")));
        throw new InvalidOperationException(
            "Les faces n'ont pas leur premier plancher à la même hauteur (" + detail.Replace('.', ',') + ") : on ne pourrait pas passer d'une face à l'autre. " +
            "Ajustez la hauteur du dernier plancher (écart multiple de 2 m) ou les niveaux des faces concernées ; la structure n'est pas dessinée.");
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
        bool inversee = projet.FacesInversees;
        for (int i = 0; i < projet.Faces.Count; i++)
        {
            if (i > 0)
            {
                // Bout de la face précédente : sur sa file extérieure (Y local = -largeur) quand on tourne à gauche ;
                // structure inversée (on tourne à droite) : sur sa file intérieure, prolongée de la largeur de la face
                // (la file intérieure de la face suivante est contre le mur en retour).
                var prec = projet.Faces[i - 1];
                var pointLocal = inversee
                    ? new Point3(prec.LongueurObtenue + projet.Faces[i].Parametres.LargeurMaille, 0, 0)
                    : new Point3(prec.LongueurObtenue, -prec.Parametres.LargeurMaille, 0);
                var bout = new ElementPlace(string.Empty, Famille.Poteau, pointLocal, Axe.Z)
                    .DansLaStructure(prec.Nom, angle, origine).Origine;
                angle += inversee ? -90 : 90;
                origine = new Point3(bout.X, bout.Y, 0);
            }

            poses.Add(new PoseFace(((angle % 360) + 360) % 360, origine));
        }

        return poses;
    }

    /// <summary>La face <paramref name="i"/> se raccorde à la précédente (ou à S4 pour S1 d'une structure fermée).</summary>
    public static bool RaccordeeAvant(ProjetStructure projet, int i) =>
        !projet.EstVolumique && (i > 0 || (projet.Forme == FormeStructure.Fermee && projet.Faces.Count == 4));

    /// <summary>La face <paramref name="i"/> se raccorde à la suivante (ou à S1 pour S4 d'une structure fermée).</summary>
    public static bool RaccordeeApres(ProjetStructure projet, int i) =>
        !projet.EstVolumique && (i < projet.Faces.Count - 1 || (projet.Forme == FormeStructure.Fermee && projet.Faces.Count == 4));

    /// <summary>Volumique : sur la longueur commune aux deux modules, mêmes files (le plus long peut continuer au-delà).</summary>
    private static bool FilesCommunes(FaceEchafaudage a, FaceEchafaudage b)
    {
        double commun = Math.Min(a.LongueurObtenue, b.LongueurObtenue);
        var fa = a.AbscissesFiles().Where(x => x <= commun + Cotes.Tolerance).Select(Cotes.Arrondi);
        var fb = b.AbscissesFiles().Where(x => x <= commun + Cotes.Tolerance).Select(Cotes.Arrondi);
        return fa.SequenceEqual(fb);
    }

    /// <summary>Volumique : retire les pièces identiques déjà posées par le module voisin (lisses sur la file commune…).</summary>
    private static List<ElementPlace> SansDoublons(List<ElementPlace> face, List<ElementPlace> dejaPoses)
    {
        var poses = new HashSet<(string, double, double, double, Axe)>(dejaPoses.Select(CleComplete));
        var resultat = face.Where(e => !poses.Contains(CleComplete(e))).ToList();

        // Lisse commune posée par les deux modules avec deux références (l'une renforcée car elle porte des plateaux
        // en damier) : on garde la renforcée.
        var lissesDeja = dejaPoses.Where(EstLisse).GroupBy(ClePosition).ToDictionary(g => g.Key, g => g.First());
        var retirees = new List<ElementPlace>();
        foreach (var e in resultat.Where(EstLisse).ToList())
        {
            if (!lissesDeja.TryGetValue(ClePosition(e), out var deja))
            {
                continue;
            }

            if (Renforcee(e) && !Renforcee(deja))
            {
                dejaPoses.Remove(deja);
            }
            else
            {
                retirees.Add(e);
            }
        }

        return resultat.Except(retirees).ToList();
    }

    private static bool EstLisse(ElementPlace e) =>
        (e.Famille == Famille.Lisse || e.Famille == Famille.Traverse) && e.Axe != Axe.Z;

    private static bool Renforcee(ElementPlace e) =>
        e.Reference.StartsWith("HMLISR", StringComparison.Ordinal) || (e.Reference.StartsWith("HMTRA", StringComparison.Ordinal) && e.Reference.EndsWith("R", StringComparison.Ordinal));

    private static (double, double, double, Axe) ClePosition(ElementPlace e) =>
        (Math.Round(e.Origine.X, 3), Math.Round(e.Origine.Y, 3), Math.Round(e.Origine.Z, 3), e.Axe);

    private static (string, double, double, double, Axe) CleComplete(ElementPlace e) =>
        (e.Reference, Math.Round(e.Origine.X, 3), Math.Round(e.Origine.Y, 3), Math.Round(e.Origine.Z, 3), e.Axe);

    /// <summary>
    /// Retire d'une face les pièces verticales (calage, pied, poteaux…) d'une file déjà posée par une face
    /// précédente au même endroit : les poteaux d'angle sont communs.
    /// </summary>
    private static List<ElementPlace> SansPoteauxCommuns(List<ElementPlace> face, List<ElementPlace> dejaPoses)
    {
        // Poteau d'angle commun : on garde la pile qui monte le plus haut (garde-corps accroché d'un seul côté).
        var hautDeja = dejaPoses.Where(EstVertical).GroupBy(Cle).ToDictionary(g => g.Key, g => g.Max(e => e.Origine.Z));
        var hautFace = face.Where(EstVertical).GroupBy(Cle).ToDictionary(g => g.Key, g => g.Max(e => e.Origine.Z));
        var remplaces = new HashSet<(double, double)>(hautFace.Where(kv => hautDeja.TryGetValue(kv.Key, out double z) && kv.Value > z + Cotes.Tolerance).Select(kv => kv.Key));
        if (remplaces.Count > 0)
        {
            dejaPoses.RemoveAll(e => EstVertical(e) && remplaces.Contains(Cle(e)));
        }

        return face.Where(e => !(EstVertical(e) && hautDeja.ContainsKey(Cle(e)) && !remplaces.Contains(Cle(e)))).ToList();
    }

    private static bool EstVertical(ElementPlace e) => e.Axe == Axe.Z && e.Famille switch
    {
        Famille.Calage or Famille.Socle or Famille.Embase or Famille.Poteau or Famille.Goupille => true,
        _ => false,
    };

    private static (double, double) Cle(ElementPlace e) => (Math.Round(e.Origine.X, 3), Math.Round(e.Origine.Y, 3));
}
