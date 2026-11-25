using System.IO;
using System.Linq;
using MultiDEC.Core.Decompte;
using MultiDEC.Core.Facade;
using MultiDEC.Core.Geometrie;
using MultiDEC.Core.Persistance;
using Xunit;

namespace MultiDEC.Core.Tests;

/// <summary>Travail en références client (Laurent, 25/11/2026) : LV Tec intégré, autre client par fichier CSV.</summary>
public class ReferencesClientTests
{
    [Fact]
    public void Table_LV_Tec_integree()
    {
        var lv = ReferencesClient.LvTec;
        Assert.True(lv.Nombre > 400);
        Assert.True(lv.TryGet("HMSOCREG", out var socle));
        Assert.Equal("A1SOR050_L", socle);
        Assert.True(lv.TryGet("hmemb", out var embase)); // casse indifférente
        Assert.Equal("E1EMBASE_L", embase);
    }

    [Fact]
    public void Decompte_en_references_LV_Tec()
    {
        var s = GenerateurStructure.Generer(ProjetStructure.Nouveau(face: new ParametresFacade { LongueurTotaleSouhaitee = 10, HauteurDernierPlancher = 4 }));
        var lignes = CalculDecompte.Calculer(s.Elements, null, ReferencesClient.LvTec);
        var socle = lignes.Single(l => l.Reference == "HMSOCREG");
        Assert.Equal("A1SOR050_L", socle.ReferenceAffichee);

        // Sans référence client : la référence RETOTUB reste affichée.
        var retotub = CalculDecompte.Calculer(s.Elements);
        Assert.All(retotub, l => Assert.Equal(l.Reference, l.ReferenceAffichee));
        Assert.Empty(CalculDecompte.SansReferenceClient(lignes).Where(r => r == "HMSOCREG"));
    }

    [Fact]
    public void Fichier_CSV_du_client()
    {
        var table = ReferencesClient.Lire(new StringReader("RefRETOTUB;RefClient\nHMSOCREG;SOC-01\nHMPOT20;\nHMEMB;EMB-01\nHMEMB;AUTRE\n"), "Client");
        Assert.Equal(2, table.Nombre);
        Assert.True(table.TryGet("HMEMB", out var e));
        Assert.Equal("EMB-01", e);
        Assert.False(table.TryGet("HMPOT20", out _));
    }

    [Fact]
    public void Parametre_references_affichees_enregistre()
    {
        var p = ParametresParDefaut.Usine();
        Assert.Equal(ReferencesClient.Retotub, p.ReferencesAffichees);
        p.ReferencesAffichees = ReferencesClient.CodeLvTec;
        Assert.Equal(ReferencesClient.CodeLvTec, ParametresParDefaut.DepuisJson(p.VersJson()).ReferencesAffichees);
        Assert.Same(ReferencesClient.LvTec, ReferencesClient.Selon(ReferencesClient.CodeLvTec, null, out _));
        Assert.Null(ReferencesClient.Selon(ReferencesClient.CodeFichier, "/introuvable.csv", out var erreur));
        Assert.NotNull(erreur);
    }

    [Fact]
    public void Decompte_de_l_appli_bascule_en_references_client()
    {
        var vm = new App.ViewModels.FacadeViewModel();
        vm.Generer();
        Assert.False(vm.EnReferencesClient);
        Assert.Contains(vm.Decompte, l => l.ReferenceAffichee == "HMSOCREG");

        vm.ReferencesAffichees = ReferencesClient.CodeLvTec;
        Assert.True(vm.EnReferencesClient);
        Assert.Equal("Réf. LV Tec", vm.TitreReference);
        Assert.Contains(vm.Decompte, l => l.ReferenceAffichee == "A1SOR050_L" && l.Reference == "HMSOCREG");

        var d = ParametresParDefaut.Usine();
        d.ReferencesAffichees = ReferencesClient.CodeLvTec;
        var vm2 = new App.ViewModels.FacadeViewModel { Defauts = d };
        Assert.True(vm2.EnReferencesClient);
    }
}
