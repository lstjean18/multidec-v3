using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using MultiDEC.Core.Geometrie;

namespace MultiDEC.Core.Facade;

/// <summary>
/// Une face de la structure (S1, S2…) : ses paramètres, sa grille des mailles et sa grille des niveaux.
/// Les paramètres généraux (modèle, plateaux, protections, amarrages) viennent de <see cref="ProjetStructure.General"/>.
/// C'est l'unité que le moteur sait générer.
/// </summary>
public sealed class FaceEchafaudage
{
    /// <summary>Nom de la face : S1, S2, S3, S4.</summary>
    public string Nom { get; set; } = "S1";

    public ParametresFacade Parametres { get; set; } = new ParametresFacade();

    /// <summary>Grille des mailles, de gauche à droite (X croissant). Éditable.</summary>
    public List<MailleDefinition> Mailles { get; set; } = new List<MailleDefinition>();

    /// <summary>Niveaux de plancher (toutes hauteurs, y compris 0). Éditable.</summary>
    public List<Niveau> Niveaux { get; set; } = new List<Niveau>();

    [JsonIgnore]
    public double LongueurObtenue => Cotes.Arrondi(Mailles.Sum(m => m.Longueur));

    /// <summary>Hauteur totale = dernier plancher + 1,00 m de garde-corps (+0,50 m couvreur).</summary>
    [JsonIgnore]
    public double HauteurTotale =>
        Parametres.HauteurDernierPlancher + 1.0 + (Parametres.GardeCorpsCouvreur ? 0.5 : 0.0);

    [JsonIgnore]
    public bool NoteDeCalculObligatoire => HauteurTotale > Parametres.SeuilNoteDeCalcul;

    /// <summary>
    /// Console posée sur une maille à un niveau : celle du niveau si la case « ConXm » de la maille est cochée,
    /// seulement aux niveaux de plancher complets au-dessus du sol. Null sinon.
    /// </summary>
    public string? ConsoleDe(MailleDefinition maille, Niveau niveau) =>
        niveau.AConsole && niveau.Type == TypePlancher.Complet && niveau.Hauteur > Cotes.Tolerance && maille.ConsoleA(niveau.Hauteur)
            ? niveau.Console
            : null;

    /// <summary>Pare-gravois posé sur une maille à un niveau : niveau coché et case « PareGravXm » de la maille cochée.</summary>
    public bool PareGravoisSur(MailleDefinition maille, Niveau niveau) =>
        niveau.PareGravois && niveau.Hauteur > Cotes.Tolerance && maille.PareGravoisA(niveau.Hauteur);

    /// <summary>
    /// Fichiers d'avant le 24/11/2026 (pare-gravois sur toute la face) : un niveau de pare-gravois sans aucune maille
    /// cochée est coché sur toutes les mailles.
    /// </summary>
    public void ConvertirPareGravoisAncienFormat()
    {
        foreach (var n in Niveaux.Where(n => n.PareGravois && !Mailles.Any(m => m.PareGravoisA(n.Hauteur))))
        {
            foreach (var m in Mailles)
            {
                m.DefinirPareGravois(n.Hauteur, true);
            }
        }
    }

    /// <summary>
    /// Conversion d'un fichier v1 (console par maille, tous niveaux complets) : chaque niveau complet reçoit
    /// la console la plus fréquente et les mailles qui en avaient une sont cochées à ces niveaux.
    /// </summary>
    public void ConvertirConsolesAncienFormat()
    {
        var anciennes = Mailles.Where(m => !string.IsNullOrWhiteSpace(m.ConsoleAncienFormat)).ToList();
        if (anciennes.Count > 0)
        {
            string console = anciennes.GroupBy(m => m.ConsoleAncienFormat!).OrderByDescending(g => g.Count()).First().Key;
            foreach (var n in Niveaux.Where(n => n.Type == TypePlancher.Complet && n.Hauteur > Cotes.Tolerance))
            {
                n.Console = console;
                foreach (var m in anciennes)
                {
                    m.DefinirConsole(n.Hauteur, true);
                }
            }
        }

        foreach (var m in Mailles)
        {
            m.ConsoleAncienFormat = null;
        }
    }

    /// <summary>Copie indépendante (paramètres, mailles, niveaux).</summary>
    public FaceEchafaudage Copie() => new FaceEchafaudage
    {
        Nom = Nom,
        Parametres = Parametres.Copie(),
        Mailles = Mailles.Select(m => m.Copie()).ToList(),
        Niveaux = Niveaux.Select(n => new Niveau(n.Hauteur, n.Type, n.Console, n.DiagoH) { PareGravois = n.PareGravois }).ToList(),
    };

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
