using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MultiDEC.Core.Facade;
using MultiDEC.Core.Facade.Regles;

namespace MultiDEC.Core.Persistance;

/// <summary>
/// Format de fichier projet v3 (.mdc3) : JSON lisible, versionné.
/// Les anciens .mdc ne sont pas importés (décision Laurent).
/// </summary>
public static class FichierProjet
{
    public const string Extension = ".mdc3";
    /// <summary>2 : consoles par niveau + cases par maille, case « Diagonale » par maille (23/11/2026).</summary>
    public const int VersionFormat = 2;

    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string VersJson(ProjetFacade projet) =>
        JsonSerializer.Serialize(new Enveloppe { Version = VersionFormat, Facade = projet }, Options);

    public static ProjetFacade DepuisJson(string json)
    {
        // « Garde-corps MDS 1 » n'existe plus (Laurent, 23/11/2026) : les anciens fichiers passent en MDS 2.
        json = json.Replace("\"GardeCorpsMds1\"", "\"GardeCorpsMds2\"");
        var e = JsonSerializer.Deserialize<Enveloppe>(json, Options)
                ?? throw new InvalidDataException("Fichier projet vide.");
        if (e.Version > VersionFormat)
        {
            throw new InvalidDataException($"Fichier projet en version {e.Version}, non pris en charge par cette version de MultiDEC.");
        }

        var projet = e.Facade ?? throw new InvalidDataException("Le fichier ne contient pas de projet façade.");
        if (e.Version < 2)
        {
            projet.ConvertirConsolesAncienFormat();
            RegleDiagonales.Proposer(projet.Parametres, projet.Mailles);
        }

        return projet;
    }

    public static void Enregistrer(ProjetFacade projet, string chemin) =>
        File.WriteAllText(chemin, VersJson(projet), new UTF8Encoding(false));

    public static ProjetFacade Ouvrir(string chemin) => DepuisJson(File.ReadAllText(chemin, Encoding.UTF8));

    private sealed class Enveloppe
    {
        public string Application { get; set; } = "MultiDEC";

        public int Version { get; set; }

        public ProjetFacade? Facade { get; set; }
    }
}
