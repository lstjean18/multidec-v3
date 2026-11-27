using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace MultiDEC.Admin.Services;

/// <summary>
/// Réglages propres à l'utilisateur Windows (%APPDATA%\MultiDEC-Admin\parametres.json) : dossier de données et mot de
/// passe SFTP, chiffré par Windows (DPAPI : lisible seulement par cet utilisateur sur ce poste).
/// </summary>
public sealed class ParametresUtilisateur
{
    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
    private static readonly byte[] Entropie = Encoding.UTF8.GetBytes("MultiDEC Admin - SFTP");

    public static string Chemin => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MultiDEC-Admin", "parametres.json");

    /// <summary>Dossier des données partagées (licences, clé de signature, réglages de publication).</summary>
    public string DossierDonnees { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MultiDEC-Admin", "donnees");

    public string? MotDePasseSftpChiffre { get; set; }

    public static ParametresUtilisateur Charger()
    {
        try
        {
            return File.Exists(Chemin) ? JsonSerializer.Deserialize<ParametresUtilisateur>(File.ReadAllText(Chemin), Options) ?? new ParametresUtilisateur() : new ParametresUtilisateur();
        }
        catch (JsonException)
        {
            return new ParametresUtilisateur();
        }
    }

    public void Enregistrer()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Chemin)!);
        File.WriteAllText(Chemin, JsonSerializer.Serialize(this, Options));
    }

    public string? MotDePasseSftp
    {
        get
        {
            if (string.IsNullOrEmpty(MotDePasseSftpChiffre))
            {
                return null;
            }

            try
            {
                return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(MotDePasseSftpChiffre), Entropie, DataProtectionScope.CurrentUser));
            }
            catch (CryptographicException)
            {
                return null;
            }
        }

        set => MotDePasseSftpChiffre = string.IsNullOrEmpty(value) ? null
            : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropie, DataProtectionScope.CurrentUser));
    }
}

/// <summary>
/// Réglages de publication communs (publication.json du dossier de données) : serveur SFTP, dossier distant, adresse
/// web publique. Sans mot de passe : chaque utilisateur saisit le sien.
/// </summary>
public sealed class ParametresPublication
{
    public const string NomFichier = "publication.json";
    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };

    public string Hote { get; set; } = string.Empty;
    public int Port { get; set; } = 22;
    public string Utilisateur { get; set; } = string.Empty;

    /// <summary>Dossier distant des mises à jour (ex. /www/multidec).</summary>
    public string Dossier { get; set; } = string.Empty;

    /// <summary>Adresse web de ce dossier (ex. https://www.retotub.com/multidec).</summary>
    public string AdressePublique { get; set; } = string.Empty;

    /// <summary>Empreinte de la clé du serveur SSH (format WinSCP, ex. « ssh-ed25519 255 abc… »).</summary>
    public string Empreinte { get; set; } = string.Empty;

    public bool EstComplet => Hote.Length > 0 && Utilisateur.Length > 0 && Dossier.Length > 0 && AdressePublique.Length > 0 && Empreinte.Length > 0;

    public string AdresseWeb(string fichier) => AdressePublique.TrimEnd('/') + "/" + fichier;

    public static ParametresPublication Charger(string dossierDonnees)
    {
        string f = Path.Combine(dossierDonnees, NomFichier);
        return File.Exists(f) ? JsonSerializer.Deserialize<ParametresPublication>(File.ReadAllText(f), Options) ?? new ParametresPublication() : new ParametresPublication();
    }

    public void Enregistrer(string dossierDonnees)
    {
        Directory.CreateDirectory(dossierDonnees);
        File.WriteAllText(Path.Combine(dossierDonnees, NomFichier), JsonSerializer.Serialize(this, Options));
    }

    /// <summary>
    /// Reprend la configuration enregistrée par publication\Publier-version.ps1 -ReconfigurerSftp
    /// (%APPDATA%\MultiDEC-publication\sftp.xml, Export-Clixml ; mot de passe chiffré DPAPI du même utilisateur).
    /// </summary>
    public static (ParametresPublication parametres, string? motDePasse)? ImporterScript()
    {
        string f = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MultiDEC-publication", "sftp.xml");
        if (!File.Exists(f))
        {
            return null;
        }

        var doc = XDocument.Load(f);
        string? Valeur(string nom)
        {
            foreach (var e in doc.Descendants())
            {
                if ((string?)e.Attribute("N") == nom)
                {
                    return e.Value;
                }
            }

            return null;
        }

        var p = new ParametresPublication
        {
            Hote = Valeur("Hote") ?? string.Empty,
            Port = int.TryParse(Valeur("Port"), out int port) ? port : 22,
            Utilisateur = Valeur("Utilisateur") ?? string.Empty,
            Dossier = Valeur("Dossier") ?? string.Empty,
            AdressePublique = Valeur("AdressePublique") ?? string.Empty,
            Empreinte = Valeur("Empreinte") ?? string.Empty,
        };

        string? motDePasse = null;
        string? hex = Valeur("MotDePasse");
        if (!string.IsNullOrEmpty(hex))
        {
            try
            {
                // ConvertFrom-SecureString : DPAPI (utilisateur courant) du mot de passe en UTF-16, en hexadécimal.
                var octets = new byte[hex!.Length / 2];
                for (int i = 0; i < octets.Length; i++)
                {
                    octets[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
                }

                motDePasse = Encoding.Unicode.GetString(ProtectedData.Unprotect(octets, null, DataProtectionScope.CurrentUser));
            }
            catch (Exception ex) when (ex is CryptographicException || ex is FormatException)
            {
                motDePasse = null;
            }
        }

        return (p, motDePasse);
    }
}
