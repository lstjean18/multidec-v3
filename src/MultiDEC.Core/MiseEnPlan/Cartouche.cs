using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MultiDEC.Core.Facade;

namespace MultiDEC.Core.MiseEnPlan;

/// <summary>
/// Contenu du cartouche (Laurent, 27/11/2026) : plan issu de MultiDEC avec sa version, logos, client, chantier,
/// caractéristiques de la structure et dimensions de chaque face.
/// </summary>
public sealed class Cartouche
{
    /// <summary>« Plan établi avec MultiDEC v3.0 bêta 1 – build 105 ».</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>Logo client (Paramètres par défaut), chemin d'une image, ou null.</summary>
    public string? LogoClient { get; set; }

    /// <summary>Modèle d'échafaudage (logo Multi-RETO ou Multi-SYSTEM).</summary>
    public ModeleEchafaudage Modele { get; set; }

    /// <summary>Client, chantier, référence de l'étude, date.</summary>
    public List<KeyValuePair<string, string>> Projet { get; } = new List<KeyValuePair<string, string>>();

    /// <summary>Forme, modèle, plateaux, protections…</summary>
    public List<KeyValuePair<string, string>> Structure { get; } = new List<KeyValuePair<string, string>>();

    /// <summary>Tableau des faces : première ligne = en-têtes.</summary>
    public List<string[]> Faces { get; } = new List<string[]>();

    /// <summary>Poids total du matériel, ex. « 4 512 kg » (vide si inconnu).</summary>
    public string Poids { get; set; } = string.Empty;

    /// <summary>Mentions importantes (note de calcul obligatoire…).</summary>
    public List<string> Mentions { get; } = new List<string>();

    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    public static string Metres(double m) => m.ToString("0.00", Fr) + " m";

    /// <summary>Construit le cartouche d'un projet dessiné.</summary>
    /// <param name="libelle">Libellé affiché d'une valeur d'énumération (celui de l'interface).</param>
    public static Cartouche De(ProjetStructure projet, StructureFacade structure, string version, double? poidsKg, string? logoClient, Func<object, string> libelle)
    {
        var g = projet.General;
        var c = new Cartouche { Version = version, LogoClient = logoClient, Modele = g.Modele };
        void P(string k, string v) => c.Projet.Add(new KeyValuePair<string, string>(k, v));
        void S(string k, string v) => c.Structure.Add(new KeyValuePair<string, string>(k, v));

        P("Client", projet.Client);
        P("Chantier", projet.Chantier);
        P("Réf. étude", projet.ReferenceEtude);
        P("Date", projet.Date.ToString("dd/MM/yyyy", Fr));

        string forme = libelle(projet.Forme);
        S("Forme", projet.EstVolumique ? forme + " (" + projet.Faces.Count + " modules)" : forme);
        S("Modèle", libelle(g.Modele));
        S("Plateaux", libelle(g.Plateaux));
        S("Protection ext.", libelle(g.SecuriteExterieure));
        S("Protection int.", libelle(g.SecuriteInterieure));
        S("Abouts", g.AboutGauche || g.AboutDroit ? libelle(g.SecuriteAbouts) : "sans");
        S("Bâche", g.Bache ? "oui" : "non");

        c.Faces.Add(new[] { "Face", "Longueur", "Largeur", "Dern. plancher", "Hauteur", "Amarrages" });
        foreach (var f in projet.Faces)
        {
            int amarrages = structure.Amarrages.Amarrages.Count(a => a.NomFace == f.Nom);
            c.Faces.Add(new[]
            {
                f.Nom,
                Metres(f.LongueurObtenue),
                Metres(f.Parametres.LargeurMaille),
                Metres(f.Parametres.HauteurDernierPlancher),
                Metres(f.Mailles.Count == 0 ? f.HauteurTotale : f.Mailles.Max(m => m.HauteurMax(f.Parametres.HauteurDernierPlancher)) + f.HauteurTotale - f.Parametres.HauteurDernierPlancher),
                amarrages.ToString(Fr),
            });
        }

        if (poidsKg is > 0)
        {
            c.Poids = Math.Round(poidsKg.Value).ToString("#,0", Fr) + " kg";
        }

        if (projet.Faces.Any(f => f.NoteDeCalculObligatoire))
        {
            c.Mentions.Add("Hauteur > " + g.SeuilNoteDeCalcul.ToString("0", Fr) + " m : note de calcul obligatoire");
        }

        return c;
    }
}
