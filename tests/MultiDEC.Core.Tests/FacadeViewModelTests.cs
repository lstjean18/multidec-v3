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
}
