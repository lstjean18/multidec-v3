using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using MultiDEC.Core.Droits;

namespace MultiDEC.App.Droits;

/// <summary>
/// Fenêtre des droits d'utilisation : au démarrage quand le poste est refusé (Réessayer / Quitter), ou depuis le menu
/// Aide pour consulter le code du poste et l'état des droits.
/// </summary>
public partial class FenetreDroits : Window
{
    private readonly bool _blocage;
    private string _code;

    private FenetreDroits(ResultatDroits? resultat, string code, bool blocage)
    {
        InitializeComponent();
        _blocage = blocage;
        _code = code;
        Adresse.Text = DroitsApplication.AdresseDemandes;
        if (!blocage)
        {
            BoutonReessayer.Content = "Vérifier maintenant";
            BoutonFermer.Content = "Fermer";
        }

        Afficher(resultat, code);
    }

    /// <summary>
    /// Poste refusé au démarrage : fenêtre modale. Renvoie le résultat autorisé obtenu par « Réessayer », ou null si
    /// l'utilisateur quitte.
    /// </summary>
    internal static ResultatDroits? Bloquer(ResultatDroits? resultat, string code, string? erreur = null)
    {
        var fenetre = new FenetreDroits(resultat, code, blocage: true);
        if (erreur != null)
        {
            fenetre.Message.Text = erreur;
        }

        return fenetre.ShowDialog() == true ? fenetre.Resultat : null;
    }

    /// <summary>Consultation depuis le menu Aide.</summary>
    internal static void Consulter(Window proprietaire)
    {
        var code = DroitsApplication.DernierCode ?? SafeCode();
        new FenetreDroits(DroitsApplication.DernierResultat, code, blocage: false) { Owner = proprietaire, WindowStartupLocation = WindowStartupLocation.CenterOwner }.ShowDialog();
    }

    internal ResultatDroits? Resultat { get; private set; }

    private static string SafeCode()
    {
        try
        {
            return DroitsApplication.CodeDuPoste();
        }
        catch (Exception ex)
        {
            App.Journaliser(ex);
            return "indisponible";
        }
    }

    private void Afficher(ResultatDroits? resultat, string code)
    {
        Code.Text = code;
        Resultat = resultat;
        if (resultat == null)
        {
            Titre.Text = "Droits d'utilisation";
            Message.Text = "Les droits de ce poste n'ont pas encore été vérifiés.";
            Details.Text = string.Empty;
            return;
        }

        Titre.Text = resultat.Autorise ? "Ce poste est autorisé à utiliser MultiDEC" : resultat.Motif switch
        {
            MotifDroits.Expire => "Droit d'utilisation expiré",
            MotifDroits.VerificationNecessaire => "Vérification des droits impossible",
            MotifDroits.HorlogeIncoherente => "Date du poste incorrecte",
            _ => "Ce poste n'est pas autorisé à utiliser MultiDEC",
        };
        Message.Text = resultat.Avertissement != null ? resultat.Message + "\n\n" + resultat.Avertissement : resultat.Message;
        string details = resultat.DerniereVerification.HasValue ? $"Dernière vérification réussie : {resultat.DerniereVerification.Value.ToLocalTime():dd/MM/yyyy HH:mm}." : string.Empty;
        if (resultat.Expiration.HasValue)
        {
            details += $" Droit valable jusqu'au {resultat.Expiration.Value:dd/MM/yyyy}.";
        }

        Details.Text = details.Trim();
    }

    private void Copier_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_code);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Presse-papiers occupé par une autre application : le code reste sélectionnable.
            Code.SelectAll();
            Code.Focus();
        }
    }

    private void Demander_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(DroitsApplication.LienDemande(_code)) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException)
        {
            Copier_Click(sender, e);
            Boite.Show(this, $"Aucune messagerie n'est configurée sur ce poste.\n\nLe code du poste a été copié : envoyez-le par e-mail à {DroitsApplication.AdresseDemandes}.",
                            "Demander l'accès", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async void Reessayer_Click(object sender, RoutedEventArgs e)
    {
        BoutonReessayer.IsEnabled = false;
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            var resultat = await Task.Run(DroitsApplication.Verifier);
            _code = DroitsApplication.DernierCode ?? _code;
            Afficher(resultat, _code);
            if (_blocage && resultat.Autorise)
            {
                DialogResult = true;
            }
        }
        catch (Exception ex)
        {
            App.Journaliser(ex);
            Message.Text = "Vérification impossible : " + ex.GetBaseException().Message;
        }
        finally
        {
            Mouse.OverrideCursor = null;
            BoutonReessayer.IsEnabled = true;
        }
    }

    private void Fermer_Click(object sender, RoutedEventArgs e)
    {
        if (_blocage)
        {
            DialogResult = false;
        }
        else
        {
            Close();
        }
    }
}
