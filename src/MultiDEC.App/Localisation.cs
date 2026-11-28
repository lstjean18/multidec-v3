using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using MultiDEC.Core;

namespace MultiDEC.App;

/// <summary>
/// Liaison de données qui lit et écrit les nombres avec le séparateur décimal du poste (remarque 64, Laurent 28/11/2026).
/// WPF prend la culture de la langue (fr-FR, en-US…) sans les réglages personnalisés de Windows ; ici, c'est la culture
/// courante du poste, réglages compris. À utiliser à la place de {Binding} dans les fenêtres.
/// </summary>
public sealed class Liaison : Binding
{
    public Liaison() => ConverterCulture = CultureInfo.CurrentCulture;

    public Liaison(string path) : base(path) => ConverterCulture = CultureInfo.CurrentCulture;
}

/// <summary>Séparateur décimal du poste à l'écran : saisie, textes fixes des fenêtres, messages.</summary>
internal static class Localisation
{
    private static bool _installe;

    /// <summary>
    /// Dans un champ lié à un nombre décimal, la touche « . » comme la touche « , » écrivent le séparateur du poste :
    /// « 1.50 » et « 1,50 » sont acceptés partout.
    /// </summary>
    public static void Installer()
    {
        if (_installe)
        {
            return;
        }

        _installe = true;
        EventManager.RegisterClassHandler(typeof(TextBox), UIElement.PreviewTextInputEvent, new TextCompositionEventHandler(Frappe));
        EventManager.RegisterClassHandler(typeof(TextBox), DataObject.PastingEvent, new DataObjectPastingEventHandler(Collage));
    }

    private static void Frappe(object sender, TextCompositionEventArgs e)
    {
        if (sender is not TextBox champ || (e.Text != "." && e.Text != ",") || !EstDecimal(champ))
        {
            return;
        }

        string separateur = CultureNombres.Separateur;
        if (e.Text == separateur)
        {
            return;
        }

        e.Handled = true;
        int position = champ.SelectionStart;
        champ.SelectedText = separateur;
        champ.SelectionLength = 0;
        champ.CaretIndex = position + separateur.Length;
    }

    private static void Collage(object sender, DataObjectPastingEventArgs e)
    {
        if (sender is not TextBox champ || !EstDecimal(champ) || e.DataObject.GetData(DataFormats.UnicodeText) is not string texte)
        {
            return;
        }

        string normal = CultureNombres.Normaliser(texte.Trim());
        if (normal != texte)
        {
            var donnees = new DataObject();
            donnees.SetData(DataFormats.UnicodeText, normal);
            e.DataObject = donnees;
        }
    }

    /// <summary>Champ dont le texte est lié à une propriété double, float ou decimal (éventuellement nullable).</summary>
    private static bool EstDecimal(TextBox champ)
    {
        var expression = BindingOperations.GetBindingExpression(champ, TextBox.TextProperty);
        string? nom = expression?.ResolvedSourcePropertyName;
        object? source = expression?.ResolvedSource;
        if (source == null || string.IsNullOrEmpty(nom) || nom!.StartsWith("[", StringComparison.Ordinal))
        {
            return false;
        }

        Type? type = source.GetType().GetProperty(nom, BindingFlags.Public | BindingFlags.Instance)?.PropertyType;
        type = type == null ? null : Nullable.GetUnderlyingType(type) ?? type;
        return type == typeof(double) || type == typeof(float) || type == typeof(decimal);
    }

    /// <summary>
    /// Textes fixes d'une fenêtre (« Lisse GC accès 1,50 m »…) remis au séparateur du poste ; les textes liés ne sont
    /// pas touchés. Sans effet sur un poste à virgule.
    /// </summary>
    public static void LocaliserTextes(DependencyObject racine)
    {
        if (CultureNombres.Separateur == ",")
        {
            return;
        }

        Parcourir(racine);
    }

    private static void Parcourir(DependencyObject element)
    {
        switch (element)
        {
            case TextBlock t when !Lie(t, TextBlock.TextProperty) && t.Inlines.Count <= 1:
                t.Text = CultureNombres.Localiser(t.Text);
                break;
            case HeaderedContentControl h when h.Header is string entete && !Lie(h, HeaderedContentControl.HeaderProperty):
                h.Header = CultureNombres.Localiser(entete);
                break;
            case HeaderedItemsControl m when m.Header is string entete && !Lie(m, HeaderedItemsControl.HeaderProperty):
                m.Header = CultureNombres.Localiser(entete);
                break;
            case DataGrid grille:
                foreach (var colonne in grille.Columns.Where(c => c.Header is string))
                {
                    colonne.Header = CultureNombres.Localiser((string)colonne.Header);
                }

                break;
        }

        if (element is ContentControl c && c.Content is string contenu && !Lie(c, ContentControl.ContentProperty))
        {
            c.Content = CultureNombres.Localiser(contenu);
        }

        if (element is FrameworkElement f && f.ToolTip is string bulle && !Lie(f, FrameworkElement.ToolTipProperty))
        {
            f.ToolTip = CultureNombres.Localiser(bulle);
        }

        foreach (var enfant in LogicalTreeHelper.GetChildren(element).OfType<DependencyObject>())
        {
            Parcourir(enfant);
        }
    }

    private static bool Lie(DependencyObject element, DependencyProperty propriete) =>
        BindingOperations.IsDataBound(element, propriete);
}

/// <summary>MessageBox dont le texte est remis au séparateur décimal du poste (règles, alertes, refus de dessiner).</summary>
internal static class Boite
{
    public static MessageBoxResult Show(string texte) => MessageBox.Show(CultureNombres.Localiser(texte));

    public static MessageBoxResult Show(string texte, string titre) => MessageBox.Show(CultureNombres.Localiser(texte), titre);

    public static MessageBoxResult Show(string texte, string titre, MessageBoxButton boutons) =>
        MessageBox.Show(CultureNombres.Localiser(texte), titre, boutons);

    public static MessageBoxResult Show(string texte, string titre, MessageBoxButton boutons, MessageBoxImage icone) =>
        MessageBox.Show(CultureNombres.Localiser(texte), titre, boutons, icone);

    public static MessageBoxResult Show(string texte, string titre, MessageBoxButton boutons, MessageBoxImage icone, MessageBoxResult defaut) =>
        MessageBox.Show(CultureNombres.Localiser(texte), titre, boutons, icone, defaut);

    public static MessageBoxResult Show(Window proprietaire, string texte) => MessageBox.Show(proprietaire, CultureNombres.Localiser(texte));

    public static MessageBoxResult Show(Window proprietaire, string texte, string titre) =>
        MessageBox.Show(proprietaire, CultureNombres.Localiser(texte), titre);

    public static MessageBoxResult Show(Window proprietaire, string texte, string titre, MessageBoxButton boutons) =>
        MessageBox.Show(proprietaire, CultureNombres.Localiser(texte), titre, boutons);

    public static MessageBoxResult Show(Window proprietaire, string texte, string titre, MessageBoxButton boutons, MessageBoxImage icone) =>
        MessageBox.Show(proprietaire, CultureNombres.Localiser(texte), titre, boutons, icone);

    public static MessageBoxResult Show(Window proprietaire, string texte, string titre, MessageBoxButton boutons, MessageBoxImage icone, MessageBoxResult defaut) =>
        MessageBox.Show(proprietaire, CultureNombres.Localiser(texte), titre, boutons, icone, defaut);
}
