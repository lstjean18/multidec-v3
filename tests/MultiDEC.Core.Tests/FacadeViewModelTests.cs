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
        vm.Mailles[0].Console = "HMCON700E";

        var s = vm.Generer();

        Assert.Equal(avant + 1, vm.Projet.Mailles.Count);
        Assert.Contains(s.Elements, e => e.Reference == "HMCON700E");
        Assert.NotEmpty(vm.Decompte);
    }

    [Fact]
    public void Console_vide_equivaut_a_sans_console()
    {
        var vm = new FacadeViewModel();
        foreach (var m in vm.Mailles)
        {
            m.Console = string.Empty;
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
}
