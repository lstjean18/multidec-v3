using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using MultiDEC.Core.MiseAJour;

namespace MultiDEC.App;

/// <summary>
/// Mise à jour automatique (Laurent, 26-27/11/2026) : lit la fiche version.json publiée par le script « Signer et publier »,
/// télécharge l'installateur, contrôle son empreinte et sa signature, puis le lance en mode silencieux.
/// L'adresse de la fiche est dans mise-a-jour.txt, à côté de MultiDEC.exe (écrit par le script de publication).
/// </summary>
internal static class MiseAJourApplication
{
    public const string FichierAdresse = "mise-a-jour.txt";

    /// <summary>Version installée (numéro de l'assembly, fixé par /p:Version au moment de la publication).</summary>
    public static Version VersionInstallee => typeof(MiseAJourApplication).Assembly.GetName().Version ?? new Version(0, 0);

    /// <summary>Adresse de la fiche (https://…/version.json ou chemin de dossier/fichier), ou null si non définie.</summary>
    public static string? Adresse
    {
        get
        {
            try
            {
                string f = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FichierAdresse);
                string? ligne = File.Exists(f) ? File.ReadAllLines(f).Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0 && !l.StartsWith("#", StringComparison.Ordinal)) : null;
                if (ligne == null)
                {
                    return null;
                }

                // Un dossier : la fiche est dedans.
                return EstWeb(ligne) || ligne.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? ligne : Path.Combine(ligne, FicheVersion.NomFichier);
            }
            catch (IOException)
            {
                return null;
            }
        }
    }

    private static bool EstWeb(string adresse) =>
        adresse.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || adresse.StartsWith("http://", StringComparison.OrdinalIgnoreCase);

    private static HttpClient Client()
    {
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MultiDEC/" + VersionInstallee);
        return client;
    }

    /// <summary>Fiche de la dernière version publiée.</summary>
    public static async Task<FicheVersion> LireFicheAsync(string adresse)
    {
        string json;
        if (EstWeb(adresse))
        {
            using var client = Client();
            // Paramètre anti-cache : la fiche change d'une version à l'autre sous la même adresse.
            json = await client.GetStringAsync(adresse + (adresse.Contains("?") ? "&" : "?") + "t=" + DateTime.UtcNow.Ticks).ConfigureAwait(false);
        }
        else
        {
            json = File.ReadAllText(adresse);
        }

        return FicheVersion.Lire(json);
    }

    /// <summary>
    /// Télécharge l'installateur dans %TEMP%, contrôle l'empreinte puis la signature : si MultiDEC.exe est signé,
    /// l'installateur doit porter une signature valide du même éditeur.
    /// </summary>
    public static async Task<string> TelechargerAsync(FicheVersion fiche, string adresseFiche, IProgress<double>? progression)
    {
        string source = fiche.AdresseInstallateur(adresseFiche);
        string nom = Path.GetFileName(new Uri(source, UriKind.RelativeOrAbsolute).IsAbsoluteUri ? new Uri(source).LocalPath : source);
        string dossier = Path.Combine(Path.GetTempPath(), "MultiDEC-mise-a-jour");
        Directory.CreateDirectory(dossier);
        string cible = Path.Combine(dossier, string.IsNullOrEmpty(nom) ? "MultiDEC-v3-Setup.exe" : nom);

        if (EstWeb(source))
        {
            using var client = Client();
            using var reponse = await client.GetAsync(source, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            reponse.EnsureSuccessStatusCode();
            long? total = reponse.Content.Headers.ContentLength;
            using var flux = await reponse.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var fichier = File.Create(cible);
            var tampon = new byte[81920];
            long lus = 0;
            int n;
            while ((n = await flux.ReadAsync(tampon, 0, tampon.Length).ConfigureAwait(false)) > 0)
            {
                await fichier.WriteAsync(tampon, 0, n).ConfigureAwait(false);
                lus += n;
                if (total > 0)
                {
                    progression?.Report((double)lus / total.Value);
                }
            }
        }
        else
        {
            File.Copy(source, cible, true);
            progression?.Report(1);
        }

        using (var lecture = File.OpenRead(cible))
        {
            if (!fiche.EmpreinteConforme(lecture))
            {
                throw new InvalidDataException("L'installateur téléchargé est altéré (empreinte SHA-256 différente de la fiche de version).");
            }
        }

        string? editeur = SignatureAuthenticode.Signataire(typeof(MiseAJourApplication).Assembly.Location);
        if (editeur != null)
        {
            if (!SignatureAuthenticode.Valide(cible))
            {
                throw new InvalidDataException("L'installateur téléchargé n'a pas de signature valide.");
            }

            string? signataire = SignatureAuthenticode.Signataire(cible);
            if (!string.Equals(signataire, editeur, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"L'installateur est signé par « {signataire} » et non par « {editeur} ».");
            }
        }

        return cible;
    }

    /// <summary>Lance l'installateur en mode silencieux : il ferme MultiDEC, installe, puis relance MultiDEC.</summary>
    public static void LancerInstallateur(string installateur)
    {
        Process.Start(new ProcessStartInfo(installateur, "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS") { UseShellExecute = true });
    }
}

/// <summary>Signature Authenticode d'un fichier (WinVerifyTrust) et nom de son signataire.</summary>
internal static class SignatureAuthenticode
{
    /// <summary>Nom simple du certificat signataire (ex. « RETOTUB »), ou null si le fichier n'est pas signé.</summary>
    public static string? Signataire(string fichier)
    {
        try
        {
            var certificat = new X509Certificate2(X509Certificate.CreateFromSignedFile(fichier));
            return certificat.GetNameInfo(X509NameType.SimpleName, false);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }

    /// <summary>Vrai si la signature est valide (chaîne de confiance de Windows, fichier non modifié).</summary>
    public static bool Valide(string fichier)
    {
        var info = new WintrustFileInfo { cbStruct = (uint)Marshal.SizeOf(typeof(WintrustFileInfo)), pcwszFilePath = fichier };
        IntPtr pInfo = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WintrustFileInfo)));
        IntPtr pDonnees = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WintrustData)));
        try
        {
            Marshal.StructureToPtr(info, pInfo, false);
            var donnees = new WintrustData
            {
                cbStruct = (uint)Marshal.SizeOf(typeof(WintrustData)),
                dwUIChoice = 2,          // WTD_UI_NONE
                fdwRevocationChecks = 0, // WTD_REVOKE_NONE
                dwUnionChoice = 1,       // WTD_CHOICE_FILE
                pFile = pInfo,
                dwStateAction = 1,       // WTD_STATEACTION_VERIFY
            };
            Marshal.StructureToPtr(donnees, pDonnees, false);
            int resultat = WinVerifyTrust(IntPtr.Zero, ActionVerification, pDonnees);

            donnees = (WintrustData)Marshal.PtrToStructure(pDonnees, typeof(WintrustData))!;
            donnees.dwStateAction = 2; // WTD_STATEACTION_CLOSE
            Marshal.StructureToPtr(donnees, pDonnees, true);
            WinVerifyTrust(IntPtr.Zero, ActionVerification, pDonnees);
            return resultat == 0;
        }
        finally
        {
            Marshal.DestroyStructure(pInfo, typeof(WintrustFileInfo));
            Marshal.FreeHGlobal(pInfo);
            Marshal.FreeHGlobal(pDonnees);
        }
    }

    private static readonly Guid ActionVerification = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE"); // WINTRUST_ACTION_GENERIC_VERIFY_V2

    [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionId, IntPtr pWvtData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WintrustFileInfo
    {
        public uint cbStruct;
        [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WintrustData
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }
}
