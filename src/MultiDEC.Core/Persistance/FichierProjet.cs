using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MultiDEC.Core.Facade;

namespace MultiDEC.Core.Persistance;

/// <summary>
/// Format de fichier projet v3 (.mdc3) : JSON lisible, versionné.
/// Les anciens .mdc ne sont pas importés (décision Laurent).
/// </summary>
public static class FichierProjet
{
    public const string Extension = ".mdc3";
    public const int VersionFormat = 1;

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
        var e = JsonSerializer.Deserialize<Enveloppe>(json, Options)
                ?? throw new InvalidDataException("Fichier projet vide.");
        if (e.Version > VersionFormat)
        {
            throw new InvalidDataException($"Fichier projet en version {e.Version}, non pris en charge par cette version de MultiDEC.");
        }

        return e.Facade ?? throw new InvalidDataException("Le fichier ne contient pas de projet façade.");
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
