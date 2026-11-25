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
        Vm_PropertyChanged(Vm, new System.ComponentModel.PropertyChangedEventArgs(string.Empty));
        OngletsFaces();
        _orientations = TableOrientations.Charger(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "orientations-blocs.json"));

        // Paramètres par défaut de l'utilisateur : appliqués au projet de départ, à la bibliothèque et au rendu.
        try
        {
            Vm.Defauts = ParametresParDefaut.Charger(Reglages.CheminParametresParDefaut);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Paramètres par défaut illisibles, paramètres d'usine utilisés : " + ex.Message, "Paramètres par défaut", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        Vm.Nouveau();
        ListeRendu(true);
        Loaded += (_, _) =>
        {
            string? defaut = Vm.Defauts.Bibliotheque;
            ChargerBibliotheque(!string.IsNullOrEmpty(defaut) && File.Exists(defaut) ? defaut : Reglages.CheminBibliotheque);
        };
    }

    /// <summary>Liste des rendus : modes retirés masqués (sauf celui en cours), mode par défaut choisi si demandé.</summary>
    private void ListeRendu(bool choisirDefaut)
    {
        foreach (var item in ChoixRendu.Items.OfType<ComboBoxItem>())
        {
            if (choisirDefaut && (string)item.Tag == Vm.Defauts.ModeRendu)
            {
                ChoixRendu.SelectedItem = item;
            }
        }

        foreach (var item in ChoixRendu.Items.OfType<ComboBoxItem>())
        {
            bool masque = Vm.Defauts.EstMasque(ListesDeChoix.ModeRendu, (string)item.Tag) && !ReferenceEquals(item, ChoixRendu.SelectedItem);
            item.Visibility = masque ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private void ParametresParDefaut_Click(object sender, RoutedEventArgs e)
    {
        var fenetre = new ParametresParDefautWindow(Vm.Defauts) { Owner = this };
        if (fenetre.ShowDialog() != true)
        {
            return;
        }

        try
        {
            fenetre.Resultat.Enregistrer(Reglages.CheminParametresParDefaut);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Enregistrement impossible : " + ex.Message, "Paramètres par défaut", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        Vm.Defauts = fenetre.Resultat;
        ListeRendu(false);
    }

    private FacadeViewModel Vm => (FacadeViewModel)DataContext;

    // ---- Captures du manuel utilisateur (tests du serveur, CapturesManuelTests) ------------------

    internal FacadeViewModel ModeleVue => Vm;

    internal void ChargerBibliothequePourCapture(string chemin) => ChargerBibliotheque(chemin);

    internal void DessinerPourCapture() => Dessiner_Click(this, new RoutedEventArgs());

    internal RenduVectorDraw? RenduCourant => _rendu;

    /// <summary>Image du dessin 3D (le contrôle WinForms n'apparaît pas dans un rendu WPF hors écran).</summary>
    internal System.Drawing.Bitmap ImageDessin(int largeur, int hauteur)
    {
        var bmp = new System.Drawing.Bitmap(Math.Max(1, largeur), Math.Max(1, hauteur));
        _rendu?.RendreDans(bmp);
        return bmp;
    }

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
            Rendu_SelectionChanged(ChoixRendu, null!);
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
        // Un bouton « Valider la structure » est rouge : on prévient et on ouvre l'onglet de la face concernée.
        int aValider = Vm.PremiereFaceAValider();
        if (aValider >= 0)
        {
            Onglets.SelectedIndex = aValider + 1;
            MessageBox.Show(this, $"Les paramètres de {Vm.Faces[aValider].Nom} ont changé : cliquez sur « Valider la structure » (bouton rouge) avant de dessiner.",
                            "Dessin", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var structure = Vm.Generer();
            if (_rendu == null)
            {
                MessageBox.Show(this, "Choisissez d'abord la bibliothèque de blocs (Blocs 3D - Echafaudage.dwg). Le décompte est calculé.", "Dessin", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var rapport = _rendu.Dessiner(structure, _orientations);
            _rendu.DessinerFacade(Vm.MursDuBatiment(), Vm.DessinerFacade); // masqués si la case est décochée
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

    private void Facade_Click(object sender, RoutedEventArgs e) => _rendu?.AfficherFacade(Vm.DessinerFacade);

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

        for (int i = 0; i < Vm.Faces.Count; i++)
        {
            var face = Vm.Faces[i];
            Onglets.Items.Add(new TabItem
            {
                // Couleur pastel de la face, reprise sur l'onglet « Mailles et niveaux » quand elle est active.
                Header = new Border
                {
                    Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(FacadeViewModel.CouleurFace(i))!,
                    CornerRadius = new CornerRadius(2),
                    Padding = new Thickness(6, 1, 6, 1),
                    Margin = new Thickness(-4, -1, -4, -1),
                    Child = new TextBlock { Text = face.Nom },
                },
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
        if (e.PropertyName is nameof(FacadeViewModel.ColonneApproVisible) or nameof(FacadeViewModel.ColonneRecetteVisible) or "" or null)
        {
            ColonneAppro.Visibility = Vm.ColonneApproVisible ? Visibility.Visible : Visibility.Collapsed;
            ColonneRecette.Visibility = Vm.ColonneRecetteVisible ? Visibility.Visible : Visibility.Collapsed;
        }

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

        // Clic droit sur un about (ou un déport en console) : « Remplacer l'about par » (Laurent, 25/11/2026).
        var separateurAbout = new System.Windows.Forms.ToolStripSeparator();
        var remplacer = new System.Windows.Forms.ToolStripMenuItem("Remplacer l'about par");
        menu.Items.Insert(0, separateurAbout);
        menu.Items.Insert(0, remplacer);
        menu.Opening += (_, _) =>
        {
            int n = _rendu?.NombreSelectionnes ?? 0;
            supprimer.Enabled = n > 0;
            supprimer.Text = n > 1 ? $"Supprimer les {n} blocs sélectionnés" : "Supprimer la sélection";

            var point = _vd.BaseControl.PointToClient(System.Windows.Forms.Control.MousePosition);
            var element = _rendu?.ElementDesigne(point);
            bool about = element?.About != null;
            remplacer.Visible = about;
            separateurAbout.Visible = about;
            if (about)
            {
                MenuRemplacerAbout(remplacer, element!.NomFace, element.About!);
            }
        };
        _vd.BaseControl.ContextMenuStrip = menu;
    }

    /// <summary>
    /// Sous-menu « Remplacer l'about par » : garde-corps d'about de la liste des protections, déports en console
    /// (consoles gardées dans les paramètres par défaut) et retour aux paramètres de la face. Le choix est
    /// enregistré dans le projet puis le dessin est refait.
    /// </summary>
    private void MenuRemplacerAbout(System.Windows.Forms.ToolStripMenuItem menu, string nomFace, RepereAbout about)
    {
        menu.DropDownItems.Clear();
        var actuel = Vm.AboutRemplace(nomFace, about);
        menu.Text = FormattableStringFr($"Remplacer l'about {(about.Droit ? "droit" : "gauche")} de {nomFace} à {about.Hauteur:0.00} m par");

        void Ajouter(string texte, bool coche, TypeSecurite? securite, string? console)
        {
            var item = new System.Windows.Forms.ToolStripMenuItem(texte) { Checked = coche };
            item.Click += (_, _) =>
            {
                Vm.RemplacerAbout(nomFace, about, securite, console);
                Dessiner_Click(this, new RoutedEventArgs());
            };
            menu.DropDownItems.Add(item);
        }

        foreach (var type in Vm.ChoixRemplacementAbout)
        {
            Ajouter(Libelles.De(type), actuel?.Console == null && actuel?.Securite == type, type, null);
        }

        menu.DropDownItems.Add(new System.Windows.Forms.ToolStripSeparator());
        foreach (string console in Vm.ConsolesDeport(nomFace))
        {
            Ajouter("Déport en console " + console, string.Equals(actuel?.Console, console, StringComparison.OrdinalIgnoreCase), null, console);
        }

        if (actuel != null)
        {
            menu.DropDownItems.Add(new System.Windows.Forms.ToolStripSeparator());
            Ajouter("Revenir aux paramètres de la face", false, null, null);
        }
    }

    private static string FormattableStringFr(FormattableString texte) => texte.ToString(System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));

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


    private void Modele_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => Vm.ModeleChange();

    private void Nouveau_Click(object sender, RoutedEventArgs e)
    {
        Vm.Nouveau();
        ListeRendu(true);
    }

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

    /// <summary>Paramètres par défaut de l'utilisateur (profil Windows).</summary>
    public static string CheminParametresParDefaut => Path.Combine(Dossier, ParametresParDefaut.NomFichier);

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
