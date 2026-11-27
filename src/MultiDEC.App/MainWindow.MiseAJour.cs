using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using MultiDEC.Core.MiseAJour;

namespace MultiDEC.App;

/// <summary>Menu Aide : mises à jour (vérification au démarrage et à la demande) et « À propos ».</summary>
public partial class MainWindow
{
    private bool _rechercheEnCours;

    private void MisesAJour_Click(object sender, RoutedEventArgs e) => _ = RechercherMiseAJourAsync(silencieux: false);

    private void APropos_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(this,
            $"MultiDEC {VersionApplication.Libelle}\n{VersionApplication.Texte} ({MiseAJourApplication.VersionInstallee})\n\n"
            + $"Installé dans : {AppDomain.CurrentDomain.BaseDirectory}\n"
            + $"Mises à jour : {MiseAJourApplication.Adresse ?? "adresse non définie"}\n"
            + $"Journal des erreurs : {App.CheminJournal}\n\n© RETOTUB",
            "À propos de MultiDEC", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>
    /// Cherche une version plus récente. Au démarrage (<paramref name="silencieux"/>), ne dit rien si tout est à jour
    /// ou si la recherche échoue (hors ligne) : l'erreur va seulement au journal.
    /// </summary>
    private async Task RechercherMiseAJourAsync(bool silencieux)
    {
        if (_rechercheEnCours)
        {
            return;
        }

        string? adresse = MiseAJourApplication.Adresse;
        if (adresse == null)
        {
            if (!silencieux)
            {
                MessageBox.Show(this, "L'adresse des mises à jour n'est pas définie sur ce poste (fichier "
                                + MiseAJourApplication.FichierAdresse + " à côté de MultiDEC.exe, posé par l'installateur).",
                                "Mises à jour", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            return;
        }

        _rechercheEnCours = true;
        try
        {
            FicheVersion fiche;
            try
            {
                fiche = await MiseAJourApplication.LireFicheAsync(adresse);
            }
            catch (Exception ex)
            {
                App.Journaliser(ex);
                if (!silencieux)
                {
                    MessageBox.Show(this, "Impossible de joindre le serveur des mises à jour :\n" + ex.GetBaseException().Message,
                                    "Mises à jour", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                return;
            }

            var installee = MiseAJourApplication.VersionInstallee;
            if (!fiche.EstPlusRecenteQue(installee))
            {
                if (!silencieux)
                {
                    MessageBox.Show(this, $"MultiDEC est à jour ({VersionApplication.Libelle}).", "Mises à jour", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                return;
            }

            string libelle = string.IsNullOrWhiteSpace(fiche.Libelle) ? fiche.Version : fiche.Libelle!;
            string notes = string.IsNullOrWhiteSpace(fiche.Notes) ? string.Empty : "\n\nNouveautés :\n" + fiche.Notes;
            var reponse = MessageBox.Show(this,
                $"Une nouvelle version de MultiDEC est disponible : {libelle} (vous avez {VersionApplication.Libelle}).{notes}\n\n"
                + "L'installer maintenant ? MultiDEC se fermera puis se relancera : enregistrez d'abord votre projet.",
                "Mise à jour disponible", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (reponse != MessageBoxResult.Yes)
            {
                return;
            }

            await InstallerAsync(fiche, adresse);
        }
        finally
        {
            _rechercheEnCours = false;
        }
    }

    private async Task InstallerAsync(FicheVersion fiche, string adresse)
    {
        var barre = new ProgressBar { Height = 18, Minimum = 0, Maximum = 1, Margin = new Thickness(16, 8, 16, 16) };
        var fenetre = new Window
        {
            Title = "Mise à jour de MultiDEC",
            Owner = this,
            Width = 380,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Content = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = "Téléchargement de la nouvelle version…", Margin = new Thickness(16, 16, 16, 0) },
                    barre,
                },
            },
        };
        fenetre.Show();

        string installateur;
        try
        {
            installateur = await MiseAJourApplication.TelechargerAsync(fiche, adresse, new Progress<double>(v => barre.Value = v));
        }
        catch (Exception ex)
        {
            App.Journaliser(ex);
            fenetre.Close();
            MessageBox.Show(this, "Mise à jour impossible : " + ex.GetBaseException().Message, "Mises à jour", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        fenetre.Close();
        try
        {
            MiseAJourApplication.LancerInstallateur(installateur);
        }
        catch (Exception ex)
        {
            App.Journaliser(ex);
            MessageBox.Show(this, "L'installateur n'a pas pu être lancé : " + ex.Message + "\n\nVous pouvez le lancer vous-même : " + installateur,
                            "Mises à jour", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Application.Current.Shutdown();
    }
}
