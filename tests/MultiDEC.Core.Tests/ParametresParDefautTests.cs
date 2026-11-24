using MultiDEC.Core.Facade;
using MultiDEC.Core.Persistance;
using Xunit;

namespace MultiDEC.Core.Tests;

/// <summary>Écran « Paramètres par défaut » (Laurent, 24/11/2026) : valeurs, listes réduites, export/import.</summary>
public class ParametresParDefautTests
{
    [Fact]
    public void Usine_lisses_en_pied_cochees()
    {
        var d = ParametresParDefaut.Usine();
        Assert.True(d.Face.LissesEnPied);
        Assert.True(d.NouveauProjet().S1.Parametres.LissesEnPied);
    }

    [Fact]
    public void Nouveau_projet_reprend_les_valeurs_par_defaut_sans_les_modifier()
    {
        var d = ParametresParDefaut.Usine();
        d.General.Modele = ModeleEchafaudage.MultiReto;
        d.General.SecuriteInterieure = TypeSecurite.Lisses;
        d.Face.LargeurMaille = 1.0;
        d.Face.LongueurTotaleSouhaitee = 30;
        d.Forme = FormeStructure.EnL;

        var p = d.NouveauProjet();
        Assert.Equal(ModeleEchafaudage.MultiReto, p.General.Modele);
        Assert.Equal(TypeSecurite.Lisses, p.General.SecuriteInterieure);
        Assert.Equal(2, p.Faces.Count);
        Assert.Equal(1.0, p.S1.Parametres.LargeurMaille);
        Assert.Equal(2, p.S1.Parametres.NombreAcces); // 30 m : deux accès
        Assert.Equal(-1, p.PremiereFaceAValider());

        p.S1.Parametres.LargeurMaille = 0.7;
        p.General.Bache = true;
        Assert.Equal(1.0, d.Face.LargeurMaille);
        Assert.False(d.General.Bache);
    }

    [Fact]
    public void Valeurs_retirees_des_listes_sauf_la_valeur_en_cours()
    {
        var d = ParametresParDefaut.Usine();
        d.Masquer(ListesDeChoix.LargeurMaille, 0.75, true);
        d.Masquer(ListesDeChoix.LargeurMaille, 1.05, true);
        d.Masquer(ListesDeChoix.Plinthe, TypePlinthe.Deportees, true);

        Assert.Equal(new[] { 0.70, 1.00 }, d.Filtrer(ListesDeChoix.LargeurMaille, new[] { 0.70, 0.75, 1.00, 1.05 }));
        Assert.Equal(new[] { 0.70, 1.00, 1.05 }, d.Filtrer(ListesDeChoix.LargeurMaille, new[] { 0.70, 0.75, 1.00, 1.05 }, 1.05));
        Assert.DoesNotContain(TypePlinthe.Deportees, d.Filtrer(ListesDeChoix.Plinthe, new[] { TypePlinthe.Sans, TypePlinthe.Deportees, TypePlinthe.Axees }));

        d.Masquer(ListesDeChoix.LargeurMaille, 0.75, false);
        Assert.False(d.EstMasque(ListesDeChoix.LargeurMaille, 0.75));
        Assert.True(d.EstMasque(ListesDeChoix.LargeurMaille, 1.05));
    }

    [Fact]
    public void Tout_retirer_garde_la_liste_complete()
    {
        var d = ParametresParDefaut.Usine();
        d.Masquer(ListesDeChoix.TypePlancher, TypePlancher.Complet, true);
        d.Masquer(ListesDeChoix.TypePlancher, TypePlancher.AccesSeul, true);
        Assert.Equal(2, d.Filtrer(ListesDeChoix.TypePlancher, new[] { TypePlancher.Complet, TypePlancher.AccesSeul }).Count);
    }

    [Fact]
    public void Export_puis_import_donne_les_memes_parametres()
    {
        var d = ParametresParDefaut.Usine();
        d.General.Plateaux = GammePlateaux.Optissimo;
        d.Face.DistanceFacade = 0.35;
        d.Face.LissesEnPied = false;
        d.DessinerFacade = true;
        d.ModeRendu = "Realiste";
        d.Masquer(ListesDeChoix.Modele, ModeleEchafaudage.MultiRetoEmbase, true);
        d.Bibliotheque = @"C:\Bibliotheque\Blocs 3D - Echafaudage.dwg";
        d.ModeleDevis = @"C:\Devis\trame.xlsx";
        d.ReferencesClients = @"C:\Clients\refs.csv";
        d.LogoClient = @"C:\Clients\logo.png";

        string fichier = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "multidec-defauts-" + System.Guid.NewGuid() + ".json");
        try
        {
            d.Enregistrer(fichier);
            var r = ParametresParDefaut.Charger(fichier);
            Assert.Equal(d.VersJson(), r.VersJson());
            Assert.False(r.Face.LissesEnPied);
            Assert.True(r.EstMasque(ListesDeChoix.Modele, ModeleEchafaudage.MultiRetoEmbase));
            Assert.Equal(@"C:\Clients\logo.png", r.LogoClient);
        }
        finally
        {
            System.IO.File.Delete(fichier);
        }
    }

    [Fact]
    public void Fichier_absent_parametres_d_usine()
    {
        var d = ParametresParDefaut.Charger(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "absent-" + System.Guid.NewGuid() + ".json"));
        Assert.Equal(ParametresParDefaut.Usine().VersJson(), d.VersJson());
    }
}

public class ParametresParDefautEcranTests
{
    [Fact]
    public void Decocher_la_valeur_par_defaut_prend_la_premiere_gardee()
    {
        var vm = new MultiDEC.App.ViewModels.ParametresParDefautViewModel(ParametresParDefaut.Usine());
        var plinthes = vm.Listes.Single(l => l.Cle == ListesDeChoix.Plinthe);
        var ext = plinthes.Defauts.Single(d => d.Titre == "Extérieur");
        Assert.Equal(TypePlinthe.Axees, ext.Choix!.Valeur);

        plinthes.Valeurs.Single(v => Equals(v.Valeur, TypePlinthe.Axees)).Proposee = false;
        Assert.True(vm.Resultat.EstMasque(ListesDeChoix.Plinthe, TypePlinthe.Axees));
        Assert.NotEqual(TypePlinthe.Axees, vm.Resultat.General.PlintheExterieure);
        Assert.DoesNotContain(ext.Proposees, v => Equals(v.Valeur, TypePlinthe.Axees));
    }

    [Fact]
    public void La_derniere_valeur_reste_proposee()
    {
        var vm = new MultiDEC.App.ViewModels.ParametresParDefautViewModel(ParametresParDefaut.Usine());
        var types = vm.Listes.Single(l => l.Cle == ListesDeChoix.TypePlancher);
        types.Valeurs[0].Proposee = false;
        types.Valeurs[1].Proposee = false;
        Assert.True(types.Valeurs[1].Proposee);
    }

    [Fact]
    public void Ecran_modifie_une_copie_et_rend_le_resultat()
    {
        var source = ParametresParDefaut.Usine();
        var vm = new MultiDEC.App.ViewModels.ParametresParDefautViewModel(source);
        vm.Cases.Single(c => c.Titre == "Lisses en pied").Valeur = false;
        vm.Cotes.Single(c => c.Titre.StartsWith("Distance")).Valeur = 0.4;
        vm.Fichiers.Single(f => f.Titre == "Logo client").Chemin = @"C:\logo.png";
        var modele = vm.Listes.Single(l => l.Cle == ListesDeChoix.Modele).Defauts[0];
        modele.Choix = vm.Listes.Single(l => l.Cle == ListesDeChoix.Modele).Valeurs.Single(v => Equals(v.Valeur, ModeleEchafaudage.MultiReto));

        Assert.True(source.Face.LissesEnPied);
        Assert.False(vm.Resultat.Face.LissesEnPied);
        Assert.Equal(0.4, vm.Resultat.Face.DistanceFacade);
        Assert.Equal(@"C:\logo.png", vm.Resultat.LogoClient);
        Assert.Equal(ModeleEchafaudage.MultiReto, vm.Resultat.General.Modele);

        vm.Remplacer(ParametresParDefaut.Usine());
        Assert.True(vm.Resultat.Face.LissesEnPied);
    }

    [Fact]
    public void Valeurs_retirees_absentes_des_listes_de_l_application()
    {
        var d = ParametresParDefaut.Usine();
        d.Masquer(ListesDeChoix.Modele, ModeleEchafaudage.MultiRetoEmbase, true);
        d.Masquer(ListesDeChoix.TypeMaille, TypeMaille.DemiEscalier070, true);
        d.Masquer(ListesDeChoix.Console, "HMCON700E", true);
        var vm = new MultiDEC.App.ViewModels.FacadeViewModel { Defauts = d };
        Assert.DoesNotContain(ModeleEchafaudage.MultiRetoEmbase, vm.ChoixModeles);
        Assert.DoesNotContain(TypeMaille.DemiEscalier070, vm.ChoixTypesMaille);
        Assert.DoesNotContain("HMCON700E", vm.Consoles);

        // Valeur utilisée par la grille : gardée dans la liste.
        vm.Mailles[0].Type = TypeMaille.DemiEscalier070;
        Assert.Contains(TypeMaille.DemiEscalier070, vm.ChoixTypesMaille);
    }

    [Fact]
    public void Nouveau_avec_les_parametres_par_defaut()
    {
        var d = ParametresParDefaut.Usine();
        d.General.Plateaux = GammePlateaux.Optissimo;
        d.DessinerFacade = true;
        var vm = new MultiDEC.App.ViewModels.FacadeViewModel { Defauts = d };
        vm.Nouveau();
        Assert.Equal(GammePlateaux.Optissimo, vm.General.Plateaux);
        Assert.True(vm.DessinerFacade);
        Assert.True(vm.Parametres.LissesEnPied);
    }
}
