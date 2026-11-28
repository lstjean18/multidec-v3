using System;
using System.Windows;
using MultiDEC.Core.Droits;

namespace MultiDEC.Admin;

/// <summary>Saisie du mot de passe de la clé de signature (jamais enregistré).</summary>
public partial class FenetreMotDePasse : Window
{
    private readonly bool _confirmer;

    private FenetreMotDePasse(string titre, string message, bool confirmer)
    {
        InitializeComponent();
        Title = titre;
        Message.Text = message;
        _confirmer = confirmer;
        if (confirmer)
        {
            LibelleConfirmation.Visibility = Visibility.Visible;
            Confirmation.Visibility = Visibility.Visible;
        }

        Loaded += (_, _) => MotDePasse.Focus();
    }

    /// <summary>Mot de passe saisi, ou null si annulé.</summary>
    public static string? Demander(Window proprietaire, string titre, string message, bool confirmer = false)
    {
        var f = new FenetreMotDePasse(titre, message, confirmer) { Owner = proprietaire };
        return f.ShowDialog() == true ? f.MotDePasse.Password : null;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (_confirmer)
        {
            try
            {
                CoffreCle.VerifierMotDePasse(MotDePasse.Password);
            }
            catch (ArgumentException ex)
            {
                MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (MotDePasse.Password != Confirmation.Password)
            {
                MessageBox.Show(this, "Les deux saisies sont différentes.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        DialogResult = true;
    }
}
