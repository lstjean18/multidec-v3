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

    /// <summary>Vue 3D VectorDraw ; null si le composant n'a pas pu être créé (licence, DLL absente) : l'application s'ouvre quand même.</summary>
    private readonly vdScrollableControl.vdScrollableControl? _vd;

    /// <summary>Raison pour laquelle la vue 3D est indisponible (affichée à l'ouverture), ou null.</summary>
    private readonly string? _erreurVectorDraw;
    private readonly TableOrientations _orientations;
    private RenduVectorDraw? _rendu;

    /// <summary>Colonnes « ConXm » et « PareGravXm » ajoutées à la grille des mailles (une par niveau avec console / pare-gravois).</summary>
    private readonly List<DataGridColumn> _colonnesConsoles = new List<DataGridColumn>();

    private bool _ongletsAReconstruire;

    public MainWindow()
    {
        InitializeComponent();
        Title = $"MultiDEC {VersionApplication.Libelle} (build {VersionApplication.Build})";

        // Poste sans VectorDraw utilisable (licence, DLL) : la fenêtre s'ouvre sans vue 3D au lieu de planter (Laurent, 26/11/2026).
        try
        {
            _vd = CreerVueVectorDraw();
            HoteVectorDraw.Child = _vd;
            MenuContextuel();
        }
        catch (Exception ex)
        {
            _vd = null;
            _erreurVectorDraw = ex.GetBaseException().Message;
            App.Journaliser(ex);
            HoteVectorDraw.Child = new System.Windows.Forms.Label
            {
                Text = "Vue 3D indisponible : " + _erreurVectorDraw,
                Dock = System.Windows.Forms.DockStyle.Fill,
                TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
            };
        }

        Vm.ColonnesConsolesChangees += (_, _) => ColonnesConsoles();
        ColonnesConsoles();
        Vm.Faces.CollectionChanged += (_, _) => ReconstruireOnglets();
        Vm.OuvragesChanges += (_, _) => ReconstruireOnglets();
        Vm.Mailles.CollectionChanged += (_, _) => Dispatcher.BeginInvoke(new Action(NumeroterMailles));
        Vm.PropertyChanged += Vm_PropertyChanged;
        Vm_PropertyChanged(Vm, new System.ComponentModel.PropertyChangedEventArgs(string.Empty));
        OngletsFaces();
        _orientations = TableOrientations.Charger(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "orientations-blocs.json"));

        // Paramètres par défaut de l'utilisateur : appliqués au projet de départ, à la bibliothèque et au rendu.
        try
        {
            var defauts = ParametresParDefaut.Charger(Reglages.CheminParametresParDefaut);
            defauts.CompleterFichiersInstalles(DossierProgramme); // bibliothèque et multidec.bib installés (remarque 62)
            Vm.Defauts = defauts;
        }
        catch (Exception ex)
        {
            Boite.Show("Paramètres par défaut illisibles, paramètres d'usine utilisés : " + ex.Message, "Paramètres par défaut", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        Vm.Nouveau();
        ListeRendu(true);
        Loaded += (_, _) =>
        {
            Localisation.LocaliserTextes(this); // textes fixes au séparateur décimal du poste (remarque 64)
            if (_erreurVectorDraw != null)
            {
                Boite.Show(this, "La vue 3D VectorDraw n'a pas pu être créée sur ce poste :\n" + _erreurVectorDraw
                                + "\n\nMultiDEC reste utilisable pour la saisie et le décompte. Détail dans " + App.CheminJournal,
                                "MultiDEC", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            // Bibliothèque des Paramètres par défaut, sinon celle installée avec MultiDEC, sinon la dernière chargée.
            string? defaut = Vm.Defauts.Bibliotheque;
            ChargerBibliotheque(!string.IsNullOrEmpty(defaut) && File.Exists(defaut)
                ? defaut
                : ParametresParDefaut.BibliothequeInstallee(DossierProgramme) ?? Reglages.CheminBibliotheque);

            // Double-clic sur un .mdc3 (association créée par l'installateur) : le projet est ouvert au démarrage.
            string? projet = Environment.GetCommandLineArgs().Skip(1)
                .FirstOrDefault(a => a.EndsWith(FichierProjet.Extension, StringComparison.OrdinalIgnoreCase) && File.Exists(a));
            if (projet != null)
            {
                OuvrirProjet(projet);
            }

            if (Vm.Defauts.RechercherMisesAJour)
            {
                _ = RechercherMiseAJourAsync(silencieux: true);
            }
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
        string? ancienneBibliotheque = Vm.Defauts.Bibliotheque;
        string? anciensArticles = Vm.Defauts.Articles;
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
            Boite.Show(this, "Enregistrement impossible : " + ex.Message, "Paramètres par défaut", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        Vm.Defauts = fenetre.Resultat; // recharge aussi le fichier de références client
        ListeRendu(false);

        // Bibliothèque par défaut nouvelle ou modifiée : chargée tout de suite, avec multidec.bib (Laurent, 26/11/2026),
        // sans avoir à relancer MultiDEC.
        string? bibliotheque = Vm.Defauts.Bibliotheque;
        bool changee = !string.Equals(bibliotheque, ancienneBibliotheque, StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(bibliotheque) && File.Exists(bibliotheque) && (changee || _rendu == null))
        {
            ChargerBibliotheque(bibliotheque);
        }
        else if (!string.Equals(Vm.Defauts.Articles, anciensArticles, StringComparison.OrdinalIgnoreCase))
        {
            ChargerArticles(Vm.Defauts.CheminArticles(Reglages.CheminBibliotheque, DossierProgramme));
        }
    }

    /// <summary>Dossier du programme (MultiDEC.exe), où l'installateur dépose aussi Bibliotheque\.</summary>
    internal static string DossierProgramme => AppDomain.CurrentDomain.BaseDirectory;

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

    /// <summary>Création du contrôle VectorDraw dans sa propre méthode : une DLL absente lève l'erreur ici, pas dans le constructeur.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static vdScrollableControl.vdScrollableControl CreerVueVectorDraw() => new vdScrollableControl.vdScrollableControl();

    private void ChargerBibliotheque(string? chemin)
    {
        if (string.IsNullOrEmpty(chemin) || !File.Exists(chemin))
        {
            TexteBibliotheque.Text = string.IsNullOrEmpty(chemin)
                ? "Bibliothèque de blocs non choisie (Outils > Paramètres par défaut, onglet Fichiers)"
                : "Bibliothèque introuvable : " + chemin + " (Outils > Paramètres par défaut, onglet Fichiers)";
            ChargerArticles(Vm.Defauts.CheminArticles(null, DossierProgramme));
            return;
        }

        if (_vd == null)
        {
            // Pas de vue 3D : on garde le chemin et on charge quand même désignations et poids du décompte.
            Reglages.CheminBibliotheque = chemin;
            TexteBibliotheque.Text = "Bibliothèque : " + Path.GetFileName(chemin) + " (vue 3D indisponible)";
            ChargerArticles(Vm.Defauts.CheminArticles(chemin, DossierProgramme));
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
            ChargerArticles(Vm.Defauts.CheminArticles(chemin, DossierProgramme));
        }
        catch (Exception ex)
        {
            _rendu = null;
            Boite.Show(this, ex.Message, "Bibliothèque de blocs", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Charge multidec.bib (désignations et poids du décompte) ; décompte sans désignations s'il manque.</summary>
    private void ChargerArticles(string? chemin)
    {
        try
        {
            Vm.Articles = !string.IsNullOrEmpty(chemin) && File.Exists(chemin) ? CatalogueArticles.Charger(chemin!) : CatalogueArticles.Vide;
        }
        catch (Exception ex)
        {
            Vm.Articles = CatalogueArticles.Vide;
            Boite.Show(this, "Lecture de " + chemin + " impossible : " + ex.Message, "Désignations et poids", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Dessiner_Click(object sender, RoutedEventArgs e)
    {
        // Un bouton « Valider la structure » est rouge : on prévient et on ouvre l'onglet de la face concernée.
        int aValider = Vm.PremiereFaceAValider();
        if (aValider >= 0)
        {
            Onglets.SelectedIndex = aValider + 1;
            Boite.Show(this, $"Les paramètres de {Vm.Faces[aValider].Nom} ont changé : cliquez sur « Valider la structure » (bouton rouge) avant de dessiner.",
                            "Dessin", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var structure = Vm.Generer();
            if (_rendu == null)
            {
                Boite.Show(this, "Choisissez d'abord la bibliothèque de blocs (Blocs 3D - Echafaudage.dwg). Le décompte est calculé.", "Dessin", MessageBoxButton.OK, MessageBoxImage.Information);
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
            Boite.Show(this, ex.Message, "Dessin de la façade", MessageBoxButton.OK, MessageBoxImage.Warning);
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

        for (int i = 0; i < NombreOngletsFaces; i++)
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

        OngletsTours();

        // On reste sur « Général », sinon sur la face active (S1 si l'ancienne face n'existe plus), ou sur l'onglet demandé.
        Onglets.SelectedIndex = _ongletApresReconstruction is int demande && demande < Onglets.Items.Count
            ? demande
            : selection <= 0 || NombreOngletsFaces == 0 ? (selection > 0 ? Math.Min(selection, Onglets.Items.Count - 1) : 0) : Math.Min(Vm.IndexFaceActive + 1, Onglets.Items.Count - 1);
        _ongletApresReconstruction = null;
    }

    private void Onglets_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, Onglets))
        {
            return; // listes déroulantes à l'intérieur des onglets
        }

        if (Onglets.SelectedIndex > 0 && Onglets.SelectedIndex <= NombreOngletsFaces)
        {
            Vm.IndexFaceActive = Onglets.SelectedIndex - 1;
        }

        MettreEnEvidence();
    }

    private void Vm_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FacadeViewModel.ColonneApproVisible) or nameof(FacadeViewModel.ColonneRecetteVisible) or nameof(FacadeViewModel.ColonneLestVisible) or "" or null)
        {
            ColonneLest.Visibility = Vm.ColonneLestVisible ? Visibility.Visible : Visibility.Collapsed;
            ColonneAppro.Visibility = Vm.ColonneApproVisible ? Visibility.Visible : Visibility.Collapsed;
            ColonneRecette.Visibility = Vm.ColonneRecetteVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        if (e.PropertyName is nameof(FacadeViewModel.EnReferencesClient) or nameof(FacadeViewModel.TitreReference) or "" or null)
        {
            // En-têtes de colonnes : hors de l'arbre visuel, mis à jour ici.
            ColonneRefRetotub.Visibility = Vm.EnReferencesClient ? Visibility.Visible : Visibility.Collapsed;
            ColonneReference.Header = Vm.TitreReference;
        }

        if (e.PropertyName == nameof(FacadeViewModel.IndexFaceActive) && Onglets.SelectedIndex > 0 && Onglets.SelectedIndex <= NombreOngletsFaces
            && Onglets.SelectedIndex != Vm.IndexFaceActive + 1)
        {
            Onglets.SelectedIndex = Vm.IndexFaceActive + 1;
        }
    }

    /// <summary>Face active en couleur dans le dessin, les autres estompées (toutes en couleur sur « Général »).</summary>
    private void MettreEnEvidence() => _rendu?.MettreEnEvidence(NomOuvrageActif());

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
            Boite.Show(this, "Sélectionnez d'abord la maille à supprimer.", "Mailles", MessageBoxButton.OK, MessageBoxImage.Information);
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
        if (_vd == null)
        {
            return;
        }

        var menu = new System.Windows.Forms.ContextMenuStrip();
        var supprimer = new System.Windows.Forms.ToolStripMenuItem("Supprimer la sélection");
        supprimer.Click += (_, _) => SupprimerSelection();
        menu.Items.Add(supprimer);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        var retablir = new System.Windows.Forms.ToolStripMenuItem("Rétablir les pièces supprimées");
        retablir.Click += (_, _) =>
        {
            Vm.RetablirPiecesSupprimees();
            Dessiner_Click(this, new RoutedEventArgs());
        };
        menu.Items.Add(retablir);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        var zoom = new System.Windows.Forms.ToolStripMenuItem("Zoom tout");
        zoom.Click += (_, _) => _rendu?.ZoomTout();
        menu.Items.Add(zoom);

        // Clic droit sur un about (ou un déport en console) : « Remplacer l'about par » (Laurent, 25/11/2026).
        var separateurAbout = new System.Windows.Forms.ToolStripSeparator();
        var remplacer = new System.Windows.Forms.ToolStripMenuItem("Remplacer l'about par");
        menu.Items.Insert(0, separateurAbout);
        menu.Items.Insert(0, remplacer);

        // Clic droit sur une pièce d'un côté de tour escalier : ouvrir ou fermer ce côté au palier (sortie).
        var separateurTour = new System.Windows.Forms.ToolStripSeparator();
        var sortieTour = new System.Windows.Forms.ToolStripMenuItem("Ouvrir");
        var acrotereTour = new System.Windows.Forms.ToolStripMenuItem("Ouvrir + sortie acrotère");
        menu.Items.Insert(0, separateurTour);
        menu.Items.Insert(0, acrotereTour);
        menu.Items.Insert(0, sortieTour);
        menu.Opening += (_, _) =>
        {
            int n = _rendu?.NombreSelectionnes ?? 0;
            supprimer.Enabled = n > 0;
            supprimer.Text = n > 1 ? $"Supprimer les {n} blocs sélectionnés" : "Supprimer la sélection";
            int supprimees = Vm.NombrePiecesSupprimees;
            retablir.Enabled = supprimees > 0;
            retablir.Text = supprimees > 0 ? $"Rétablir les pièces supprimées ({supprimees})" : "Rétablir les pièces supprimées";

            var point = _vd!.BaseControl.PointToClient(System.Windows.Forms.Control.MousePosition);
            var element = _rendu?.ElementDesigne(point);
            bool about = element?.About != null;
            remplacer.Visible = about;
            separateurAbout.Visible = about;
            separateurTour.Visible = sortieTour.Visible = PreparerMenuTour(sortieTour, element);
            acrotereTour.Visible = sortieTour.Visible && PreparerMenuAcrotere(acrotereTour, element);
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

    private static string FormattableStringFr(FormattableString texte) => texte.ToString(MultiDEC.Core.CultureNombres.Documents);

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
        // Projet multi-ouvrages : on choisit le premier ouvrage (façade ou tour escalier alu).
        var choix = ChoisirPremierOuvrage();
        if (choix == null)
        {
            return;
        }

        if (choix == PremierOuvrage.TourEscalierAlu)
        {
            Vm.NouveauProjetTour();
            _ongletApresReconstruction = 1;
        }
        else
        {
            Vm.Nouveau();
        }

        ListeRendu(true);
    }

    private void Quitter_Click(object sender, RoutedEventArgs e) => Close();

    private void Ouvrir_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = FiltreProjet };
        if (dlg.ShowDialog(this) == true)
        {
            OuvrirProjet(dlg.FileName);
        }
    }

    private void OuvrirProjet(string chemin)
    {
        try
        {
            Vm.Ouvrir(chemin);
        }
        catch (Exception ex)
        {
            Boite.Show(this, ex.Message, "Ouvrir", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // Le modèle 3D enregistré (structure + pièces supprimées + abouts remplacés) est redessiné tel quel (point 54).
        if (_rendu != null && Vm.PremiereFaceAValider() < 0)
        {
            Dessiner_Click(this, new RoutedEventArgs());
        }
    }

    /// <summary>
    /// Fichier > Exporter le modèle (point 54) : boîte d'enregistrement de VectorDraw (formats et versions qu'il sait écrire :
    /// DWG, DXF, PDF, VDML…), sinon une boîte standard.
    /// </summary>
    private void ExporterModele_Click(object sender, RoutedEventArgs e)
    {
        if (_rendu == null || Vm.Structure == null)
        {
            Boite.Show(this, "Dessinez d'abord la structure (bouton Dessiner).", "Exporter le modèle", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string nom = Path.GetFileNameWithoutExtension(Vm.CheminFichier ?? "MultiDEC");
        string? chemin = _rendu.DemanderFichierExport(nom);
        if (chemin == null)
        {
            var dlg = new SaveFileDialog { FileName = nom, Filter = RenduVectorDraw.FiltreExport };
            chemin = dlg.ShowDialog(this) == true ? dlg.FileName : null;
        }

        if (string.IsNullOrEmpty(chemin))
        {
            return;
        }

        try
        {
            _rendu.Exporter(chemin!);
            Boite.Show(this, "Modèle exporté : " + chemin, "Exporter le modèle", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Boite.Show(this, "Export impossible : " + ex.Message, "Exporter le modèle", MessageBoxButton.OK, MessageBoxImage.Warning);
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
