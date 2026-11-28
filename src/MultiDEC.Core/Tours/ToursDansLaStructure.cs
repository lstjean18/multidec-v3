using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MultiDEC.Core.Catalogue;
using MultiDEC.Core.Facade;
using MultiDEC.Core.Facade.Regles;
using MultiDEC.Core.Geometrie;

namespace MultiDEC.Core.TourEscalier;

/// <summary>
/// Place les tours escalier dans la structure du projet :
/// - tour seule : à sa position (X, Y) et son angle ;
/// - tour accolée à une face (Sn) : le long de la face, côté extérieur, son côté A à <see cref="TourEscalierAlu.Ecart"/>
///   de la file extérieure, son bout D à l'abscisse <see cref="TourEscalierAlu.Abscisse"/>. Elle garde ses poteaux
///   (Laurent, 28/11/2026 : la version à file commune est la maille « Accès escalier rapporté ») et elle est reliée à la
///   façade par des tubes et colliers tous les 4 m ; le garde-corps extérieur de la façade est ouvert devant ses sorties
///   du côté A.
/// </summary>
public static class ToursDansLaStructure
{
    /// <summary>Pose d'une tour : rotation autour de Z (degrés) et point du projet où tombe son origine.</summary>
    public static PoseFace? Pose(ProjetStructure projet, TourEscalierAlu tour, IReadOnlyList<PoseFace>? poses = null)
    {
        if (!tour.EstAccolee)
        {
            return new PoseFace(tour.Angle, new Point3(tour.PositionX, tour.PositionY, 0));
        }

        int i = IndexFace(projet, tour);
        if (i < 0)
        {
            return null;
        }

        poses ??= GenerateurStructure.Poses(projet);
        var face = projet.Faces[i];
        var local = new Point3(tour.Abscisse, Cotes.Arrondi(-face.Parametres.LargeurMaille - tour.Ecart), 0);
        var global = new ElementPlace(string.Empty, Famille.Poteau, local, Axe.Z).DansLaStructure(face.Nom, poses[i].Angle, poses[i].Origine).Origine;
        return new PoseFace(poses[i].Angle, global);
    }

    /// <summary>Indice de la face à laquelle la tour est accolée (-1 si elle est seule ou si la face n'existe pas).</summary>
    public static int IndexFace(ProjetStructure projet, TourEscalierAlu tour) =>
        !tour.EstAccolee || !projet.AvecFacade || projet.EstVolumique ? -1 : projet.Faces.FindIndex(f => f.Nom == tour.FaceAccolee);

    /// <summary>Génère chaque tour et l'ajoute aux éléments de la structure (nom d'ouvrage T1, T2… sur chaque élément).</summary>
    public static void Ajouter(ProjetStructure projet, IReadOnlyList<PoseFace> poses, List<ElementPlace> elements, List<Amarrage> amarrages, List<string> alertes)
    {
        foreach (var tour in projet.Tours)
        {
            bool accolee = IndexFace(projet, tour) >= 0;
            if (tour.EstAccolee && !accolee)
            {
                alertes.Add(projet.EstVolumique
                    ? $"{tour.Nom} : accolement à une structure volumique non prévu, tour placée seule."
                    : $"{tour.Nom} : la face {tour.FaceAccolee} n'existe pas, tour placée seule.");
            }

            var resultat = GenerateurTourEscalier.Generer(tour, projet.General, accolee);
            alertes.AddRange(resultat.Alertes.Select(a => tour.Nom + " : " + a));
            var pose = accolee ? Pose(projet, tour, poses)! : new PoseFace(tour.Angle, new Point3(tour.PositionX, tour.PositionY, 0));
            var locaux = resultat.Elements.ToList();
            if (accolee && resultat.Elements.Count > 0)
            {
                locaux.AddRange(Liaisons(projet, tour, alertes));
                VerifierSorties(projet, tour, alertes);
            }

            elements.AddRange(locaux.Select(e => e.DansLaStructure(tour.Nom, pose.Angle, pose.Origine)));
            amarrages.AddRange(resultat.Amarrages);
        }
    }

    /// <summary>
    /// Liaisons tour / façade : sur les deux poteaux du côté A, tous les 4 m depuis 2 m et jusqu'au dernier palier commun,
    /// un tube vers la façade et deux colliers fixes (à valider avec RETOTUB).
    /// </summary>
    private static IEnumerable<ElementPlace> Liaisons(ProjetStructure projet, TourEscalierAlu tour, List<string> alertes)
    {
        var face = projet.Faces[IndexFace(projet, tour)];
        double haut = Math.Min(tour.Hauteur, face.Parametres.HauteurDernierPlancher);
        string tube = References.Tube(References.LongueurTube(tour.Ecart + 0.30) ?? 0.70);
        int n = 0;
        for (double z = 2.0; z <= haut + Cotes.Tolerance; z = Cotes.Arrondi(z + 4.0))
        {
            foreach (double x in new[] { 0.0, TourEscalierAlu.Longueur })
            {
                n++;
                yield return new ElementPlace(tube, Famille.Amarrage, new Point3(x, 0, z), Axe.Y, Face.Interieure);
                yield return new ElementPlace(References.CollierFixe, Famille.Amarrage, new Point3(x, 0, z), Axe.Z, Face.Interieure, rotation: 90);
                yield return new ElementPlace(References.CollierFixe, Famille.Amarrage, new Point3(x, Cotes.Arrondi(tour.Ecart), z), Axe.Z, Face.Interieure, rotation: 90);
            }
        }

        if (n == 0)
        {
            alertes.Add($"{tour.Nom} : trop basse pour être liée à {face.Nom} (première liaison à 2 m), vérifier sa stabilité.");
        }
    }

    /// <summary>Les sorties du côté A d'une tour accolée doivent tomber sur un plancher de la face.</summary>
    private static void VerifierSorties(ProjetStructure projet, TourEscalierAlu tour, List<string> alertes)
    {
        var face = projet.Faces[IndexFace(projet, tour)];
        foreach (var s in tour.Sorties.Where(s => s.Cote == CoteTour.A))
        {
            if (!face.Niveaux.Any(n => Cotes.Egal(n.Hauteur, s.Hauteur)))
            {
                alertes.Add(string.Format(CultureInfo.GetCultureInfo("fr-FR"),
                    "{0} : sortie côté façade à {1:0.00} m, pas de plancher de {2} à cette hauteur.", tour.Nom, s.Hauteur, face.Nom));
            }
        }
    }

    /// <summary>
    /// Hauteurs des paliers d'une tour qui tombent sur un plancher de la face (sorties côté façade proposées à l'accolement).
    /// </summary>
    public static IReadOnlyList<double> PaliersCommuns(ProjetStructure projet, TourEscalierAlu tour)
    {
        int i = IndexFace(projet, tour);
        if (i < 0)
        {
            return Array.Empty<double>();
        }

        var niveaux = projet.Faces[i].Niveaux.Select(n => n.Hauteur).ToList();
        return tour.Paliers().Skip(1).Where(z => niveaux.Any(h => Cotes.Egal(h, z))).ToList();
    }

    /// <summary>
    /// Retire le garde-corps extérieur de la face (cadres, lisses au-dessus du plancher, plinthes) sur les mailles qui
    /// font face à une sortie du côté A d'une tour accolée, au palier de la sortie. Éléments dans le repère de la face.
    /// </summary>
    public static IReadOnlyList<ElementPlace> OuvrirDevantLesSorties(ProjetStructure projet, FaceEchafaudage face, IReadOnlyList<ElementPlace> elements, List<string> alertes)
    {
        var ouvertures = new List<(TourEscalierAlu Tour, double X0, double X1, double Z)>();
        foreach (var tour in projet.Tours.Where(t => t.FaceAccolee == face.Nom && IndexFace(projet, t) >= 0 && t.ErreurHauteur() == null))
        {
            var paliers = tour.Paliers();
            foreach (var s in tour.Sorties.Where(s => s.Cote == CoteTour.A))
            {
                int k = paliers.ToList().FindIndex(z => Cotes.Egal(z, s.Hauteur));
                if (k <= 0)
                {
                    continue;
                }

                double o = tour.LargeurOuverture(s);
                bool versB = tour.BoutArrivee(k) == CoteTour.B;
                double x0 = tour.Abscisse + (versB ? TourEscalierAlu.Longueur - o : 0);
                ouvertures.Add((tour, Cotes.Arrondi(x0), Cotes.Arrondi(x0 + o), s.Hauteur));
            }
        }

        if (ouvertures.Count == 0)
        {
            return elements;
        }

        var xs = face.AbscissesFiles();
        double yExt = -face.Parametres.LargeurMaille;
        var retires = new HashSet<ElementPlace>();
        foreach (var (tour, x0, x1, z) in ouvertures)
        {
            var mailles = Enumerable.Range(1, Math.Max(0, xs.Count - 1))
                .Where(num => xs[num - 1] < x1 - Cotes.Tolerance && xs[num] > x0 + Cotes.Tolerance)
                .ToList();
            foreach (var e in elements.Where(e => mailles.Contains(e.NumeroMaille) && e.Axe == Axe.X && Math.Abs(e.Origine.Y - yExt) < 0.10))
            {
                bool gardeCorps = (e.Famille == Famille.GardeCorps || e.Famille == Famille.Plinthe) && Cotes.Egal(e.Origine.Z, z);
                bool lisse = e.Famille == Famille.Lisse && e.Origine.Z > z + Cotes.Tolerance && e.Origine.Z <= z + 1.5 + Cotes.Tolerance;
                if (gardeCorps || lisse)
                {
                    retires.Add(e);
                }
            }

            foreach (int num in mailles)
            {
                if (xs[num - 1] < tour.Abscisse - Cotes.Tolerance || xs[num] > tour.Abscisse + TourEscalierAlu.Longueur + Cotes.Tolerance)
                {
                    alertes.Add(string.Format(CultureInfo.GetCultureInfo("fr-FR"),
                        "{0} : garde-corps extérieur de {1} retiré sur la maille {2} à {3:0.00} m (sortie de {4}) ; la maille dépasse la tour, protection à compléter.",
                        face.Nom, face.Nom, num, z, tour.Nom));
                }
            }
        }

        return elements.Where(e => !retires.Contains(e)).ToList();
    }
}
