using System;
using System.Linq;
using MultiDEC.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MultiDEC.App.ViewModels;
using MultiDEC.Core.Facade;

namespace MultiDEC.App;

/// <summary>
/// Projet multi-ouvrages (Laurent, 28/11/2026) : ruban « Ouvrages », un onglet par tour escalier alu (T1, T2…)
/// après les faces, choix du premier ouvrage au « Nouveau », clic droit « Ouvrir / Fermer » sur un côté de tour.
/// </summary>
public partial class MainWindow
{
    /// <summary>Couleur des onglets de tour (les faces ont leurs pastels).</summary>
    private const string CouleurTour = "#FFF2CC";

    /// <summary>Onglet à sélectionner après la prochaine reconstruction des onglets (tour ajoutée, nouveau projet).</summary>
    private int? _ongletApresReconstruction;

    private enum PremierOuvrage
    {
        Facade,
        TourEscalierAlu,
    }

    /// <summary>Onglets de faces affichés : aucun si le projet n'a pas de façade.</summary>
    private int NombreOngletsFaces => Vm.AvecFacade ? Vm.Faces.Count : 0;

    /// <summary>Ajoute un onglet par tour après les faces.</summary>
    private void OngletsTours()
    {
        foreach (var tour in Vm.Tours)
        {
            Onglets.Items.Add(new TabItem
            {
                Header = new Border
                {
                    Background = (Brush)new BrushConverter().ConvertFromString(CouleurTour)!,
                    CornerRadius = new CornerRadius(2),
                    Padding = new Thickness(6, 1, 6, 1),
                    Margin = new Thickness(-4, -1, -4, -1),
                    Child = new TextBlock { Text = tour.Nom, ToolTip = "Tour escalier à volées alu " + tour.Nom },
                },
                Content = tour,
                ContentTemplate = (DataTemplate)FindResource("PanneauTour"),
                MinWidth = 44,
            });
        }

        BoutonAjouterFacade.IsEnabled = !Vm.AvecFacade;
    }

    /// <summary>Tour de l'onglet ouvert, ou null.</summary>
    private TourViewModel? TourActive =>
        Onglets.SelectedIndex > NombreOngletsFaces && Onglets.SelectedIndex - NombreOngletsFaces - 1 < Vm.Tours.Count
            ? Vm.Tours[Onglets.SelectedIndex - NombreOngletsFaces - 1]
            : null;

    /// <summary>Ouvrage mis en évidence dans le dessin : face ou tour de l'onglet ouvert, null sur « Général ».</summary>
    private string? NomOuvrageActif() =>
        TourActive?.Nom ?? (Onglets.SelectedIndex > 0 && Onglets.SelectedIndex <= NombreOngletsFaces ? Vm.NomFaceActive : null);

    private void AjouterFacade_Click(object sender, RoutedEventArgs e)
    {
        Vm.AvecFacade = true;
        _ongletApresReconstruction = 1;
    }

    private void AjouterTour_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.AUnOuvrage)
        {
            var reponse = MessageBox.Show(this,
                "Supprimer les autres ouvrages du projet (façade et tours) ?\n\nOui : la nouvelle tour reste seule (la façade est mise de côté, « + Façade » la remet).\nNon : la tour s'ajoute aux ouvrages existants.",
                "Ajouter une tour escalier", MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Yes); // souvent une tour seule (Laurent)
            if (reponse == MessageBoxResult.Cancel)
            {
                return;
            }

            if (reponse == MessageBoxResult.Yes)
            {
                Vm.AjouterTourSeule();
                _ongletApresReconstruction = 1;
                return;
            }
        }

        Vm.AjouterTour();
        _ongletApresReconstruction = NombreOngletsFaces + Vm.Tours.Count; // onglet de la nouvelle tour
    }

    private void SupprimerOuvrage_Click(object sender, RoutedEventArgs e)
    {
        if (TourActive is TourViewModel tour)
        {
            if (MessageBox.Show(this, $"Supprimer la tour {tour.Nom} du projet ?", "Ouvrages", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            if (!Vm.SupprimerTour(tour))
            {
                MessageBox.Show(this, "C'est le dernier ouvrage du projet : ajoutez d'abord une façade ou une autre tour.", "Ouvrages", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            return;
        }

        if (Onglets.SelectedIndex > 0 && Onglets.SelectedIndex <= NombreOngletsFaces)
        {
            if (Vm.Tours.Count == 0)
            {
                MessageBox.Show(this, "La façade est le seul ouvrage du projet : ajoutez d'abord une tour.", "Ouvrages", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (MessageBox.Show(this, "Retirer la façade du projet ? Ses paramètres sont gardés : « + Façade » la remet.", "Ouvrages",
                                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                Vm.AvecFacade = false;
                _ongletApresReconstruction = 0;
            }

            return;
        }

        MessageBox.Show(this, "Ouvrez l'onglet de l'ouvrage à supprimer (une face ou une tour).", "Ouvrages", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>Fenêtre « Nouveau projet » : premier ouvrage à créer (null si l'utilisateur annule).</summary>
    private PremierOuvrage? ChoisirPremierOuvrage()
    {
        PremierOuvrage? choix = null;
        var fenetre = new Window
        {
            Title = "Nouveau projet",
            Owner = this,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            FontFamily = FontFamily,
            FontSize = 13,
        };

        var panneau = new StackPanel { Margin = new Thickness(16) };
        panneau.Children.Add(new TextBlock { Text = "Quel ouvrage voulez-vous créer en premier ?", Margin = new Thickness(0, 0, 0, 12) });
        var boutons = new StackPanel { Orientation = Orientation.Horizontal };

        Button Bouton(string titre, string detail, PremierOuvrage? valeur)
        {
            var contenu = new StackPanel { Width = 170 };
            contenu.Children.Add(new TextBlock { Text = titre, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            contenu.Children.Add(new TextBlock { Text = detail, FontSize = 12, Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
            var b = new Button { Content = contenu, Padding = new Thickness(10), Margin = new Thickness(0, 0, 8, 0), IsEnabled = valeur != null, HorizontalContentAlignment = HorizontalAlignment.Left };
            b.Click += (_, _) =>
            {
                choix = valeur;
                fenetre.DialogResult = true;
            };
            return b;
        }

        boutons.Children.Add(Bouton("Échafaudage de façade", "Droite, en L, en U, fermée ou volumique.", PremierOuvrage.Facade));
        boutons.Children.Add(Bouton("Tour escalier alu", "Volées aluminium de 2,50 m, tour de 1,50 ou 2,00 m.", PremierOuvrage.TourEscalierAlu));
        boutons.Children.Add(Bouton("Tour escalier limon", "À venir.", null));
        panneau.Children.Add(boutons);
        panneau.Children.Add(new TextBlock
        {
            Text = "D'autres ouvrages s'ajoutent ensuite avec le ruban « Ouvrages ».",
            FontSize = 12,
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 12, 0, 0),
        });
        fenetre.Content = panneau;
        return fenetre.ShowDialog() == true ? choix : null;
    }

    /// <summary>
    /// Menu « Ouvrir / Fermer le côté X de Tn au palier z » sur une pièce d'un côté de tour. Renvoie false si l'élément
    /// désigné n'appartient pas à un côté de tour.
    /// </summary>
    private bool PreparerMenuTour(System.Windows.Forms.ToolStripMenuItem menu, ElementPlace? element)
    {
        if (element?.CoteTour is not { } repere || Vm.TourNommee(element.NomFace) is not TourViewModel tour)
        {
            return false;
        }

        bool ouverte = tour.SortieOuverte(repere.Cote, repere.Palier);
        bool bout = repere.Cote == Core.TourEscalier.CoteTour.B || repere.Cote == Core.TourEscalier.CoteTour.D;
        var palier = tour.Paliers.FirstOrDefault(p => Math.Abs(p.Hauteur - repere.Palier) < 1e-3);
        menu.Enabled = ouverte || palier != null && (!bout || palier.Bout == repere.Cote);
        menu.Text = string.Format(CultureNombres.Documents, "{0} le côté {1} de {2} au palier {3:0.00} m",
            ouverte ? "Fermer" : "Ouvrir", repere.Cote, tour.Nom, repere.Palier)
            + (menu.Enabled ? string.Empty : " (pas de palier de ce côté)");
        menu.Tag = (tour, repere);
        menu.Click -= SortieTour_Click;
        menu.Click += SortieTour_Click;
        return true;
    }

    /// <summary>« Ouvrir + sortie acrotère » (v2) : seulement sur un côté fermé qui peut être ouvert à ce palier.</summary>
    private bool PreparerMenuAcrotere(System.Windows.Forms.ToolStripMenuItem menu, ElementPlace? element)
    {
        if (element?.CoteTour is not { } repere || Vm.TourNommee(element.NomFace) is not TourViewModel tour || tour.SortieOuverte(repere.Cote, repere.Palier))
        {
            return false;
        }

        menu.Enabled = true;
        menu.Tag = (tour, repere);
        menu.Click -= AcrotereTour_Click;
        menu.Click += AcrotereTour_Click;
        return true;
    }

    private void AcrotereTour_Click(object? sender, EventArgs e)
    {
        if (sender is System.Windows.Forms.ToolStripMenuItem { Tag: ValueTuple<TourViewModel, Core.TourEscalier.RepereCoteTour> cible })
        {
            cible.Item1.AjouterAcrotere(cible.Item2.Cote, cible.Item2.Palier);
            Dessiner_Click(this, new RoutedEventArgs());
        }
    }

    private void SortieTour_Click(object? sender, EventArgs e)
    {
        if (sender is System.Windows.Forms.ToolStripMenuItem { Tag: ValueTuple<TourViewModel, Core.TourEscalier.RepereCoteTour> cible })
        {
            cible.Item1.BasculerSortie(cible.Item2.Cote, cible.Item2.Palier);
            Dessiner_Click(this, new RoutedEventArgs());
        }
    }
}
