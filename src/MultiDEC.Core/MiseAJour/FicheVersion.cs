using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace MultiDEC.Core.MiseAJour;

/// <summary>
/// Fiche de la dernière version publiée (version.json), écrite par le script « Signer et publier » à côté de l'installateur
/// et lue par MultiDEC pour proposer la mise à jour (Laurent, 26-27/11/2026).
/// </summary>
public sealed class FicheVersion
{
    /// <summary>Nom du fichier de la fiche, publié à côté de l'installateur.</summary>
    public const string NomFichier = "version.json";

    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Numéro de version comparable, ex. « 3.0.1 ».</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>Libellé affiché, ex. « v3.0 bêta 2 ».</summary>
    public string? Libelle { get; set; }

    /// <summary>Installateur : nom du fichier à côté de la fiche, ou adresse complète.</summary>
    public string Installateur { get; set; } = string.Empty;

    /// <summary>Empreinte SHA-256 de l'installateur (hexadécimal), contrôlée après le téléchargement.</summary>
    public string Sha256 { get; set; } = string.Empty;

    public DateTime? Date { get; set; }

    /// <summary>Nouveautés, affichées dans la proposition de mise à jour.</summary>
    public string? Notes { get; set; }

    public static FicheVersion Lire(string json)
    {
        var fiche = JsonSerializer.Deserialize<FicheVersion>(json.TrimStart('\uFEFF'), Options) ?? throw new InvalidDataException("Fiche de version vide.");
        if (NumeroDe(fiche.Version) == null)
        {
            throw new InvalidDataException($"Numéro de version illisible : « {fiche.Version} ».");
        }

        if (string.IsNullOrWhiteSpace(fiche.Installateur) || string.IsNullOrWhiteSpace(fiche.Sha256))
        {
            throw new InvalidDataException("Fiche de version incomplète (installateur ou empreinte manquant).");
        }

        return fiche;
    }

    public string Ecrire() => JsonSerializer.Serialize(this, Options);

    /// <summary>« 3.0.1 », « v3.0.1 », « 3.0.1-beta » ou « 3.0.1.0 » → 3.0.1.0 ; null si illisible.</summary>
    public static Version? NumeroDe(string? texte)
    {
        if (string.IsNullOrWhiteSpace(texte))
        {
            return null;
        }

        string t = texte!.Trim().TrimStart('v', 'V');
        int fin = t.IndexOfAny(new[] { '-', '+', ' ' });
        if (fin >= 0)
        {
            t = t.Substring(0, fin);
        }

        if (!System.Version.TryParse(t.Contains('.') ? t : t + ".0", out var v))
        {
            return null;
        }

        return new Version(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
    }

    /// <summary>Vrai si cette version est plus récente que la version installée.</summary>
    public bool EstPlusRecenteQue(Version installee)
    {
        var publiee = NumeroDe(Version);
        var courante = new Version(installee.Major, installee.Minor, Math.Max(installee.Build, 0), Math.Max(installee.Revision, 0));
        return publiee != null && publiee > courante;
    }

    /// <summary>Adresse de l'installateur : telle quelle si elle est complète, sinon à côté de la fiche.</summary>
    public string AdresseInstallateur(string adresseFiche)
    {
        if (Uri.TryCreate(Installateur, UriKind.Absolute, out var absolue) && (absolue.Scheme == Uri.UriSchemeHttps || absolue.Scheme == Uri.UriSchemeHttp))
        {
            return Installateur;
        }

        if (Path.IsPathRooted(Installateur) && !Installateur.StartsWith("/", StringComparison.Ordinal))
        {
            return Installateur;
        }

        if (Uri.TryCreate(adresseFiche, UriKind.Absolute, out var fiche) && (fiche.Scheme == Uri.UriSchemeHttps || fiche.Scheme == Uri.UriSchemeHttp))
        {
            return new Uri(fiche, Installateur).ToString();
        }

        return Path.Combine(Path.GetDirectoryName(adresseFiche) ?? string.Empty, Installateur);
    }

    /// <summary>Contrôle l'empreinte SHA-256 d'un fichier téléchargé.</summary>
    public bool EmpreinteConforme(Stream contenu) =>
        string.Equals(Empreinte(contenu), Sha256.Trim(), StringComparison.OrdinalIgnoreCase);

    public static string Empreinte(Stream contenu)
    {
        using var sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(contenu).Select(b => b.ToString("x2")));
    }
}
