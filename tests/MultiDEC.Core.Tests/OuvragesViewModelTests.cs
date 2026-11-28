using MultiDEC.App.ViewModels;
using MultiDEC.Core.Geometrie;
using MultiDEC.Core.TourEscalier;
using Xunit;

namespace MultiDEC.Core.Tests;

/// <summary>Ruban « Ouvrages », onglet d'une tour et décompte par ouvrage (Laurent, 28/11/2026).</summary>
public class OuvragesViewModelTests
{
    [Fact]
    public void Ajouter_une_tour_et_filtrer_le_decompte()
    {
        var vm = new FacadeViewModel();
        int onglets = 0;
        vm.OuvragesChanges += (_, _) => onglets++;
        var tour = vm.AjouterTour();
        Assert.Equal("T1", tour.Nom);
        Assert.Single(vm.Tours);
        Assert.True(onglets > 0);
        Assert.Contains(vm.ChoixOuvragesDecompte, c => c.Key == "T1");

        vm.Generer();
        int toutes = vm.Decompte.Sum(l => l.Quantite);
        vm.OuvrageDecompte = "T1";
        Assert.True(vm.DecompteFiltre);
        Assert.False(vm.DecompteModifiable);
        int tour1 = vm.Decompte.Sum(l => l.Quantite);
        Assert.Contains(vm.Decompte, l => l.Reference.StartsWith("HMESCALU", StringComparison.Ordinal));
        vm.OuvrageDecompte = FacadeViewModel.OuvrageFacade;
        int facade = vm.Decompte.Sum(l => l.Quantite);
        Assert.DoesNotContain(vm.Decompte, l => l.Reference.StartsWith("HMESCALU", StringComparison.Ordinal));
        Assert.Equal(toutes, tour1 + facade);
        vm.OuvrageDecompte = string.Empty;
        Assert.Equal(toutes, vm.Decompte.Sum(l => l.Quantite));
    }

    [Fact]
    public void Il_reste_toujours_un_ouvrage()
    {
        var vm = new FacadeViewModel();
        vm.AvecFacade = false;
        Assert.True(vm.AvecFacade); // pas de tour : la façade reste
        var tour = vm.AjouterTour();
        vm.AvecFacade = false;
        Assert.False(vm.AvecFacade);
        Assert.False(vm.SupprimerTour(tour));
        vm.AvecFacade = true;
        Assert.True(vm.SupprimerTour(tour));
        Assert.Empty(vm.Projet.Tours);
    }

    [Fact]
    public void Nouveau_projet_de_tour_seule()
    {
        var vm = new FacadeViewModel();
        var tour = vm.NouveauProjetTour();
        Assert.False(vm.AvecFacade);
        Assert.Equal(-1, vm.PremiereFaceAValider());
        var s = vm.Generer();
        Assert.All(s.Elements, e => Assert.Equal(tour.Nom, e.NomFace));
        Assert.DoesNotContain(vm.ChoixOuvragesDecompte, c => c.Key == FacadeViewModel.OuvrageFacade);
    }

    [Fact]
    public void Grille_des_paliers_et_sorties()
    {
        var vm = new FacadeViewModel();
        var tour = vm.AjouterTour();
        tour.Hauteur = 6;
        Assert.Equal(new[] { 6.0, 4.0, 2.0 }, tour.Paliers.Select(p => p.Hauteur));
        Assert.True(tour.Paliers[0].SortieBout); // sortie par défaut au dernier palier

        tour.Paliers[1].SortieC = true;
        tour.Paliers[1].LargeurC = 0.5;
        Assert.Contains(tour.Tour.Sorties, s => s.Cote == CoteTour.C && Cotes.Egal(s.Hauteur, 4) && Cotes.Egal(s.Largeur, 0.5));

        // Hauteur réduite : la sortie à 6 m disparaît avec le palier.
        tour.Hauteur = 4;
        Assert.DoesNotContain(tour.Tour.Sorties, s => Cotes.Egal(s.Hauteur, 6));
        Assert.Equal(new[] { 4.0, 2.0 }, tour.Paliers.Select(p => p.Hauteur));

        tour.Hauteur = 5.3;
        Assert.False(tour.HauteurCorrecte);
        Assert.Empty(tour.Paliers);
        Assert.Contains("0,50", tour.Resume);
    }

    [Fact]
    public void Accoler_la_tour_propose_les_sorties_cote_facade()
    {
        var vm = new FacadeViewModel();
        var tour = vm.AjouterTour();
        tour.Hauteur = vm.Projet.S1.Parametres.HauteurDernierPlancher;
        tour.Accolee = true;
        Assert.Equal("S1", tour.FaceAccolee);
        var communs = ToursDansLaStructure.PaliersCommuns(vm.Projet, tour.Tour);
        Assert.NotEmpty(communs);
        Assert.All(communs, z => Assert.Contains(tour.Tour.Sorties, s => s.Cote == CoteTour.A && Cotes.Egal(s.Hauteur, z)));

        Assert.True(tour.BasculerSortie(CoteTour.C, communs[0]) && tour.SortieOuverte(CoteTour.C, communs[0]));
        Assert.False(tour.BasculerSortie(CoteTour.C, communs[0]));
    }

    [Fact]
    public void Sortie_acrotere_depuis_la_grille_et_le_clic_droit()
    {
        var vm = new FacadeViewModel();
        var tour = vm.AjouterTour();
        tour.Hauteur = 6;
        var palier = tour.Paliers[1]; // 4 m
        palier.Acrotere = "C";
        Assert.True(palier.SortieC && palier.AvecAcrotere);
        palier.LongueurPont = 2.0;
        palier.AcrotereTournee = true;
        Assert.Contains(tour.Tour.Sorties, s => s.Acrotere && s.Cote == CoteTour.C && Cotes.Egal(s.LongueurPont, 2.0) && s.Tournee);
        palier.Acrotere = string.Empty;
        Assert.DoesNotContain(tour.Tour.Sorties, s => s.Acrotere);

        Assert.True(tour.AjouterAcrotere(CoteTour.A, 2));
        Assert.Contains(tour.Tour.Sorties, s => s.Acrotere && s.Cote == CoteTour.A && Cotes.Egal(s.Hauteur, 2));
        Assert.False(tour.AjouterAcrotere(CoteTour.A, 2));
    }

    [Fact]
    public void Ajouter_une_tour_en_supprimant_les_autres_ouvrages()
    {
        var vm = new FacadeViewModel();
        vm.AjouterTour();
        var seule = vm.AjouterTourSeule();
        Assert.False(vm.AvecFacade);
        Assert.Equal(seule, Assert.Single(vm.Tours));
        Assert.Equal("T1", seule.Nom);
        Assert.Single(vm.Projet.Tours);
        Assert.True(vm.AUnOuvrage);
        var s = vm.Generer();
        Assert.All(s.Elements, e => Assert.Equal(seule.Nom, e.NomFace));
    }
}
