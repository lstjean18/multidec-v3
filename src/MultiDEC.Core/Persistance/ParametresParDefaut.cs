using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using MultiDEC.Core.Facade;
using MultiDEC.Core.Facade.Regles;

namespace MultiDEC.Core.Persistance;

/// <summary>Clés des listes de choix dont l'utilisateur peut retirer des valeurs (écran « Paramètres par défaut »).</summary>
public static class ListesDeChoix
{
    public const string Modele = "Modele";
    public const string Plateaux = "Plateaux";
    public const string Socle = "Socle";
    public const string Securite = "Securite";
    public const string Plinthe = "Plinthe";
    public const string LargeurMaille = "LargeurMaille";
    public const string LongueurMaille = "LongueurMaille";
    public const string Traverses = "Traverses";
    public const string TypeMaille = "TypeMaille";
    public const string TypePlancher = "TypePlancher";
    public const string Console = "Console";
    public const string Stabilisation = "Stabilisation";
    public const string ProfondeurRecette = "ProfondeurRecette";
    public const string ModeRendu = "ModeRendu";
}

/// <summary>
/// Paramètres par défaut d'un utilisateur (Laurent, 24/11/2026) : valeurs de toutes les cases et listes
/// au « Nouveau » et à l'ouverture du programme, valeurs retirées des listes, fichiers de référence
/// (bibliothèque de blocs, trame Excel des devis, références clients, logo client).
/// Enregistrés dans le profil Windows de l'utilisateur ; exportables et importables (même format JSON).
/// </summary>
public sealed class ParametresParDefaut
{
    public const int VersionFormat = 1;

    /// <summary>Nom du fichier dans %APPDATA%\MultiDEC\v3.</summary>
    public const string NomFichier = "parametres-par-defaut.json";

    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public int Version { get; set; } = VersionFormat;

    /// <summary>Paramètres communs (onglet « Général ») : matériel, protections, abouts, bâche, amarrages.</summary>
    public ParametresGeneraux General { get; set; } = new ParametresGeneraux();

    /// <summary>Paramètres d'une nouvelle face. « Lisses en pied » cochée par défaut (Laurent, 24/11/2026).</summary>
    public ParametresFacade Face { get; set; } = new ParametresFacade { LissesEnPied = true };

    public FormeStructure Forme { get; set; } = FormeStructure.Droite;

    /// <summary>Mode de rendu du dessin (nom de la valeur de ModeRendu : Filaire, LignesCachees, Ombre…).</summary>
    public string ModeRendu { get; set; } = "Ombre";

    public bool DessinerFacade { get; set; }

    /// <summary>Valeurs retirées de chaque liste (<see cref="ListesDeChoix"/>), sous la forme de <see cref="Cle"/>.</summary>
    public Dictionary<string, List<string>> Masques { get; set; } = new Dictionary<string, List<string>>();

    /// <summary>Bibliothèque de blocs chargée au démarrage.</summary>
    public string? Bibliotheque { get; set; }

    /// <summary>Fichier Excel servant de trame aux devis.</summary>
    public string? ModeleDevis { get; set; }

    /// <summary>Fichier de correspondance des références RETOTUB en références clients (CSV RefRETOTUB;RefClient).</summary>
    public string? ReferencesClients { get; set; }

    /// <summary>
    /// Références affichées dans le décompte (Laurent 25/11/2026) : RETOTUB, LVTEC (table intégrée) ou FICHIER
    /// (<see cref="ReferencesClients"/>). Voir <see cref="Decompte.ReferencesClient"/>.
    /// </summary>
    public string ReferencesAffichees { get; set; } = Decompte.ReferencesClient.Retotub;

    /// <summary>Logo client pour le décompte, le devis et la mise en plan.</summary>
    public string? LogoClient { get; set; }

    public static ParametresParDefaut Usine() => new ParametresParDefaut();

    /// <summary>Forme écrite d'une valeur de liste : nom de l'énumération, cote « 0.70 », référence.</summary>
    public static string Cle(object? valeur) => valeur switch
    {
        null => string.Empty,
        double d => d.ToString("0.00", CultureInfo.InvariantCulture),
        _ => valeur.ToString() ?? string.Empty,
    };

    public bool EstMasque(string liste, object? valeur) =>
        Masques.TryGetValue(liste, out var l) && l.Contains(Cle(valeur));

    public void Masquer(string liste, object? valeur, bool masque)
    {
        string cle = Cle(valeur);
        if (!Masques.TryGetValue(liste, out var l))
        {
            if (!masque)
            {
                return;
            }

            Masques[liste] = l = new List<string>();
        }

        l.Remove(cle);
        if (masque)
        {
            l.Add(cle);
        }

        if (l.Count == 0)
        {
            Masques.Remove(liste);
        }
    }

    /// <summary>
    /// Valeurs proposées d'une liste : celles qui ne sont pas retirées, plus la valeur actuelle si elle l'est
    /// (projet ouvert qui l'utilise). Si tout est retiré, la liste reste complète.
    /// </summary>
    public IReadOnlyList<T> Filtrer<T>(string liste, IEnumerable<T> valeurs, object? courant = null)
    {
        var toutes = valeurs.ToList();
        var gardees = toutes.Where(v => !EstMasque(liste, v) || (courant != null && Cle(v) == Cle(courant))).ToList();
        return gardees.Count == 0 ? toutes : gardees;
    }

    /// <summary>Nouveau projet avec ces paramètres (copies : le projet ne modifie pas les paramètres par défaut).</summary>
    public ProjetStructure NouveauProjet()
    {
        var copie = Copie();
        copie.Face.NombreAcces = RegleAcces.NombreConseille(copie.Face.LongueurTotaleSouhaitee);
        var projet = ProjetStructure.Nouveau(copie.General, copie.Face);
        if (Forme != FormeStructure.Droite)
        {
            projet.Forme = Forme;
            projet.AjusterFaces();
        }

        return projet;
    }

    public ParametresParDefaut Copie() => DepuisJson(VersJson());

    public string VersJson() => JsonSerializer.Serialize(this, Options);

    public static ParametresParDefaut DepuisJson(string json)
    {
        var p = JsonSerializer.Deserialize<ParametresParDefaut>(json, Options) ?? throw new InvalidDataException("Paramètres par défaut vides.");
        p.General ??= new ParametresGeneraux();
        p.Face ??= new ParametresFacade { LissesEnPied = true };
        p.Masques ??= new Dictionary<string, List<string>>();
        p.ReferencesAffichees ??= Decompte.ReferencesClient.Retotub;
        return p;
    }

    /// <summary>Lit le fichier ; paramètres d'usine s'il n'existe pas.</summary>
    public static ParametresParDefaut Charger(string chemin) =>
        File.Exists(chemin) ? DepuisJson(File.ReadAllText(chemin)) : Usine();

    public void Enregistrer(string chemin)
    {
        string? dossier = Path.GetDirectoryName(chemin);
        if (!string.IsNullOrEmpty(dossier))
        {
            Directory.CreateDirectory(dossier);
        }

        File.WriteAllText(chemin, VersJson());
    }
}
