using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using Microsoft.Win32;
using MultiDEC.App.ViewModels;
using MultiDEC.Core.Facade;
using MultiDEC.Core.Persistance;
using MultiDEC.Rendering.VectorDraw;

namespace MultiDEC.App;

/// <summary>
/// Fenêtre principale : héberge le contrôle VectorDraw (WinForms) et relie le ViewModel au rendu.
/// </summary>
public partial class MainWindow : Window
{
    private const string FiltreProjet = "Projet MultiDEC v3 (*" + FichierProjet.Extension + ")|*" + FichierProjet.Extension;

    private readonly vdScrollableControl.vdScrollableControl _vd = new vdScrollableControl.vdScrollableControl();
    private readonly TableOrientations _orientations;
    private RenduVectorDraw? _rendu;

    /// <summary>Colonnes « ConXm » ajoutées à la grille des mailles (une par niveau avec console).</summary>
    private readonly List<DataGridColumn> _colonnesConsoles = new List<DataGridColumn>();

    public MainWindow()
    {
        InitializeComponent();
        HoteVectorDraw.Child = _vd;
        MenuContextuel();
        Vm.ColonnesConsolesChangees += (_, _) => ColonnesConsoles();
        ColonnesConsoles();
        _orientations = TableOrientations.Charger(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "orientations-blocs.json"));
        Loaded += (_, _) => ChargerBibliotheque(Reglages.CheminBibliotheque);
    }

    private FacadeViewModel Vm => (FacadeViewModel)DataContext;

    private void ChargerBibliotheque(string? chemin)
    {
        if (string.IsNullOrEmpty(chemin) || !File.Exists(chemin))
        {
            TexteBibliotheque.Text = "Bibliothèque de blocs non choisie (Fichier > Bibliothèque de blocs…)";
            return;
        }

        try
        {
            _vd.BaseControl.EnsureDocument();
            _rendu = new RenduVectorDraw(_vd.BaseControl.ActiveDocument);
            _rendu.ChargerBibliotheque(chemin!);
            _vd.ReFillTabs(); // onglets des présentations masqués : seul le modèle reste
            Reglages.CheminBibliotheque = chemin;
            TexteBibliotheque.Text = "Bibliothèque : " + Path.GetFileName(chemin);
        }
        catch (Exception ex)
        {
            _rendu = null;
            MessageBox.Show(this, ex.Message, "Bibliothèque de blocs", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Dessiner_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var structure = Vm.Generer();
            if (_rendu == null)
            {
                MessageBox.Show(this, "Choisissez d'abord la bibliothèque de blocs (Blocs-EM-3D.dxf). Le décompte est calculé.", "Dessin", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var rapport = _rendu.Dessiner(structure, _orientations);
            if (rapport.BlocsManquants.Count > 0)
            {
                Vm.Alertes.Add("Blocs absents de la bibliothèque : " + string.Join(", ", rapport.BlocsManquants));
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Dessin de la façade", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Vue_Click(object sender, RoutedEventArgs e)
    {
        if (_rendu != null && sender is FrameworkElement b && Enum.TryParse((string)b.Tag, out Vues vue))
        {
            _rendu.Vue(vue);
        }
    }

    private void Rendu_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_rendu != null && ChoixRendu.SelectedItem is FrameworkElement item && Enum.TryParse((string)item.Tag, out ModeRendu mode))
        {
            _rendu.Rendu(mode);
        }
    }

    /// <summary>Clic droit dans le dessin : supprimer les blocs sélectionnés (le décompte suit).</summary>
    private void MenuContextuel()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        var supprimer = new System.Windows.Forms.ToolStripMenuItem("Supprimer la sélection");
        supprimer.Click += (_, _) => SupprimerSelection();
        menu.Items.Add(supprimer);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        var zoom = new System.Windows.Forms.ToolStripMenuItem("Zoom tout");
        zoom.Click += (_, _) => _rendu?.ZoomTout();
        menu.Items.Add(zoom);
        menu.Opening += (_, _) =>
        {
            int n = _rendu?.NombreSelectionnes ?? 0;
            supprimer.Enabled = n > 0;
            supprimer.Text = n > 1 ? $"Supprimer les {n} blocs sélectionnés" : "Supprimer la sélection";
        };
        _vd.BaseControl.ContextMenuStrip = menu;
    }

    private void SupprimerSelection()
    {
        if (_rendu == null)
        {
            return;
        }

        var retires = _rendu.SupprimerSelection();
        Vm.RetirerElements(retires);
    }

    /// <summary>
    /// Une colonne de cases par niveau avec console, titrée « Con » + hauteur (ex. « Con4m ») :
    /// cochée = la maille porte la console de ce niveau (Laurent, 23/11/2026).
    /// </summary>
    private void ColonnesConsoles()
    {
        foreach (var c in _colonnesConsoles)
        {
            GrilleMailles.Columns.Remove(c);
        }

        _colonnesConsoles.Clear();
        foreach (var n in Vm.NiveauxAvecConsole)
        {
            var caseACocher = new FrameworkElementFactory(typeof(CheckBox));
            caseACocher.SetBinding(ToggleButton.IsCheckedProperty, new Binding("[" + MailleDefinition.Cle(n.Hauteur) + "]")
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            });
            caseACocher.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            caseACocher.SetValue(ToolTipProperty, $"Console {n.Console} au niveau {n.Hauteur:0.00} m");

            var colonne = new DataGridTemplateColumn
            {
                Header = n.NomColonneConsole,
                CellTemplate = new DataTemplate { VisualTree = caseACocher },
                Width = DataGridLength.Auto,
            };
            GrilleMailles.Columns.Add(colonne);
            _colonnesConsoles.Add(colonne);
        }
    }

    private void ZoomTout_Click(object sender, RoutedEventArgs e) => _rendu?.ZoomTout();

    private void Axes_Click(object sender, RoutedEventArgs e) => _rendu?.AxesFilaires(CaseAxes.IsChecked == true);

    private void Modele_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => Vm.ModeleChange();

    private void Nouveau_Click(object sender, RoutedEventArgs e) => Vm.Nouveau();

    private void Ouvrir_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = FiltreProjet };
        if (dlg.ShowDialog(this) == true)
        {
            try
            {
                Vm.Ouvrir(dlg.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Ouvrir", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void Enregistrer_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.CheminFichier == null)
        {
            EnregistrerSous_Click(sender, e);
            return;
        }

        Vm.Enregistrer(Vm.CheminFichier);
    }

    private void EnregistrerSous_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { Filter = FiltreProjet, FileName = string.IsNullOrEmpty(Vm.Projet.ReferenceEtude) ? "facade" : Vm.Projet.ReferenceEtude };
        if (dlg.ShowDialog(this) == true)
        {
            Vm.Enregistrer(dlg.FileName);
        }
    }

    private void Bibliotheque_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Bibliothèque de blocs (*.dxf;*.dwg)|*.dxf;*.dwg" };
        if (dlg.ShowDialog(this) == true)
        {
            ChargerBibliotheque(dlg.FileName);
        }
    }
}

/// <summary>Réglages du poste, dans %APPDATA%\MultiDEC\v3\ (un fichier texte par réglage).</summary>
internal static class Reglages
{
    private static readonly string Dossier = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MultiDEC", "v3");

    public static string? CheminBibliotheque
    {
        get => Lire("bibliotheque.txt");
        set => Ecrire("bibliotheque.txt", value);
    }

    private static string? Lire(string nom)
    {
        string f = Path.Combine(Dossier, nom);
        return File.Exists(f) ? File.ReadAllLines(f).FirstOrDefault() : null;
    }

    private static void Ecrire(string nom, string? valeur)
    {
        Directory.CreateDirectory(Dossier);
        File.WriteAllText(Path.Combine(Dossier, nom), valeur ?? string.Empty);
    }
}
