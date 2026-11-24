using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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

    /// <summary>
    /// 2 : consoles par niveau + cases par maille, case « Diagonale » par maille (23/11/2026).
    /// 3 : structure à plusieurs faces (S1…S4) et paramètres généraux communs (24/11/2026).
    /// </summary>
    public const int VersionFormat = 3;

    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string VersJson(ProjetStructure projet) =>
        JsonSerializer.Serialize(new Enveloppe { Version = VersionFormat, Structure = projet }, Options);

    public static ProjetStructure DepuisJson(string json)
    {
        // « Garde-corps MDS 1 » n'existe plus (Laurent, 23/11/2026) : les anciens fichiers passent en MDS 2.
        json = json.Replace("\"GardeCorpsMds1\"", "\"GardeCorpsMds2\"");
        var racine = JsonNode.Parse(json) as JsonObject ?? throw new InvalidDataException("Fichier projet vide.");
        int version = racine["version"]?.GetValue<int>() ?? 0;
        if (version > VersionFormat)
        {
            throw new InvalidDataException($"Fichier projet en version {version}, non pris en charge par cette version de MultiDEC.");
        }

        if (version >= 3)
        {
            var e = racine.Deserialize<Enveloppe>(Options);
            var structure = e?.Structure ?? throw new InvalidDataException("Le fichier ne contient pas de structure.");
            if (structure.Faces.Count == 0)
            {
                throw new InvalidDataException("La structure n'a aucune face.");
            }

            structure.AjusterFaces();
            return structure;
        }

        return DepuisFaceUnique(racine, version);
    }

    /// <summary>
    /// Fichiers v1 et v2 : une seule façade (nœud « facade »), dont les paramètres contenaient aussi
    /// les paramètres généraux et les informations d'étude. Elle devient la face S1 d'une structure droite.
    /// </summary>
    private static ProjetStructure DepuisFaceUnique(JsonObject racine, int version)
    {
        var noeud = racine["facade"] as JsonObject ?? throw new InvalidDataException("Le fichier ne contient pas de projet façade.");
        var face = noeud.Deserialize<FaceEchafaudage>(Options) ?? throw new InvalidDataException("Projet façade illisible.");
        var general = noeud["parametres"]?.Deserialize<ParametresGeneraux>(Options) ?? new ParametresGeneraux();
        var projet = new ProjetStructure
        {
            Chantier = noeud["chantier"]?.GetValue<string>() ?? string.Empty,
            Client = noeud["client"]?.GetValue<string>() ?? string.Empty,
            ReferenceEtude = noeud["referenceEtude"]?.GetValue<string>() ?? string.Empty,
            General = general,
            Forme = FormeStructure.Droite,
        };
        if (noeud["date"] is JsonNode date)
        {
            projet.Date = date.GetValue<DateTime>();
        }

        face.Nom = "S1";
        projet.Faces.Add(face);
        projet.AppliquerGeneraux();
        if (version < 2)
        {
            face.ConvertirConsolesAncienFormat();
            RegleDiagonales.Proposer(face.Parametres, face.Mailles);
        }

        return projet;
    }

    public static void Enregistrer(ProjetStructure projet, string chemin) =>
        File.WriteAllText(chemin, VersJson(projet), new UTF8Encoding(false));

    public static ProjetStructure Ouvrir(string chemin) => DepuisJson(File.ReadAllText(chemin, Encoding.UTF8));

    private sealed class Enveloppe
    {
        public string Application { get; set; } = "MultiDEC";

        public int Version { get; set; }

        public ProjetStructure? Structure { get; set; }
    }
}
