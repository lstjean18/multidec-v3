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
using MultiDEC.Core.Decompte;
using MultiDEC.Core.Facade;
using MultiDEC.Core.Persistance;
using MultiDEC.Rendering.VectorDraw;

namespace MultiDEC.App;

/// <summary>
/// Fenêtre principale : héberge le contrôle VectorDraw (WinForms) et relie le ViewModel au rendu.
/// Onglets de gauche : « Général » (dans le XAML) puis un onglet par face, créés ici selon la forme.
/// </summary>
public partial class MainWindow : Window
{
    private const string FiltreProjet = "Projet MultiDEC v3 (*" + FichierProjet.Extension + ")|*" + FichierProjet.Extension;

    private readonly vdScrollableControl.vdScrollableControl _vd = new vdScrollableControl.vdScrollableControl();
    private readonly TableOrientations _orientations;
    private RenduVectorDraw? _rendu;

    /// <summary>Colonnes « ConXm » et « PareGravXm » ajoutées à la grille des mailles (une par niveau avec console / pare-gravois).</summary>
    private readonly List<DataGridColumn> _colonnesConsoles = new List<DataGridColumn>();

    private bool _ongletsAReconstruire;

    public MainWindow()
    {
        InitializeComponent();
        HoteVectorDraw.Child = _vd;
        MenuContextuel();
        Vm.ColonnesConsolesChangees += (_, _) => ColonnesConsoles();
        ColonnesConsoles();
        Vm.Faces.CollectionChanged += (_, _) => ReconstruireOnglets();
        Vm.Mailles.CollectionChanged += (_, _) => Dispatcher.BeginInvoke(new Action(NumeroterMailles));
        Vm.PropertyChanged += Vm_PropertyChanged;
        OngletsFaces();
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
            ChargerArticles(Path.Combine(Path.GetDirectoryName(chemin)!, CatalogueArticles.NomFichier));
        }
        catch (Exception ex)
        {
            _rendu = null;
            MessageBox.Show(this, ex.Message, "Bibliothèque de blocs", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Charge multidec.bib (désignations et poids du décompte) ; décompte sans désignations s'il manque.</summary>
    private void ChargerArticles(string chemin)
    {
        try
        {
            Vm.Articles = File.Exists(chemin) ? CatalogueArticles.Charger(chemin) : CatalogueArticles.Vide;
        }
        catch (Exception ex)
        {
            Vm.Articles = CatalogueArticles.Vide;
            MessageBox.Show(this, "Lecture de " + chemin + " impossible : " + ex.Message, "Désignations et poids", MessageBoxButton.OK, MessageBoxImage.Warning);
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

            MettreEnEvidence();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Dessin de la façade", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ---- Onglets des faces ----------------------------------------------------------------------

    /// <summary>Les faces changent par lots (forme modifiée, projet ouvert) : une seule reconstruction à la fin.</summary>
    private void ReconstruireOnglets()
    {
        if (_ongletsAReconstruire)
        {
            return;
        }

        _ongletsAReconstruire = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _ongletsAReconstruire = false;
            OngletsFaces();
        }));
    }

    /// <summary>Un onglet par face (S1…S4) après « Général », tous sur le même panneau lié à la face active.</summary>
    private void OngletsFaces()
    {
        int selection = Onglets.SelectedIndex;
        while (Onglets.Items.Count > 1)
        {
            Onglets.Items.RemoveAt(Onglets.Items.Count - 1);
        }

        foreach (var face in Vm.Faces)
        {
            Onglets.Items.Add(new TabItem
            {
                Header = face.Nom,
                Content = Vm,
                ContentTemplate = (DataTemplate)FindResource("PanneauFace"),
                MinWidth = 44,
            });
        }

        // On reste sur « Général », sinon sur la face active (S1 si l'ancienne face n'existe plus).
        Onglets.SelectedIndex = selection <= 0 ? 0 : Math.Min(Vm.IndexFaceActive + 1, Onglets.Items.Count - 1);
    }

    private void Onglets_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, Onglets))
        {
            return; // listes déroulantes à l'intérieur des onglets
        }

        if (Onglets.SelectedIndex > 0)
        {
            Vm.IndexFaceActive = Onglets.SelectedIndex - 1;
        }

        MettreEnEvidence();
    }

    private void Vm_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FacadeViewModel.IndexFaceActive) && Onglets.SelectedIndex > 0 && Onglets.SelectedIndex != Vm.IndexFaceActive + 1)
        {
            Onglets.SelectedIndex = Vm.IndexFaceActive + 1;
        }
    }

    /// <summary>Face active en couleur dans le dessin, les autres estompées (toutes en couleur sur « Général »).</summary>
    private void MettreEnEvidence() => _rendu?.MettreEnEvidence(Onglets.SelectedIndex > 0 ? Vm.NomFaceActive : null);

    // ---- Grille des mailles -------------------------------------------------------------------

    private void GrilleMailles_LoadingRow(object? sender, DataGridRowEventArgs e) => e.Row.Header = (e.Row.GetIndex() + 1).ToString();

    /// <summary>Renumérote les lignes (colonne « # ») après un ajout, une insertion ou une suppression.</summary>
    private void NumeroterMailles()
    {
        for (int i = 0; i < GrilleMailles.Items.Count; i++)
        {
            if (GrilleMailles.ItemContainerGenerator.ContainerFromIndex(i) is DataGridRow ligne)
            {
                ligne.Header = (i + 1).ToString();
            }
        }
    }

    private void AjouterMaille_Click(object sender, RoutedEventArgs e) => SelectionnerMaille(Vm.AjouterMaille(GrilleMailles.SelectedIndex));

    private void InsererMaille_Click(object sender, RoutedEventArgs e) => SelectionnerMaille(Vm.InsererMailleAvant(GrilleMailles.SelectedIndex));

    private void SupprimerMaille_Click(object sender, RoutedEventArgs e)
    {
        int i = GrilleMailles.SelectedIndex;
        if (i < 0)
        {
            MessageBox.Show(this, "Sélectionnez d'abord la maille à supprimer.", "Mailles", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        GrilleMailles.CommitEdit(DataGridEditingUnit.Row, true);
        Vm.SupprimerMaille(i);
        SelectionnerMaille(Math.Min(i, Vm.Mailles.Count - 1));
    }

    private void SelectionnerMaille(int index)
    {
        if (index >= 0 && index < Vm.Mailles.Count)
        {
            GrilleMailles.SelectedIndex = index;
            GrilleMailles.ScrollIntoView(Vm.Mailles[index]);
        }
    }

    // ---- Vue ----------------------------------------------------------------------------------

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

        // Une colonne « PareGravXm » par niveau avec pare-gravois (Laurent, 24/11/2026).
        foreach (var n in Vm.NiveauxAvecPareGravois)
        {
            var caseACocher = new FrameworkElementFactory(typeof(CheckBox));
            caseACocher.SetBinding(ToggleButton.IsCheckedProperty, new Binding("[" + MailleDefinition.PrefixePareGravois + MailleDefinition.Cle(n.Hauteur) + "]")
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            });
            caseACocher.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            caseACocher.SetValue(ToolTipProperty, $"Pare-gravois au niveau {n.Hauteur:0.00} m sur cette maille");

            var colonne = new DataGridTemplateColumn
            {
                Header = n.NomColonnePareGravois,
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

    private void Quitter_Click(object sender, RoutedEventArgs e) => Close();

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
