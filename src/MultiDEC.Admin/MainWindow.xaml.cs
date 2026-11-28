using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MultiDEC.Admin.Services;
using MultiDEC.Core.Droits;
using MultiDEC.Core.MiseAJour;

namespace MultiDEC.Admin;

/// <summary>Ligne de la grille des licences.</summary>
public sealed class LigneLicence
{
    public LigneLicence(Licence l)
    {
        Licence = l;
    }

    public Licence Licence { get; }
    public string Code => Licence.Code;
    public string Demandeur => Licence.Demandeur;
    public string Entreprise => Licence.Entreprise;
    public string Email => Licence.Email;
    public DateTime? Expiration => Licence.Expiration;
    public string Notes => Licence.Notes;
    public string Etat => Licence.Suspendue ? "Suspendue" : Licence.EstValideLe(DateTime.Today) ? "Active" : "Expirée";
}

/// <summary>
/// MultiDEC Admin (Laurent, 27/11/2026) : licences des postes, publication de la liste signée et des versions.
/// </summary>
public partial class MainWindow : Window
{
    private ParametresUtilisateur _utilisateur;
    private DonneesAdmin _donnees = null!;
    private ParametresPublication _publication = new ParametresPublication();

    public MainWindow()
    {
        InitializeComponent();
        _utilisateur = ParametresUtilisateur.Charger();
        Loaded += (_, _) => OuvrirDossier(_utilisateur.DossierDonnees);
        Closing += (_, e) =>
        {
            if (_donnees != null && _donnees.Registre.APublier && MessageBox.Show(this, "Des licences ont été modifiées depuis la dernière publication : les postes ne verront ces changements qu'après « Publier la liste ».\n\nQuitter quand même ?",
                    "MultiDEC Admin", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                e.Cancel = true;
            }
        };
    }

    private void Tracer(string message)
    {
        Dispatcher.Invoke(() =>
        {
            Journal.AppendText($"{DateTime.Now:HH:mm:ss}  {message}\r\n");
            Journal.ScrollToEnd();
        });
    }

    // ------------------------------------------------------------------ Données

    private void OuvrirDossier(string dossier)
    {
        try
        {
            var donnees = new DonneesAdmin(dossier);
            donnees.Charger();
            _donnees = donnees;
            _publication = ParametresPublication.Charger(dossier);
            DossierDonnees.Text = dossier;
            AfficherLicences();
            AfficherCle();
            AfficherPublication();
            Tracer($"Dossier de données : {dossier} ({_donnees.Registre.Licences.Count} licence(s)).");
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException || ex is System.Text.Json.JsonException)
        {
            MessageBox.Show(this, $"Impossible d'ouvrir le dossier de données « {dossier} » :\n{ex.Message}", "MultiDEC Admin", MessageBoxButton.OK, MessageBoxImage.Error);
            _donnees ??= new DonneesAdmin(dossier);
        }
    }

    /// <summary>Vrai si une licence a été modifiée après la dernière publication de la liste.</summary>
    private bool ListeAPublier() => _donnees.Registre.APublier;

    private void AfficherLicences()
    {
        string f = Filtre.Text.Trim();
        var lignes = _donnees.Registre.Licences
            .Where(l => f.Length == 0 || new[] { l.Code, l.Demandeur, l.Entreprise, l.Email, l.Notes }.Any(t => t != null && t.IndexOf(f, StringComparison.CurrentCultureIgnoreCase) >= 0))
            .OrderBy(l => l.Entreprise, StringComparer.CurrentCultureIgnoreCase).ThenBy(l => l.Demandeur, StringComparer.CurrentCultureIgnoreCase)
            .Select(l => new LigneLicence(l))
            .ToList();
        Grille.ItemsSource = lignes;
        int actives = _donnees.Registre.Licences.Count(l => l.EstValideLe(DateTime.Today));
        string publication = _donnees.Registre.DernierePublication.HasValue
            ? $"Dernière publication de la liste : {_donnees.Registre.DernierePublication.Value.ToLocalTime():dd/MM/yyyy HH:mm}."
            : "Liste jamais publiée.";
        EtatListe.Text = $"{_donnees.Registre.Licences.Count} licence(s), dont {actives} active(s). {publication}"
                         + (ListeAPublier() ? "  Modifications non publiées : cliquez sur « Publier la liste »." : string.Empty);
    }

    private LigneLicence? Selection => Grille.SelectedItem as LigneLicence;

    private void Filtre_TextChanged(object sender, TextChangedEventArgs e) => AfficherLicences();

    private void Recharger_Click(object sender, RoutedEventArgs e) => OuvrirDossier(_donnees.Dossier);

    private void Nouvelle_Click(object sender, RoutedEventArgs e) => Editer(null);

    private void Modifier_Click(object sender, RoutedEventArgs e)
    {
        if (Selection != null)
        {
            Editer(Selection.Licence);
        }
    }

    private void Editer(Licence? licence)
    {
        var fenetre = new FenetreLicence(licence, _donnees.Registre) { Owner = this };
        if (fenetre.ShowDialog() != true)
        {
            return;
        }

        Modifier(() =>
        {
            var l = _donnees.Registre.Enregistrer(fenetre.Resultat, DateTime.UtcNow);
            return $"{(licence == null ? "Ajout" : "Modification")} de la licence {l.Code} ({l.Demandeur}, {l.Entreprise}{(l.Suspendue ? ", suspendue" : string.Empty)}, expiration {(l.Expiration.HasValue ? l.Expiration.Value.ToString("dd/MM/yyyy") : "sans limite")}).";
        });
    }

    private void Supprimer_Click(object sender, RoutedEventArgs e)
    {
        var s = Selection;
        if (s == null || MessageBox.Show(this, $"Supprimer la licence de {s.Demandeur} ({s.Entreprise}), poste {s.Code} ?\n\nPour retirer temporairement un droit, préférez « Suspendue » dans la fiche : la licence est conservée.",
                "Supprimer la licence", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        Modifier(() =>
        {
            _donnees.Registre.Supprimer(s.Code, DateTime.UtcNow);
            return $"Suppression de la licence {s.Code} ({s.Demandeur}, {s.Entreprise}).";
        });
    }

    /// <summary>Applique une modification du registre, l'enregistre et la trace ; recharge si quelqu'un d'autre l'a modifié.</summary>
    private void Modifier(Func<string> modification)
    {
        if (_donnees.ModifieAilleurs)
        {
            MessageBox.Show(this, "Le registre a été modifié par un autre utilisateur : il va être rechargé. Refaites ensuite votre modification.", "MultiDEC Admin", MessageBoxButton.OK, MessageBoxImage.Information);
            OuvrirDossier(_donnees.Dossier);
            return;
        }

        try
        {
            string action = modification();
            _donnees.Enregistrer();
            _donnees.Tracer(action);
            Tracer(action);
        }
        catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "MultiDEC Admin", MessageBoxButton.OK, MessageBoxImage.Warning);
            _donnees.Charger();
        }

        AfficherLicences();
    }

    private void Ecrire_Click(object sender, RoutedEventArgs e)
    {
        var s = Selection;
        if (s == null || string.IsNullOrWhiteSpace(s.Email))
        {
            MessageBox.Show(this, "Sélectionnez une licence avec une adresse e-mail.", "MultiDEC Admin", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string corps = s.Licence.EstValideLe(DateTime.Today)
            ? $"Bonjour,\r\n\r\nVotre poste (code {s.Code}) est autorisé à utiliser MultiDEC v3"
              + (s.Expiration.HasValue ? $" jusqu'au {s.Expiration.Value:dd/MM/yyyy}" : string.Empty)
              + ". Relancez MultiDEC (connecté à Internet) : il démarrera normalement.\r\n\r\nCordialement,\r\nRETOTUB"
            : $"Bonjour,\r\n\r\nLe droit d'utilisation de MultiDEC v3 pour votre poste (code {s.Code}) n'est plus actif.\r\n\r\nCordialement,\r\nRETOTUB";
        try
        {
            Process.Start(new ProcessStartInfo($"mailto:{s.Email}?subject={Uri.EscapeDataString("Votre droit d'utilisation MultiDEC v3")}&body={Uri.EscapeDataString(corps)}") { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Clipboard.SetText(corps);
            MessageBox.Show(this, "Aucune messagerie n'est configurée : le texte du message a été copié.", "MultiDEC Admin", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async void PublierListe_Click(object sender, RoutedEventArgs e)
    {
        if (!_donnees.CleExiste)
        {
            MessageBox.Show(this, "Aucune clé de signature dans le dossier de données (onglet Paramètres).", "Publier la liste", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_donnees.ModifieAilleurs)
        {
            OuvrirDossier(_donnees.Dossier);
        }

        int n = _donnees.Registre.Licences.Count(l => l.EstValideLe(DateTime.Today));
        string? motDePasse = FenetreMotDePasse.Demander(this, "Publier la liste des postes autorisés",
            $"{n} poste(s) seront autorisés. Mot de passe de la clé de signature :");
        if (motDePasse == null)
        {
            return;
        }

        await Executer(BoutonPublierListe, async () =>
        {
            RSA cle;
            try
            {
                string coffre = File.ReadAllText(_donnees.CheminCle);
                cle = await Task.Run(() => CoffreCle.Ouvrir(coffre, motDePasse));
            }
            catch (UnauthorizedAccessException ex)
            {
                MessageBox.Show(this, ex.Message, "Publier la liste", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            using (cle)
            {
                var publication = new Publication(_publication, _utilisateur.MotDePasseSftp);
                string resume = await Publication.PublierListeAsync(_donnees, publication, cle, Tracer);
                _donnees.Enregistrer();
                _donnees.Tracer($"Publication de la liste : {resume}.");
                Tracer($"Liste publiée : {resume}.");
                AfficherLicences();
            }
        });
    }

    // ------------------------------------------------------------------ Versions

    private void ParcourirInstallateur_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Installateur MultiDEC (*.exe)|*.exe", Title = "Installateur à publier" };
        if (dlg.ShowDialog(this) != true)
        {
            return;
        }

        CheminInstallateur.Text = dlg.FileName;
        var info = FileVersionInfo.GetVersionInfo(dlg.FileName);
        var v = FicheVersion.NumeroDe(info.FileVersion);
        if (v != null)
        {
            NumeroVersion.Text = v.Revision > 0 ? v.ToString(4) : v.ToString(3);
            if (string.IsNullOrWhiteSpace(LibelleVersion.Text))
            {
                LibelleVersion.Text = "v" + NumeroVersion.Text;
            }
        }

        string signature;
        try
        {
            var certificat = new System.Security.Cryptography.X509Certificates.X509Certificate2(System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(dlg.FileName));
            signature = "signé par " + certificat.GetNameInfo(System.Security.Cryptography.X509Certificates.X509NameType.SimpleName, false);
        }
        catch (CryptographicException)
        {
            signature = "NON signé (Windows SmartScreen peut avertir à l'installation)";
        }

        InfoInstallateur.Text = $"{info.ProductName} {info.FileVersion}, {new FileInfo(dlg.FileName).Length / 1048576.0:0.0} Mo, {signature}.";
    }

    private async void LireVersion_Click(object sender, RoutedEventArgs e) => await LireVersionPublieeAsync();

    private async Task<FicheVersion?> LireVersionPublieeAsync()
    {
        if (string.IsNullOrWhiteSpace(_publication.AdressePublique))
        {
            VersionEnLigne.Text = "Version publiée : adresse web non renseignée (onglet Paramètres).";
            return null;
        }

        try
        {
            var fiche = FicheVersion.Lire(await Publication.LireAsync(_publication.AdresseWeb(FicheVersion.NomFichier)));
            VersionEnLigne.Text = $"Version publiée : {fiche.Version} ({fiche.Libelle}), le {fiche.Date?.ToLocalTime():dd/MM/yyyy HH:mm}.";
            return fiche;
        }
        catch (Exception ex)
        {
            VersionEnLigne.Text = "Version publiée : lecture impossible (" + ex.GetBaseException().Message + ").";
            return null;
        }
    }

    private async void PublierVersion_Click(object sender, RoutedEventArgs e)
    {
        string installateur = CheminInstallateur.Text, version = NumeroVersion.Text.Trim();
        if (!File.Exists(installateur) || FicheVersion.NumeroDe(version) == null)
        {
            MessageBox.Show(this, "Choisissez l'installateur et indiquez un numéro de version (ex. 3.0.2).", "Publier la version", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var publiee = await LireVersionPublieeAsync();
        if (publiee != null && !new FicheVersion { Version = version }.EstPlusRecenteQue(FicheVersion.NumeroDe(publiee.Version)!)
            && MessageBox.Show(this, $"La version {version} n'est pas plus récente que celle déjà publiée ({publiee.Version}) : les postes ne la proposeront pas en mise à jour.\n\nPublier quand même ?",
                "Publier la version", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        if (MessageBox.Show(this, $"Publier MultiDEC {version} sur {_publication.AdressePublique} ?\n\nTous les postes connectés se verront proposer cette mise à jour.",
                "Publier la version", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
        {
            return;
        }

        await Executer(BoutonPublierVersion, async () =>
        {
            var publication = new Publication(_publication, _utilisateur.MotDePasseSftp);
            await Publication.PublierVersionAsync(publication, installateur, version, LibelleVersion.Text, NotesVersion.Text, Tracer);
            _donnees.Tracer($"Publication de la version {version} ({Path.GetFileName(installateur)}).");
            Tracer($"Version {version} publiée.");
            await LireVersionPublieeAsync();
        });
    }

    // ------------------------------------------------------------------ Paramètres

    private void AfficherCle()
    {
        if (!_donnees.CleExiste)
        {
            EtatCle.Text = "Aucune clé dans ce dossier. Créez-la une seule fois (ou choisissez le dossier qui la contient), puis transmettez la clé publique pour qu'elle soit intégrée à MultiDEC.";
            BoutonCreerCle.IsEnabled = true;
            return;
        }

        try
        {
            string cle = _donnees.ClePublique();
            int bits = CoffreCle.TailleBits(cle);
            if (bits < CoffreCle.TailleCle)
            {
                EtatCle.Text = $"Clé de {bits} bits seulement, trop courte ({CoffreCle.TailleCle} bits attendus) : cliquez sur « Recréer la clé… » puis transmettez la nouvelle clé publique. "
                               + "Les licences sont conservées.";
                EtatCle.Foreground = System.Windows.Media.Brushes.Firebrick;
                BoutonCreerCle.Content = "Recréer la clé…";
                BoutonCreerCle.IsEnabled = true;
                return;
            }

            EtatCle.Text = $"Clé présente ({CoffreCle.NomFichier}, {bits} bits), empreinte {CoffreCle.Empreinte(cle)}. Elle est protégée par un mot de passe, demandé à chaque publication. "
                           + "Conservez une sauvegarde : sans elle, il faudrait diffuser une nouvelle version de MultiDEC avec une nouvelle clé.";
            EtatCle.ClearValue(TextBlock.ForegroundProperty);
        }
        catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is FormatException)
        {
            EtatCle.Text = "Fichier de clé illisible : " + ex.Message;
        }

        BoutonCreerCle.Content = "Créer la clé…";
        BoutonCreerCle.IsEnabled = false;
    }

    private async void CreerCle_Click(object sender, RoutedEventArgs e)
    {
        bool remplacer = false;
        if (_donnees.CleExiste)
        {
            // Seule une clé trop courte peut être remplacée ici (l'ancienne est conservée à côté).
            if (CoffreCle.EstSuffisante(_donnees.ClePublique())
                || MessageBox.Show(this, "Remplacer la clé actuelle, trop courte, par une nouvelle clé de 3 072 bits ?

L'ancienne est conservée dans le dossier de données sous un autre nom. Il faudra transmettre la nouvelle clé publique pour qu'elle soit intégrée à MultiDEC.",
                    "Recréer la clé", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            remplacer = true;
        }

        string? motDePasse = FenetreMotDePasse.Demander(this, "Créer la clé de signature",
            "Choisissez le mot de passe de la clé (10 caractères au moins). Il sera demandé à chaque publication de la liste ; il n'est enregistré nulle part : notez-le en lieu sûr.", confirmer: true);
        if (motDePasse == null)
        {
            return;
        }

        await Executer(BoutonCreerCle, async () =>
        {
            string coffre = await Task.Run(() =>
            {
                using var rsa = CoffreCle.Generer(() => new RSACng(CoffreCle.TailleCle));
                return CoffreCle.Chiffrer(rsa, motDePasse);
            });
            if (remplacer)
            {
                string ancienne = Path.Combine(_donnees.Dossier, $"cle-signature-remplacee-{DateTime.Now:yyyyMMdd-HHmmss}.json");
                File.Move(_donnees.CheminCle, ancienne);
                _donnees.Tracer($"Remplacement de la clé de signature (ancienne : {Path.GetFileName(ancienne)}).");
            }

            File.WriteAllText(_donnees.CheminCle, coffre);
            _donnees.Tracer($"Création de la clé de signature (empreinte {CoffreCle.Empreinte(CoffreCle.ClePublique(coffre))}).");
            Tracer("Clé de signature créée.");
            AfficherCle();
            Clipboard.SetText(CoffreCle.ClePublique(coffre));
            MessageBox.Show(this, "Clé créée. La clé publique a été copiée dans le presse-papiers : transmettez-la pour qu'elle soit intégrée à MultiDEC (elle n'est pas secrète).\n\nExportez aussi une sauvegarde de la clé.",
                            "Clé de signature", MessageBoxButton.OK, MessageBoxImage.Information);
        });
    }

    private async void ChangerMotDePasseCle_Click(object sender, RoutedEventArgs e)
    {
        if (!_donnees.CleExiste)
        {
            return;
        }

        string? ancien = FenetreMotDePasse.Demander(this, "Changer le mot de passe de la clé", "Mot de passe actuel :");
        if (ancien == null)
        {
            return;
        }

        string? nouveau = FenetreMotDePasse.Demander(this, "Changer le mot de passe de la clé", "Nouveau mot de passe (10 caractères au moins) :", confirmer: true);
        if (nouveau == null)
        {
            return;
        }

        await Executer(null, async () =>
        {
            string coffre = File.ReadAllText(_donnees.CheminCle);
            try
            {
                string nouveauCoffre = await Task.Run(() =>
                {
                    using var rsa = CoffreCle.Ouvrir(coffre, ancien);
                    return CoffreCle.Chiffrer(rsa, nouveau);
                });
                File.Copy(_donnees.CheminCle, _donnees.CheminCle + ".bak", true);
                File.WriteAllText(_donnees.CheminCle, nouveauCoffre);
                _donnees.Tracer("Changement du mot de passe de la clé de signature.");
                Tracer("Mot de passe de la clé changé.");
            }
            catch (UnauthorizedAccessException ex)
            {
                MessageBox.Show(this, ex.Message, "MultiDEC Admin", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        });
    }

    private void CopierClePublique_Click(object sender, RoutedEventArgs e)
    {
        if (_donnees.CleExiste)
        {
            Clipboard.SetText(_donnees.ClePublique());
            Tracer("Clé publique copiée dans le presse-papiers.");
        }
    }

    private void SauvegarderCle_Click(object sender, RoutedEventArgs e)
    {
        if (!_donnees.CleExiste)
        {
            return;
        }

        var dlg = new Microsoft.Win32.SaveFileDialog { FileName = $"cle-signature-multidec-{DateTime.Today:yyyy-MM-dd}.json", Filter = "Clé de signature (*.json)|*.json" };
        if (dlg.ShowDialog(this) == true)
        {
            File.Copy(_donnees.CheminCle, dlg.FileName, true);
            Tracer($"Sauvegarde de la clé : {dlg.FileName} (toujours protégée par son mot de passe).");
        }
    }

    private void ChangerDossier_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = "Dossier de données de MultiDEC Admin (partagé si plusieurs personnes gèrent les licences)", SelectedPath = _donnees.Dossier };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
        {
            return;
        }

        _utilisateur.DossierDonnees = dlg.SelectedPath;
        _utilisateur.Enregistrer();
        OuvrirDossier(dlg.SelectedPath);
    }

    private void AfficherPublication()
    {
        SftpHote.Text = _publication.Hote;
        SftpPort.Text = _publication.Port.ToString();
        SftpUtilisateur.Text = _publication.Utilisateur;
        SftpDossier.Text = _publication.Dossier;
        SftpAdresse.Text = _publication.AdressePublique;
        SftpEmpreinte.Text = _publication.Empreinte;
        SftpMotDePasse.Password = _utilisateur.MotDePasseSftp ?? string.Empty;
    }

    private ParametresPublication LirePublication() => new ParametresPublication
    {
        Hote = SftpHote.Text.Trim(),
        Port = int.TryParse(SftpPort.Text, out int p) ? p : 22,
        Utilisateur = SftpUtilisateur.Text.Trim(),
        Dossier = SftpDossier.Text.Trim(),
        AdressePublique = SftpAdresse.Text.Trim().TrimEnd('/'),
        Empreinte = SftpEmpreinte.Text.Trim(),
    };

    private void EnregistrerSftp_Click(object sender, RoutedEventArgs e)
    {
        _publication = LirePublication();
        _publication.Enregistrer(_donnees.Dossier);
        _utilisateur.MotDePasseSftp = SftpMotDePasse.Password;
        _utilisateur.Enregistrer();
        Tracer("Réglages de publication enregistrés.");
    }

    private async void ScannerEmpreinte_Click(object sender, RoutedEventArgs e)
    {
        var p = LirePublication();
        if (p.Hote.Length == 0)
        {
            return;
        }

        await Executer(sender as Button, async () =>
        {
            string empreinte = await Task.Run(() => Publication.ScannerEmpreinte(p.Hote, p.Port));
            if (MessageBox.Show(this, $"Empreinte de la clé du serveur {p.Hote} :\n\n{empreinte}\n\nEst-ce bien celle du serveur de retotub.com (fournie par l'hébergeur) ?",
                    "Empreinte du serveur", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                SftpEmpreinte.Text = empreinte;
            }
        });
    }

    private async void TesterSftp_Click(object sender, RoutedEventArgs e)
    {
        var p = LirePublication();
        string mdp = SftpMotDePasse.Password;
        await Executer(sender as Button, async () =>
        {
            await Task.Run(() => new Publication(p, mdp).TesterConnexion());
            Tracer($"Connexion SFTP OK ({p.Hote}, dossier {p.Dossier}).");
        });
    }

    private void ImporterScript_Click(object sender, RoutedEventArgs e)
    {
        var import = ParametresPublication.ImporterScript();
        if (import == null)
        {
            MessageBox.Show(this, "Aucune configuration du script sur ce poste pour cet utilisateur (%APPDATA%\\MultiDEC-publication\\sftp.xml).", "MultiDEC Admin", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _publication = import.Value.parametres;
        AfficherPublication();
        if (import.Value.motDePasse != null)
        {
            SftpMotDePasse.Password = import.Value.motDePasse;
        }

        Tracer("Configuration reprise du script de publication : vérifiez-la puis cliquez sur « Enregistrer ».");
    }

    /// <summary>Opération longue : bouton désactivé, sablier, erreurs dans le journal et en message.</summary>
    private async Task Executer(Button? bouton, Func<Task> action)
    {
        if (bouton != null)
        {
            bouton.IsEnabled = false;
        }

        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            App.Journaliser(ex.ToString());
            Tracer("ERREUR : " + ex.GetBaseException().Message);
            Mouse.OverrideCursor = null;
            MessageBox.Show(this, ex.GetBaseException().Message, "MultiDEC Admin", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
            if (bouton != null)
            {
                bouton.IsEnabled = true;
            }

            if (_donnees != null)
            {
                AfficherCle();
            }
        }
    }
}
