using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using MultiDEC.Core.Droits;

namespace MultiDEC.App.Droits;

/// <summary>
/// Droits d'utilisation (Laurent, 27/11/2026) : code du poste, lecture de la liste signée des postes autorisés sur
/// retotub.com au démarrage, mémoire locale chiffrée (DPAPI) pour la tolérance hors ligne de 30 jours.
/// Les règles sont dans <see cref="ControleDroits"/> (MultiDEC.Core, testées).
/// </summary>
internal static class DroitsApplication
{
    /// <summary>Adresse à laquelle envoyer les demandes d'accès (Laurent, 27/11/2026).</summary>
    public const string AdresseDemandes = "multidec@retotub.com";

    private static readonly byte[] Entropie = Encoding.UTF8.GetBytes("MultiDEC v3 - droits d'utilisation");

    /// <summary>Dernier contrôle effectué (menu Aide, « Droits d'utilisation »).</summary>
    public static ResultatDroits? DernierResultat { get; private set; }

    public static string? DernierCode { get; private set; }

    /// <summary>Clé publique de RETOTUB compilée dans l'exe (Droits\cle-publique.xml) ; null = contrôle inactif.</summary>
    public static string? ClePublique
    {
        get
        {
            using var flux = typeof(DroitsApplication).Assembly.GetManifestResourceStream("MultiDEC.App.cle-publique-droits.xml");
            if (flux == null)
            {
                return null;
            }

            using var lecture = new StreamReader(flux);
            string cle = lecture.ReadToEnd();
            return cle.Contains("<Modulus>") ? cle : null;
        }
    }

    /// <summary>
    /// Adresse de la liste : celle compilée dans l'exe (propriété MSBuild AdresseDroits) en priorité, sinon à côté de la
    /// fiche des mises à jour (mise-a-jour.txt).
    /// </summary>
    public static string? AdresseListe =>
        typeof(DroitsApplication).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "AdresseDroits")?.Value is { Length: > 0 } a
            ? a
            : ControleDroits.AdresseListe(MiseAJourApplication.Adresse);

    private static string CheminEtat =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MultiDEC", "droits.dat");

    /// <summary>Contrôle complet (appelé hors du fil de l'interface : accès réseau, WMI).</summary>
    public static ResultatDroits Verifier()
    {
        EtatDroits? etat = LireEtat();
        string code = CodeDuPoste(etat);
        string? cle = ClePublique;
        string? liste = null;
        DateTime? dateServeur = null;
        if (cle != null)
        {
            (liste, dateServeur) = Telecharger(AdresseListe);
        }

        var resultat = ControleDroits.Evaluer(code, cle, liste, dateServeur, etat, DateTime.UtcNow);
        if (resultat.NouvelEtat != null)
        {
            EcrireEtat(resultat.NouvelEtat);
        }

        if (resultat.Motif != MotifDroits.AutoriseEnLigne && resultat.Motif != MotifDroits.ControleInactif)
        {
            App.Journaliser($"Droits d'utilisation : {resultat.Motif} (poste {code}, liste {AdresseListe ?? "sans adresse"}). {resultat.Message}");
        }

        DernierResultat = resultat;
        DernierCode = code;
        return resultat;
    }

    /// <summary>Code du poste ; si les identifiants de la machine sont illisibles, celui de la dernière vérification.</summary>
    public static string CodeDuPoste(EtatDroits? etat = null)
    {
        try
        {
            return CodePoste.Calculer(IdentifiantsPoste());
        }
        catch (Exception ex) when (etat?.Code != null)
        {
            App.Journaliser(ex);
            return etat.Code!;
        }
    }

    /// <summary>
    /// Identifiants stables : MachineGuid de Windows (vue 64 bits du registre, l'application est en x86) et UUID SMBIOS
    /// de la carte mère. Changent à la réinstallation de Windows ou au remplacement de la carte mère, pas avec le disque
    /// ni la carte réseau.
    /// </summary>
    public static IReadOnlyList<string> IdentifiantsPoste()
    {
        string machine;
        using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32))
        using (var cle = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
        {
            machine = cle?.GetValue("MachineGuid") as string ?? string.Empty;
        }

        string uuid = string.Empty;
        try
        {
            using var recherche = new ManagementObjectSearcher("SELECT UUID FROM Win32_ComputerSystemProduct");
            foreach (ManagementObject o in recherche.Get())
            {
                using (o)
                {
                    string valeur = (o["UUID"] as string ?? string.Empty).Trim();
                    if (UuidUtilisable(valeur))
                    {
                        uuid = valeur;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is ManagementException || ex is System.Runtime.InteropServices.COMException || ex is UnauthorizedAccessException)
        {
            // Sans l'UUID le code serait différent : on ne le calcule pas (le code mémorisé sert de secours).
            throw new InvalidOperationException("UUID de la carte mère illisible (WMI) : " + ex.Message, ex);
        }

        if (machine.Length == 0 && uuid.Length == 0)
        {
            throw new InvalidOperationException("Identifiants du poste introuvables.");
        }

        return new[] { machine, uuid };
    }

    // Valeurs de remplissage de certains fabricants : ignorées (elles seraient identiques sur toutes les machines).
    private static bool UuidUtilisable(string uuid)
    {
        string chiffres = new string(uuid.Where(Uri.IsHexDigit).ToArray());
        return chiffres.Length >= 16 && chiffres.Distinct().Count() > 1;
    }

    private static (string? liste, DateTime? dateServeur) Telecharger(string? adresse)
    {
        if (adresse == null)
        {
            return (null, null);
        }

        try
        {
            if (!adresse.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                return (File.Exists(adresse) ? File.ReadAllText(adresse) : null, null);
            }

            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("MultiDEC/" + MiseAJourApplication.VersionInstallee);
            using var reponse = client.GetAsync(adresse + (adresse.Contains("?") ? "&" : "?") + "t=" + DateTime.UtcNow.Ticks).GetAwaiter().GetResult();
            if (!reponse.IsSuccessStatusCode)
            {
                App.Journaliser($"Droits d'utilisation : {adresse} a répondu {(int)reponse.StatusCode} {reponse.ReasonPhrase}.");
                return (null, null);
            }

            DateTime? date = reponse.Headers.Date?.UtcDateTime;
            return (reponse.Content.ReadAsStringAsync().GetAwaiter().GetResult(), date);
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is System.Threading.Tasks.TaskCanceledException || ex is IOException || ex is WebException || ex is UnauthorizedAccessException)
        {
            App.Journaliser($"Droits d'utilisation : {adresse} injoignable ({ex.GetBaseException().Message}).");
            return (null, null);
        }
    }

    private static EtatDroits? LireEtat()
    {
        try
        {
            if (!File.Exists(CheminEtat))
            {
                return null;
            }

            byte[] clair = ProtectedData.Unprotect(File.ReadAllBytes(CheminEtat), Entropie, DataProtectionScope.CurrentUser);
            return EtatDroits.Lire(Encoding.UTF8.GetString(clair));
        }
        catch (Exception ex) when (ex is CryptographicException || ex is IOException || ex is System.Text.Json.JsonException || ex is UnauthorizedAccessException)
        {
            // Fichier copié d'un autre poste ou abîmé : ignoré (une vérification en ligne le recrée).
            App.Journaliser(ex);
            return null;
        }
    }

    private static void EcrireEtat(EtatDroits etat)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CheminEtat)!);
            byte[] chiffre = ProtectedData.Protect(Encoding.UTF8.GetBytes(etat.Ecrire()), Entropie, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(CheminEtat, chiffre);
        }
        catch (Exception ex) when (ex is CryptographicException || ex is IOException || ex is UnauthorizedAccessException)
        {
            App.Journaliser(ex);
        }
    }

    /// <summary>Courriel de demande d'accès (client de messagerie par défaut).</summary>
    public static string LienDemande(string code)
    {
        string sujet = "Demande de droit d'utilisation MultiDEC v3";
        string corps = "Bonjour,\r\n\r\nMerci d'autoriser ce poste à utiliser MultiDEC v3.\r\n\r\n"
                       + $"Code du poste : {code}\r\n"
                       + $"Nom de l'ordinateur : {Environment.MachineName}\r\n"
                       + $"Utilisateur Windows : {Environment.UserName}\r\n"
                       + $"Version : {VersionApplication.Texte}\r\n\r\n"
                       + "Société : \r\nNom et prénom : \r\nTéléphone : \r\n";
        return $"mailto:{AdresseDemandes}?subject={Uri.EscapeDataString(sujet)}&body={Uri.EscapeDataString(corps)}";
    }
}
