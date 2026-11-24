using MultiDEC.App.ViewModels;
using MultiDEC.Core.Facade;
using Xunit;

namespace MultiDEC.Core.Tests;

public class FacadeViewModelTests
{
    [Fact]
    public void Grille_editee_prise_en_compte_a_la_generation()
    {
        var vm = new FacadeViewModel();
        int avant = vm.Mailles.Count;
        vm.Mailles.Add(new MailleDefinition(1.5));
        vm.Niveaux.First(n => n.Hauteur == 4).Console = "HMCON700E";

        var s = vm.Generer();

        Assert.Equal(avant + 1, vm.Projet.S1.Mailles.Count);
        Assert.Contains(s.Elements, e => e.Reference == "HMCON700E");
        Assert.NotEmpty(vm.Decompte);
    }

    [Fact]
    public void Maille_passee_en_recette_coche_le_dernier_niveau()
    {
        var vm = new FacadeViewModel();
        Assert.DoesNotContain(vm.Niveaux, n => n.Recette);
        vm.Mailles[0].Type = TypeMaille.Recette;
        var coches = vm.Niveaux.Where(n => n.Recette).ToList();
        Assert.Single(coches);
        Assert.Equal(vm.Niveaux.Max(n => n.Hauteur), coches[0].Hauteur);

        // Tout décoché : la recette n'est pas dessinée, l'utilisateur est prévenu.
        coches[0].Recette = false;
        var s = vm.Generer();
        Assert.Contains(s.Alertes, a => a.Contains("ne sera pas représentée"));
    }

    [Fact]
    public void Console_vide_equivaut_a_sans_console()
    {
        var vm = new FacadeViewModel();
        foreach (var n in vm.Niveaux)
        {
            n.Console = string.Empty;
        }

        Assert.DoesNotContain(vm.Generer().Elements, e => e.Famille == Famille.Console);
    }

    [Fact]
    public void Liste_des_consoles_commence_par_sans_console()
    {
        var vm = new FacadeViewModel();
        Assert.Equal(string.Empty, vm.Consoles[0]);
        Assert.Contains("HMCON700E", vm.Consoles);
    }

    [Theory]
    [InlineData(10, 1)]
    [InlineData(20, 1)]
    [InlineData(20.5, 2)]
    [InlineData(40, 2)]
    [InlineData(41, 3)]
    [InlineData(62, 4)]
    public void Nombre_d_acces_conseille(double longueur, int attendu) =>
        Assert.Equal(attendu, MultiDEC.Core.Facade.Regles.RegleAcces.NombreConseille(longueur));

    [Fact]
    public void Changer_la_longueur_recalcule_les_acces_puis_l_utilisateur_peut_les_modifier()
    {
        var vm = new FacadeViewModel();
        var notifies = new List<string?>();
        vm.PropertyChanged += (_, e) => notifies.Add(e.PropertyName);

        vm.LongueurFacade = 45;
        Assert.Equal(3, vm.NombreAcces);
        Assert.Equal(3, vm.Parametres.NombreAcces);
        Assert.Contains(nameof(FacadeViewModel.NombreAcces), notifies);

        vm.NombreAcces = 2; // choix de l'utilisateur, conservé tant que la longueur ne change pas
        Assert.Equal(2, vm.Parametres.NombreAcces);
        vm.LongueurFacade = 45;
        Assert.Equal(2, vm.NombreAcces);
    }

    [Fact]
    public void Supprimer_des_blocs_dans_le_dessin_met_a_jour_le_decompte()
    {
        var vm = new FacadeViewModel();
        var structure = vm.Generer();
        var socles = structure.Elements.Where(e => e.Reference == "HMSOCREG").Take(2).ToList();
        int avant = vm.Decompte.Single(l => l.Reference == "HMSOCREG").Quantite;

        vm.RetirerElements(socles);

        Assert.Equal(avant - 2, vm.Decompte.Single(l => l.Reference == "HMSOCREG").Quantite);
        Assert.Equal(structure.Elements.Count - 2, vm.Structure!.Elements.Count);
    }

    [Fact]
    public void Console_ajoutee_a_un_niveau_cochee_sur_toutes_les_mailles_et_colonne_creee()
    {
        var vm = new FacadeViewModel();
        int signaux = 0;
        vm.ColonnesConsolesChangees += (_, _) => signaux++;
        var n4 = vm.Niveaux.Single(n => n.Hauteur == 4);

        n4.Console = "HMCON700E";

        Assert.True(signaux > 0);
        Assert.Equal("Con4m", vm.NiveauxAvecConsole.Single().NomColonneConsole);
        Assert.All(vm.Mailles, m => Assert.True(m["4"]));

        vm.Mailles[0]["4"] = false; // l'utilisateur décoche une maille
        Assert.False(vm.Mailles[0].ConsoleA(4));

        n4.Hauteur = 4.5;            // la case suit le niveau
        Assert.True(vm.Mailles[1]["4.5"]);
        Assert.False(vm.Mailles[1]["4"]);
        Assert.Equal("Con4,5m", vm.NiveauxAvecConsole.Single().NomColonneConsole);

        n4.Console = null;           // console retirée : cases effacées, plus de colonne
        Assert.Empty(vm.NiveauxAvecConsole);
        Assert.All(vm.Mailles, m => Assert.Empty(m.Consoles));
    }

    [Fact]
    public void Pare_gravois_coche_a_un_niveau_colonne_creee_et_toutes_les_mailles_cochees()
    {
        var vm = new FacadeViewModel();
        int signaux = 0;
        vm.ColonnesConsolesChangees += (_, _) => signaux++;
        var n4 = vm.Niveaux.Single(n => n.Hauteur == 4);

        n4.PareGravois = true;

        Assert.True(signaux > 0);
        Assert.Equal("PareGrav4m", vm.NiveauxAvecPareGravois.Single().NomColonnePareGravois);
        Assert.All(vm.Mailles, m => Assert.True(m["PG4"]));
        Assert.All(vm.Mailles, m => Assert.False(m["4"]));

        vm.Mailles[0]["PG4"] = false;
        var s = vm.Generer();
        Assert.DoesNotContain(s.Elements, e => e.Famille == Famille.PareGravois && e.NumeroMaille == 1);

        n4.Hauteur = 4.5;
        Assert.True(vm.Mailles[1]["PG4.5"]);

        n4.PareGravois = false;
        Assert.Empty(vm.NiveauxAvecPareGravois);
        Assert.All(vm.Mailles, m => Assert.Empty(m.PareGravois));
    }

    [Fact]
    public void Niveau_supprime_efface_ses_cases_console()
    {
        var vm = new FacadeViewModel();
        var n6 = vm.Niveaux.Single(n => n.Hauteur == 6);
        n6.Console = "HMCON700E";
        vm.Niveaux.Remove(n6);
        Assert.All(vm.Mailles, m => Assert.Empty(m.Consoles));
        Assert.Empty(vm.NiveauxAvecConsole);
    }

    [Fact]
    public void Proposer_les_diagonales_recoche_selon_la_regle()
    {
        var vm = new FacadeViewModel();
        foreach (var m in vm.Mailles)
        {
            m.Diagonale = false;
        }

        vm.Parametres.DiagonalesRenforcees = true; // une maille sur 2 (règle v2)
        vm.ProposerLesDiagonales.Execute(null);
        Assert.Equal(new[] { 1, 3, 5, 7 }, vm.Mailles.Select((m, i) => (m, i)).Where(x => x.m.Diagonale).Select(x => x.i + 1));
    }

    [Fact]
    public void Choisir_la_protection_bascule_la_plinthe()
    {
        var vm = new FacadeViewModel();
        vm.SecuriteExterieure = TypeSecurite.GardeCorpsMds2;
        Assert.Equal(TypePlinthe.Bois, vm.General.PlintheExterieure);
        vm.SecuriteInterieure = TypeSecurite.Lisses;
        Assert.Equal(TypePlinthe.Axees, vm.General.PlintheInterieure);
        vm.SecuriteAbouts = TypeSecurite.Sans;
        Assert.Equal(TypePlinthe.Sans, vm.PlintheAbouts);

        // La plinthe reste modifiable à la main ; le garde-corps d'about ne la change pas.
        vm.PlintheAbouts = TypePlinthe.Deportees;
        vm.SecuriteAbouts = TypeSecurite.GardeCorpsAbout;
        Assert.Equal(TypePlinthe.Deportees, vm.General.PlintheAbouts);
    }

    [Fact]
    public void Garde_corps_mds1_absent_des_choix()
    {
        Assert.DoesNotContain("GardeCorpsMds1", FacadeViewModel.Securites.Cast<object>().Select(o => o.ToString()));
        Assert.Equal(TypeSecurite.GardeCorpsMds2, new FacadeViewModel().Parametres.SecuriteExterieure);
    }
}
