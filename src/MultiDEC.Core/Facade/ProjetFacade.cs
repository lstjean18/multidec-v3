using System;
using System.Collections.Generic;
using System.Linq;
using MultiDEC.Core.Geometrie;

namespace MultiDEC.Core.Facade;

/// <summary>
/// Un projet d'échafaudage de façade : paramètres + grille des mailles + grille des niveaux.
/// C'est l'objet enregistré dans le fichier projet v3 (JSON).
/// </summary>
public sealed class ProjetFacade
{
    public string Chantier { get; set; } = string.Empty;

    public string Client { get; set; } = string.Empty;

    public string ReferenceEtude { get; set; } = string.Empty;

    public DateTime Date { get; set; } = DateTime.Today;

    public ParametresFacade Parametres { get; set; } = new ParametresFacade();

    /// <summary>Grille des mailles, de gauche à droite (X croissant). Éditable.</summary>
    public List<MailleDefinition> Mailles { get; set; } = new List<MailleDefinition>();

    /// <summary>Niveaux de plancher (toutes hauteurs, y compris 0). Éditable.</summary>
    public List<Niveau> Niveaux { get; set; } = new List<Niveau>();

    public double LongueurObtenue => Cotes.Arrondi(Mailles.Sum(m => m.Longueur));

    /// <summary>Hauteur totale = dernier plancher + 1,00 m de garde-corps (+0,50 m couvreur).</summary>
    public double HauteurTotale =>
        Parametres.HauteurDernierPlancher + 1.0 + (Parametres.GardeCorpsCouvreur ? 0.5 : 0.0);

    public bool NoteDeCalculObligatoire => HauteurTotale > Parametres.SeuilNoteDeCalcul;

    /// <summary>Abscisses des files de poteaux (n mailles → n+1 files).</summary>
    public IReadOnlyList<double> AbscissesFiles()
    {
        var x = new List<double>(Mailles.Count + 1) { 0.0 };
        double cumul = 0;
        foreach (var maille in Mailles)
        {
            cumul = Cotes.Arrondi(cumul + maille.Longueur);
            x.Add(cumul);
        }

        return x;
    }
}
