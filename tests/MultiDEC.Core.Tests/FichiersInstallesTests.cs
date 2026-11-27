using System;
using System.IO;
using System.Linq;
using MultiDEC.App.ViewModels;
using MultiDEC.Core.Decompte;
using MultiDEC.Core.Persistance;
using Xunit;

namespace MultiDEC.Core.Tests;

/// <summary>Remarques 60 et 62 (Laurent, 27/11/2026) : fichiers installés avec MultiDEC, références affichées par défaut.</summary>
public class FichiersInstallesTests
{
    private static string DossierProgramme(bool avecFichiers)
    {
        string d = Path.Combine(Path.GetTempPath(), "multidec-test-" + Guid.NewGuid().ToString("N"));
        string bib = Path.Combine(d, ParametresParDefaut.DossierBibliothequeInstallee);
        Directory.CreateDirectory(bib);
        if (avecFichiers)
        {
            File.WriteAllText(Path.Combine(bib, ParametresParDefaut.NomBibliothequeInstallee), "dwg");
            File.WriteAllText(Path.Combine(bib, CatalogueArticles.NomFichier), "bib");
        }

        return d;
    }

    [Fact]
    public void Chemins_vides_remplis_avec_les_fichiers_installes()
    {
        string d = DossierProgramme(true);
        var p = ParametresParDefaut.Usine();
        Assert.True(p.CompleterFichiersInstalles(d));
        Assert.Equal(Path.Combine(d, "Bibliotheque", "Blocs 3D - Echafaudage.dwg"), p.Bibliotheque);
        Assert.Equal(Path.Combine(d, "Bibliotheque", "multidec.bib"), p.Articles);
        Assert.False(p.CompleterFichiersInstalles(d)); // déjà remplis

        // Choix de l'utilisateur conservé.
        var perso = new ParametresParDefaut { Bibliotheque = @"X:\perso.dwg" };
        perso.CompleterFichiersInstalles(d);
        Assert.Equal(@"X:\perso.dwg", perso.Bibliotheque);
        Assert.NotNull(perso.Articles);

        // Enregistré avec les paramètres.
        Assert.Equal(p.Articles, ParametresParDefaut.DepuisJson(p.VersJson()).Articles);
    }

    [Fact]
    public void Rien_a_remplir_sans_fichiers_installes()
    {
        var p = ParametresParDefaut.Usine();
        Assert.False(p.CompleterFichiersInstalles(DossierProgramme(false)));
        Assert.Null(p.Bibliotheque);
        Assert.Null(p.Articles);
    }

    [Fact]
    public void Multidec_bib_des_parametres_sinon_a_cote_de_la_bibliotheque_sinon_installe()
    {
        string installe = DossierProgramme(true);
        string autre = DossierProgramme(true);
        string bibliothequeAutre = Path.Combine(autre, "Bibliotheque", ParametresParDefaut.NomBibliothequeInstallee);
        var p = ParametresParDefaut.Usine();
        Assert.Equal(Path.Combine(autre, "Bibliotheque", "multidec.bib"), p.CheminArticles(bibliothequeAutre, installe));
        Assert.Equal(Path.Combine(installe, "Bibliotheque", "multidec.bib"), p.CheminArticles(@"Z:\absent\blocs.dwg", installe));
        p.Articles = Path.Combine(installe, "Bibliotheque", "multidec.bib");
        Assert.Equal(p.Articles, p.CheminArticles(bibliothequeAutre, installe));
        p.Articles = @"Z:\absent\multidec.bib"; // introuvable : on retombe sur le voisin
        Assert.Equal(Path.Combine(autre, "Bibliotheque", "multidec.bib"), p.CheminArticles(bibliothequeAutre, installe));
    }

    [Fact]
    public void References_affichees_reglees_dans_l_onglet_fichiers()
    {
        var vm = new ParametresParDefautViewModel(ParametresParDefaut.Usine());
        Assert.Equal(ReferencesClient.Retotub, vm.ReferencesAffichees);
        Assert.DoesNotContain(vm.Listes, l => l.Titre == "Références du décompte");
        Assert.Contains(vm.Fichiers, f => f.Titre == "Catalogue multidec.bib");
        vm.ReferencesAffichees = ReferencesClient.CodeLvTec;
        Assert.Equal(ReferencesClient.CodeLvTec, vm.Resultat.ReferencesAffichees);
        Assert.Equal(3, ParametresParDefautViewModel.ChoixReferences.Count);
    }
}
