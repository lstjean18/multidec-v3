using System;
using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MultiDEC.App;
using Xunit;
using Xunit.Abstractions;

namespace MultiDEC.Rendering.Tests;

/// <summary>Remarque 64 : champs WPF au séparateur décimal du poste, touches « . » et « , » acceptées.</summary>
public class SeparateurDecimalEcranTests
{
    private readonly ITestOutputHelper _sortie;

    public SeparateurDecimalEcranTests(ITestOutputHelper sortie) => _sortie = sortie;

    public sealed class Modele
    {
        public double Valeur { get; set; } = 1.5;

        public int Nombre { get; set; } = 3;
    }

    [Theory]
    [InlineData(".")]
    [InlineData(",")]
    public void Champ_lie_selon_le_separateur_du_poste(string separateur)
    {
        Exception? erreur = null;
        var fil = new Thread(() =>
        {
            try
            {
                var culture = (CultureInfo)CultureInfo.GetCultureInfo("fr-FR").Clone();
                culture.NumberFormat.NumberDecimalSeparator = separateur;
                Thread.CurrentThread.CurrentCulture = culture;
                Verifier(separateur);
            }
            catch (Exception ex)
            {
                erreur = ex;
            }
        });
        fil.SetApartmentState(ApartmentState.STA);
        fil.Start();
        fil.Join();
        if (erreur != null)
        {
            throw new Exception(erreur.Message, erreur);
        }
    }

    private void Verifier(string separateur)
    {
        Localisation.Installer();
        var modele = new Modele();
        var champ = new TextBox();
        champ.SetBinding(TextBox.TextProperty, new Liaison(nameof(Modele.Valeur)) { Source = modele, StringFormat = "0.00" });
        _sortie.WriteLine($"Séparateur {separateur} : affiché « {champ.Text} »");
        Assert.Equal("1" + separateur + "50", champ.Text);

        // Saisie avec le séparateur du poste.
        champ.Text = "2" + separateur + "25";
        champ.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
        Assert.Equal(2.25, modele.Valeur, 9);

        // Touche de l'autre signe : remplacée par le séparateur du poste.
        champ.Text = "3";
        champ.CaretIndex = 1;
        string autre = separateur == "." ? "," : ".";
        var frappe = new TextCompositionEventArgs(Keyboard.PrimaryDevice, new TextComposition(InputManager.Current, champ, autre))
        {
            RoutedEvent = UIElement.PreviewTextInputEvent,
        };
        champ.RaiseEvent(frappe);
        _sortie.WriteLine($"Touche « {autre} » : texte « {champ.Text} », traité {frappe.Handled}");
        Assert.True(frappe.Handled);
        Assert.Equal("3" + separateur, champ.Text);
        champ.Text += "5";
        champ.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
        Assert.Equal(3.5, modele.Valeur, 9);

        // Champ entier : rien n'est remplacé.
        var entier = new TextBox();
        entier.SetBinding(TextBox.TextProperty, new Liaison(nameof(Modele.Nombre)) { Source = modele });
        var frappeEntier = new TextCompositionEventArgs(Keyboard.PrimaryDevice, new TextComposition(InputManager.Current, entier, autre))
        {
            RoutedEvent = UIElement.PreviewTextInputEvent,
        };
        entier.RaiseEvent(frappeEntier);
        Assert.False(frappeEntier.Handled);
    }
}
