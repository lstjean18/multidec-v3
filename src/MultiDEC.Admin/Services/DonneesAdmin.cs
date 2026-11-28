using System;
using System.IO;
using MultiDEC.Core.Droits;

namespace MultiDEC.Admin.Services;

/// <summary>
/// Dossier de données partagé de MultiDEC Admin : licences.json (registre), cle-signature.json (clé privée chiffrée par
/// mot de passe), publication.json (serveur), postes-autorises.json (dernière liste publiée), journal.txt (qui a fait quoi).
/// Plusieurs personnes peuvent l'utiliser (lecteur réseau) : un enregistrement est refusé si le registre a été modifié
/// par quelqu'un d'autre depuis son chargement.
/// </summary>
public sealed class DonneesAdmin
{
    private DateTime _dateLecture;

    public DonneesAdmin(string dossier)
    {
        Dossier = dossier;
    }

    public string Dossier { get; }

    public string CheminRegistre => Path.Combine(Dossier, RegistreLicences.NomFichier);

    public string CheminCle => Path.Combine(Dossier, CoffreCle.NomFichier);

    public string CheminListe => Path.Combine(Dossier, ListePostes.NomFichier);

    public string CheminJournal => Path.Combine(Dossier, "journal.txt");

    public RegistreLicences Registre { get; private set; } = new RegistreLicences();

    public bool CleExiste => File.Exists(CheminCle);

    public void Charger()
    {
        Directory.CreateDirectory(Dossier);
        Registre = File.Exists(CheminRegistre) ? RegistreLicences.Lire(File.ReadAllText(CheminRegistre)) : new RegistreLicences();
        _dateLecture = File.Exists(CheminRegistre) ? File.GetLastWriteTimeUtc(CheminRegistre) : DateTime.MinValue;
    }

    /// <summary>Vrai si un autre utilisateur a enregistré le registre depuis le chargement.</summary>
    public bool ModifieAilleurs => File.Exists(CheminRegistre) && File.GetLastWriteTimeUtc(CheminRegistre) != _dateLecture;

    public void Enregistrer()
    {
        if (ModifieAilleurs)
        {
            throw new IOException("Le registre des licences a été modifié par un autre utilisateur depuis son ouverture. Rechargez-le, puis refaites la modification.");
        }

        string temporaire = CheminRegistre + ".tmp";
        File.WriteAllText(temporaire, Registre.Ecrire());
        if (File.Exists(CheminRegistre))
        {
            File.Copy(CheminRegistre, CheminRegistre + ".bak", true);
            File.Delete(CheminRegistre);
        }

        File.Move(temporaire, CheminRegistre);
        _dateLecture = File.GetLastWriteTimeUtc(CheminRegistre);
    }

    public string ClePublique() => CoffreCle.ClePublique(File.ReadAllText(CheminCle));

    public void Tracer(string action)
    {
        try
        {
            File.AppendAllText(CheminJournal, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {Environment.UserName}@{Environment.MachineName}  {action}\r\n");
        }
        catch (IOException ex)
        {
            App.Journaliser(ex.ToString());
        }
    }
}
