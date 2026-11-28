using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MultiDEC.Core.Catalogue;
using MultiDEC.Core.Geometrie;

namespace MultiDEC.Core.Facade.Regles;

/// <summary>
/// Socles à roue = tour roulante (règles de la v2 version2024, CheckRoulant, précisées par Laurent le 25/11/2026) :
/// structure droite à 4 pieds (une seule maille, un seul module), largeur de 1,00 m au moins, hauteur du dernier plancher
/// et longueur mini selon la largeur. Grutable : réservé aux structures à 4 pieds. Hors règles : message et refus de dessiner.
/// </summary>
public static class RegleTourRoulante
{
    /// <summary>Largeur → (dernier plancher maxi, longueur mini). 1,05 m suit la règle de 1,00 m.</summary>
    private static readonly (double Largeur, double HauteurMax, double LongueurMin)[] Limites =
    {
        (1.00, 2.0, 1.50), (1.05, 2.0, 1.50), (1.50, 4.0, 1.50), (2.00, 6.0, 2.00), (2.50, 8.0, 2.50), (3.00, 10.0, 3.00),
    };

    /// <summary>Rappel des règles, affiché avec les écarts.</summary>
    public const string Rappel =
        "Règles : structure droite à 4 pieds (une maille, un module), largeur 1,00 m au moins ; dernier plancher maxi / longueur mini : " +
        "1,00 et 1,05 m → 2 m / 1,50 m ; 1,50 m → 4 m / 1,50 m ; 2,00 m → 6 m / 2,00 m ; 2,50 m → 8 m / 2,50 m ; 3,00 m → 10 m / 3,00 m. " +
        "L'option Grutable est réservée aux structures à 4 pieds.";

    public static bool EstRoulante(ProjetStructure projet) => CatalogueSocles.De(projet.General.Socle).AvecRoues;

    /// <summary>Écarts aux règles (liste vide si tout va bien).</summary>
    public static List<string> Ecarts(ProjetStructure projet)
    {
        var ecarts = new List<string>();
        var faces = projet.Faces.Where(f => f.Mailles.Count > 0).ToList();
        bool quatrePieds = faces.Count == 1 && faces[0].Mailles.Count == 1;
        bool grutable = projet.Faces.Any(f => f.Parametres.Grutable);
        if (grutable && !quatrePieds)
        {
            ecarts.Add("Grutable : la structure n'est pas à 4 pieds (une seule maille).");
        }

        if (!EstRoulante(projet))
        {
            return ecarts;
        }

        if (projet.Forme != FormeStructure.Droite && projet.Forme != FormeStructure.Volumique)
        {
            ecarts.Add("Socles à roue : réservés aux structures droites (ni L, ni U, ni fermée).");
        }

        if (!quatrePieds)
        {
            int mailles = faces.Sum(f => f.Mailles.Count);
            ecarts.Add(string.Format(CultureNombres.Documents,
                "Socles à roue : seulement sur une structure à 4 pieds ; ici {0} maille(s) sur {1} face(s) ou module(s).", mailles, faces.Count));
        }

        foreach (var f in faces)
        {
            var p = f.Parametres;
            double l = p.LargeurMaille;
            double h = f.Mailles.Select(m => m.HauteurMax(p.HauteurDernierPlancher)).DefaultIfEmpty(p.HauteurDernierPlancher).Max();
            double longueur = f.LongueurObtenue;
            var limite = Limites.Where(x => Cotes.Egal(x.Largeur, l)).Select(x => ((double, double)?)(x.HauteurMax, x.LongueurMin)).FirstOrDefault();
            var fr = CultureNombres.Documents;
            if (limite == null)
            {
                ecarts.Add(string.Format(fr, "{0} : largeur {1:0.00} m impossible en tour roulante (1,00 / 1,05 / 1,50 / 2,00 / 2,50 / 3,00 m).", f.Nom, l));
                continue;
            }

            var (hMax, lMin) = limite.Value;
            if (h > hMax + Cotes.Tolerance)
            {
                ecarts.Add(string.Format(fr, "{0} : dernier plancher à {1:0.00} m, maximum {2:0.00} m pour une tour roulante de {3:0.00} m de large.", f.Nom, h, hMax, l));
            }

            if (longueur < lMin - Cotes.Tolerance)
            {
                ecarts.Add(string.Format(fr, "{0} : longueur {1:0.00} m, minimum {2:0.00} m pour une tour roulante de {3:0.00} m de large.", f.Nom, longueur, lMin, l));
            }
        }

        return ecarts;
    }

    /// <summary>Refus de dessiner hors règles : ce qui ne va pas, puis le rappel des règles.</summary>
    public static void Verifier(ProjetStructure projet)
    {
        var ecarts = Ecarts(projet);
        if (ecarts.Count > 0)
        {
            throw new InvalidOperationException(
                string.Join(Environment.NewLine, ecarts) + Environment.NewLine + Environment.NewLine + Rappel +
                Environment.NewLine + "La structure n'est pas dessinée.");
        }
    }
}
