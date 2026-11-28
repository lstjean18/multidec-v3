using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using MultiDEC.Core.Droits;
using MultiDEC.Core.MiseAJour;
using WinSCP;

namespace MultiDEC.Admin.Services;

/// <summary>Envoi sur retotub.com (SFTP, WinSCP) et contrôle de ce que le site sert ensuite.</summary>
public sealed class Publication
{
    public const string InstallateurNomFixe = "MultiDEC-v3-Setup.exe";

    private readonly ParametresPublication _p;
    private readonly string _motDePasse;

    public Publication(ParametresPublication parametres, string? motDePasse)
    {
        if (!parametres.EstComplet)
        {
            throw new InvalidOperationException("Réglages de publication incomplets (onglet Paramètres).");
        }

        if (string.IsNullOrEmpty(motDePasse))
        {
            throw new InvalidOperationException("Mot de passe SFTP non renseigné (onglet Paramètres).");
        }

        _p = parametres;
        _motDePasse = motDePasse!;
    }

    private SessionOptions Options() => new SessionOptions
    {
        Protocol = Protocol.Sftp,
        HostName = _p.Hote,
        PortNumber = _p.Port,
        UserName = _p.Utilisateur,
        Password = _motDePasse,
        SshHostKeyFingerprint = _p.Empreinte,
    };

    /// <summary>Empreinte de la clé du serveur, à faire confirmer à l'utilisateur.</summary>
    public static string ScannerEmpreinte(string hote, int port)
    {
        using var session = new Session();
        return session.ScanFingerprint(new SessionOptions { Protocol = Protocol.Sftp, HostName = hote, PortNumber = port }, "SHA-256");
    }

    public void TesterConnexion()
    {
        using var session = new Session();
        session.Open(Options());
        session.ListDirectory(_p.Dossier);
    }

    /// <summary>Envoie les fichiers dans l'ordre donné (le dernier est celui qui « annonce » la publication).</summary>
    public void Envoyer(IEnumerable<(string local, string nomDistant)> fichiers, Action<string> journal)
    {
        using var session = new Session();
        session.Open(Options());
        string distant = _p.Dossier.TrimEnd('/') + "/";
        var transfert = new TransferOptions { TransferMode = TransferMode.Binary, OverwriteMode = OverwriteMode.Overwrite };
        foreach (var (local, nom) in fichiers)
        {
            journal($"Envoi de {nom}…");
            session.PutFiles(local, distant + nom, false, transfert).Check();
        }
    }

    /// <summary>Contenu servi par le site (anti-cache), ou exception.</summary>
    public async Task<string> LireEnLigneAsync(string fichier) => await LireAsync(_p.AdresseWeb(fichier));

    public static async Task<string> LireAsync(string adresse)
    {
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MultiDEC-Admin");
        return await client.GetStringAsync(adresse + (adresse.Contains("?") ? "&" : "?") + "t=" + DateTime.UtcNow.Ticks);
    }

    /// <summary>
    /// Signe et publie la liste des postes autorisés : signature, contrôle avec la clé publique, copie locale, envoi,
    /// puis relecture sur le site.
    /// </summary>
    public static async Task<string> PublierListeAsync(DonneesAdmin donnees, Publication publication, RSA cle, Action<string> journal)
    {
        DateTime emise = DateTime.UtcNow;
        string contenu = donnees.Registre.ContenuPublic(emise);
        string enveloppe = ListePostes.Signer(contenu, cle);
        var verifiee = ListePostes.LireSignee(enveloppe, donnees.ClePublique());
        journal($"Liste signée : {verifiee.Postes.Count} poste(s) autorisé(s).");

        string local = donnees.CheminListe;
        File.WriteAllText(local + ".tmp", enveloppe);
        await Task.Run(() => publication.Envoyer(new[] { (local + ".tmp", ListePostes.NomFichier) }, journal));
        if (File.Exists(local))
        {
            File.Delete(local);
        }

        File.Move(local + ".tmp", local);

        try
        {
            var enLigne = ListePostes.LireSignee(await publication.LireEnLigneAsync(ListePostes.NomFichier), donnees.ClePublique());
            journal(enLigne.Emise == verifiee.Emise
                ? $"Contrôle : le site sert bien la nouvelle liste ({publication._p.AdresseWeb(ListePostes.NomFichier)})."
                : "Attention : le site sert encore une ancienne liste (cache du site ?). Revérifiez dans quelques minutes.");
        }
        catch (Exception ex)
        {
            journal("Attention : relecture de la liste sur le site impossible : " + ex.GetBaseException().Message);
        }

        donnees.Registre.DernierePublication = emise;
        return $"{verifiee.Postes.Count} poste(s)";
    }

    /// <summary>
    /// Publie une version : copie numérotée de l'installateur, installateur à nom fixe (liens du site), puis version.json
    /// en dernier (les postes ne voient la version qu'une fois l'installateur en place) ; même ordre que
    /// publication\Publier-version.ps1.
    /// </summary>
    public static async Task PublierVersionAsync(Publication publication, string installateur, string version, string libelle, string notes, Action<string> journal)
    {
        if (FicheVersion.NumeroDe(version) == null)
        {
            throw new ArgumentException($"Numéro de version illisible : « {version} » (attendu 3.0.1, 3.1.0…).");
        }

        string dossier = Path.Combine(Path.GetTempPath(), "MultiDEC-Admin-publication");
        if (Directory.Exists(dossier))
        {
            Directory.Delete(dossier, true);
        }

        Directory.CreateDirectory(dossier);
        string numerote = Path.Combine(dossier, $"MultiDEC-v3-Setup-{version}.exe");
        File.Copy(installateur, numerote);
        string sha;
        using (var f = File.OpenRead(numerote))
        {
            sha = FicheVersion.Empreinte(f);
        }

        var fiche = new FicheVersion
        {
            Version = version,
            Libelle = string.IsNullOrWhiteSpace(libelle) ? "v" + version : libelle.Trim(),
            Installateur = Path.GetFileName(numerote),
            Sha256 = sha,
            Date = DateTime.UtcNow,
            Notes = notes?.Trim(),
        };
        string json = Path.Combine(dossier, FicheVersion.NomFichier);
        File.WriteAllText(json, fiche.Ecrire());
        journal($"Fiche : version {fiche.Version}, SHA-256 {sha.Substring(0, 16)}…");

        await Task.Run(() => publication.Envoyer(new[]
        {
            (numerote, Path.GetFileName(numerote)),
            (numerote, InstallateurNomFixe),
            (json, FicheVersion.NomFichier),
        }, journal));

        try
        {
            var publiee = FicheVersion.Lire(await publication.LireEnLigneAsync(FicheVersion.NomFichier));
            journal(publiee.Version == fiche.Version && string.Equals(publiee.Sha256, sha, StringComparison.OrdinalIgnoreCase)
                ? $"Contrôle : le site annonce la version {fiche.Version}."
                : "Attention : la fiche lue sur le site ne correspond pas (cache du site ?). Revérifiez dans quelques minutes.");
        }
        catch (Exception ex)
        {
            journal("Attention : relecture de la fiche sur le site impossible : " + ex.GetBaseException().Message);
        }

        Directory.Delete(dossier, true);
    }
}
