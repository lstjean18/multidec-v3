using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using MultiDEC.Core.Droits;

namespace MultiDEC.Admin;

/// <summary>Fiche d'une licence : code du poste (contrôlé), demandeur, entreprise, e-mail, expiration, suspension.</summary>
public partial class FenetreLicence : Window
{
    private readonly RegistreLicences _registre;
    private readonly Licence? _origine;

    public FenetreLicence(Licence? licence, RegistreLicences registre)
    {
        InitializeComponent();
        _registre = registre;
        _origine = licence;
        Title = licence == null ? "Nouvelle licence" : "Licence " + licence.Code;
        var l = licence ?? new Licence { Expiration = DateTime.Today.AddYears(1) };
        Code.Text = l.Code;
        Code.IsReadOnly = licence != null;
        Demandeur.Text = l.Demandeur;
        Entreprise.Text = l.Entreprise;
        Email.Text = l.Email;
        Expiration.SelectedDate = l.Expiration;
        SansLimite.IsChecked = !l.Expiration.HasValue;
        Suspendue.IsChecked = l.Suspendue;
        Notes.Text = l.Notes;
        Loaded += (_, _) => (licence == null ? Code : Demandeur).Focus();
    }

    public Licence Resultat { get; private set; } = new Licence();

    private void Code_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        string? n = CodePoste.Normaliser(Code.Text);
        if (Code.Text.Trim().Length == 0)
        {
            EtatCode.Text = "16 caractères, envoyés par le demandeur (MultiDEC les affiche au démarrage).";
            EtatCode.Foreground = Brushes.Gray;
        }
        else if (n == null)
        {
            EtatCode.Text = "Code incomplet ou faute de frappe (la clé de contrôle ne correspond pas).";
            EtatCode.Foreground = Brushes.Firebrick;
        }
        else if (_origine == null && _registre.Trouver(n) is Licence existante)
        {
            EtatCode.Text = $"Ce poste a déjà une licence ({existante.Demandeur}, {existante.Entreprise}) : elle sera remplacée.";
            EtatCode.Foreground = Brushes.DarkOrange;
        }
        else
        {
            EtatCode.Text = "Code valide.";
            EtatCode.Foreground = Brushes.SeaGreen;
        }
    }

    private void CollerEmail_Click(object sender, RoutedEventArgs e)
    {
        string texte = Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;
        string? code = RegistreLicences.ExtraireCode(texte);
        if (code == null)
        {
            MessageBox.Show(this, "Aucun code de poste valide dans le texte copié. Copiez tout le texte de l'e-mail de demande, puis réessayez.", "Coller l'e-mail", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!Code.IsReadOnly)
        {
            Code.Text = code;
        }

        // Champs remplis par le demandeur dans l'e-mail préparé par MultiDEC.
        Remplir(Entreprise, texte, "Société");
        Remplir(Demandeur, texte, "Nom et prénom");
        if (Email.Text.Length == 0)
        {
            var m = Regex.Match(texte, @"[\w.+-]+@[\w-]+(\.[\w-]+)+");
            if (m.Success && !m.Value.EndsWith("@retotub.com", StringComparison.OrdinalIgnoreCase))
            {
                Email.Text = m.Value;
            }
        }
    }

    private static void Remplir(System.Windows.Controls.TextBox champ, string texte, string libelle)
    {
        var m = Regex.Match(texte, "^\\s*" + Regex.Escape(libelle) + "\\s*:[ \\t]*(.+)$", RegexOptions.Multiline);
        if (champ.Text.Length == 0 && m.Success)
        {
            champ.Text = m.Groups[1].Value.Trim();
        }
    }

    private void SansLimite_Changed(object sender, RoutedEventArgs e) => Expiration.IsEnabled = SansLimite.IsChecked != true;

    private void UnAn_Click(object sender, RoutedEventArgs e)
    {
        DateTime depart = Expiration.SelectedDate.HasValue && Expiration.SelectedDate.Value > DateTime.Today ? Expiration.SelectedDate.Value : DateTime.Today;
        Expiration.SelectedDate = depart.AddYears(1);
        SansLimite.IsChecked = false;
    }

    private void Enregistrer_Click(object sender, RoutedEventArgs e)
    {
        if (CodePoste.Normaliser(Code.Text) == null)
        {
            MessageBox.Show(this, "Code du poste invalide.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (Demandeur.Text.Trim().Length == 0 || Entreprise.Text.Trim().Length == 0)
        {
            MessageBox.Show(this, "Indiquez le demandeur et son entreprise.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (SansLimite.IsChecked != true && !Expiration.SelectedDate.HasValue)
        {
            MessageBox.Show(this, "Indiquez une date d'expiration, ou cochez « Sans limite ».", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Resultat = new Licence
        {
            Code = Code.Text,
            Demandeur = Demandeur.Text,
            Entreprise = Entreprise.Text,
            Email = Email.Text,
            Expiration = SansLimite.IsChecked == true ? null : Expiration.SelectedDate?.Date,
            Suspendue = Suspendue.IsChecked == true,
            Notes = Notes.Text.Trim(),
        };
        DialogResult = true;
    }
}
