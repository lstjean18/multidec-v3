using System.Collections.Generic;
using System.Linq;
using MultiDEC.Core.Decompte;
using MultiDEC.Core.Facade;
using MultiDEC.Core.Geometrie;
using MultiDEC.Core.MiseEnPlan;
using MultiDEC.Core.Persistance;
using Xunit;

namespace MultiDEC.Core.Tests;

/// <summary>Remarque 61 (Laurent, 27/11/2026) : décompte modifiable, références ajoutées à la main.</summary>
public class DecompteModifiableTests
{
    private static readonly CatalogueArticles Catalogue = new CatalogueArticles(new[]
    {
        new Article("HMPOT20", "Poteau de 2,00m", 9.1, "Poteaux", 2.06),
        new Article("HMSOCREG", "Socle réglable", 3.5, "Socles", 1.01),
        new Article("HMCHAPEDOUBLE", "Double tête", 0.9, "Accessoires", 21.18, "DT-01"),
        new Article("HMCOLLIER", "Collier orthogonal", 1.2, "Accessoires", 21.02),
    });

    private static List<ElementPlace> Elements()
    {
        var e = new List<ElementPlace>();
        for (int i = 0; i < 4; i++)
        {
            e.Add(new ElementPlace("HMPOT20", Famille.Poteau, new Point3(i, 0, 0), Axe.Z));
            e.Add(new ElementPlace("HMSOCREG", Famille.Socle, new Point3(i, 0, 0), Axe.Z));
        }

        return e;
    }

    [Fact]
    public void Quantite_imposee_remplace_la_quantite_calculee()
    {
        var corrections = new List<CorrectionDecompte> { new CorrectionDecompte { Reference = "hmpot20", Quantite = 6 } };
        var lignes = CalculDecompte.Calculer(Elements(), Catalogue, null, corrections);
        var poteaux = lignes.Single(l => l.Reference == "HMPOT20");
        Assert.Equal(6, poteaux.Quantite);
        Assert.Equal(4, poteaux.QuantiteCalculee);
        Assert.True(poteaux.Modifiee);
        Assert.False(poteaux.Ajoutee);
        Assert.Equal("M", poteaux.Repere);
        Assert.Equal(6 * 9.1, poteaux.PoidsTotal!.Value, 6);
        Assert.Equal(6 * 9.1 + 4 * 3.5, CalculDecompte.PoidsTotal(lignes), 6);
    }

    [Fact]
    public void Reference_ajoutee_avec_designation_poids_et_famille_du_catalogue()
    {
        var corrections = new List<CorrectionDecompte> { new CorrectionDecompte { Reference = "HMCOLLIER", Quantite = 10 } };
        var lignes = CalculDecompte.Calculer(Elements(), Catalogue, null, corrections);
        var collier = lignes.Single(l => l.Reference == "HMCOLLIER");
        Assert.True(collier.Ajoutee);
        Assert.Equal("*", collier.Repere);
        Assert.Equal("Collier orthogonal", collier.Designation);
        Assert.Equal("Accessoires", collier.FamilleAffichee);
        Assert.Equal(12.0, collier.PoidsTotal!.Value, 6);
        Assert.Equal("HMCOLLIER", lignes.Last().Reference); // classement 21.02 : après socles et poteaux
    }

    [Fact]
    public void Reference_inconnue_ajoutee_en_famille_divers_sans_poids()
    {
        var corrections = new List<CorrectionDecompte> { new CorrectionDecompte { Reference = "XYZ99", Quantite = 2 } };
        var lignes = CalculDecompte.Calculer(Elements(), Catalogue, null, corrections);
        var l = lignes.Last();
        Assert.Equal("XYZ99", l.Reference);
        Assert.Equal("Divers", l.FamilleAffichee);
        Assert.Null(l.PoidsUnitaire);
        Assert.Contains("XYZ99", CalculDecompte.SansPoids(lignes));
    }

    [Fact]
    public void Imposer_retire_la_correction_quand_on_revient_a_la_quantite_calculee()
    {
        var corrections = new List<CorrectionDecompte>();
        CalculDecompte.Imposer(corrections, "HMPOT20", 6, 4);
        CalculDecompte.Imposer(corrections, "HMPOT20", 7, 4);
        Assert.Equal(7, Assert.Single(corrections).Quantite);
        CalculDecompte.Imposer(corrections, "HMPOT20", 4, 4);
        Assert.Empty(corrections);

        // Référence ajoutée remise à 0 : elle disparaît ; ligne calculée mise à 0 : la correction reste.
        CalculDecompte.Imposer(corrections, "HMCOLLIER", 3, 0);
        CalculDecompte.Imposer(corrections, "HMCOLLIER", 0, 0);
        Assert.Empty(corrections);
        CalculDecompte.Imposer(corrections, "HMSOCREG", 0, 4);
        var socles = CalculDecompte.Calculer(Elements(), Catalogue, null, corrections).Single(l => l.Reference == "HMSOCREG");
        Assert.Equal(0, socles.Quantite);
        Assert.True(socles.Modifiee);
    }

    [Fact]
    public void Saisie_reconnue_en_reference_retotub_ou_client()
    {
        var client = new ReferencesClient("LV Tec", new[] { new KeyValuePair<string, string>("HMPOT20", "LV-P200") });
        Assert.Equal("HMPOT20", CalculDecompte.ReferenceRetotub(" hmpot20 ", Catalogue, null));
        Assert.Equal("HMPOT20", CalculDecompte.ReferenceRetotub("lv-p200", Catalogue, client));
        Assert.Equal("HMCHAPEDOUBLE", CalculDecompte.ReferenceRetotub("DT-01", Catalogue, client)); // RefClient de multidec.bib
        Assert.Null(CalculDecompte.ReferenceRetotub("INCONNUE", Catalogue, client));
        Assert.Null(CalculDecompte.ReferenceRetotub("  ", Catalogue, client));
    }

    [Fact]
    public void Liste_materiel_asterisque_legende_et_lignes_a_zero_retirees()
    {
        var corrections = new List<CorrectionDecompte>
        {
            new CorrectionDecompte { Reference = "HMCOLLIER", Quantite = 10 },
            new CorrectionDecompte { Reference = "HMSOCREG", Quantite = 0 },
        };
        var lignes = CalculDecompte.Calculer(Elements(), Catalogue, null, corrections);
        var f = ListeMateriel.Feuilles(new EnTeteListe { Chantier = "Essai" }, lignes, new string[0]).Single();
        var textes = f.Textes.Select(t => t.Contenu).ToList();
        Assert.Contains("HMCOLLIER *", textes);
        Assert.Contains("HMPOT20", textes);
        Assert.DoesNotContain("HMSOCREG", textes);
        Assert.Contains(ListeMateriel.LegendeAjout, textes);
        Assert.Contains("Total : 2 références, 14 pièces", textes);

        // Sans référence ajoutée : pas de légende.
        var sans = ListeMateriel.Feuilles(new EnTeteListe(), CalculDecompte.Calculer(Elements(), Catalogue), new string[0]).Single();
        Assert.DoesNotContain(ListeMateriel.LegendeAjout, sans.Textes.Select(t => t.Contenu));
    }

    [Fact]
    public void Corrections_enregistrees_avec_le_projet()
    {
        var projet = ParametresParDefaut.Usine().NouveauProjet();
        projet.CorrectionsDecompte.Add(new CorrectionDecompte { Reference = "HMCOLLIER", Quantite = 10 });
        var relu = FichierProjet.DepuisJson(FichierProjet.VersJson(projet));
        var c = Assert.Single(relu.CorrectionsDecompte);
        Assert.Equal("HMCOLLIER", c.Reference);
        Assert.Equal(10, c.Quantite);

        // Anciens fichiers sans corrections : liste vide.
        var json = System.Text.Json.Nodes.JsonNode.Parse(FichierProjet.VersJson(projet))!;
        var structure = json["structure"]!.AsObject();
        Assert.True(structure.Remove("correctionsDecompte"));
        Assert.Empty(FichierProjet.DepuisJson(json.ToJsonString()).CorrectionsDecompte);
    }
}

/// <summary>Remarque 61 côté écran : modification, ajout, rétablissement.</summary>
public class DecompteModifiableEcranTests
{
    [Fact]
    public void Modifier_ajouter_retablir_dans_le_decompte()
    {
        var vm = new MultiDEC.App.ViewModels.FacadeViewModel();
        vm.Articles = new CatalogueArticles(new[] { new Article("HMPOT20", "Poteau de 2,00m", 9.1, "Poteaux", 2.06) });
        Assert.Null(vm.AjouterAuDecompte("HMCOLLIER", 2)); // rien sans structure générée
        vm.Generer();
        var premiere = vm.Decompte.First();
        int calculee = premiere.Quantite;

        vm.ModifierQuantite(premiere.Reference, calculee + 3);
        var modifiee = vm.Decompte.Single(l => l.Reference == premiere.Reference);
        Assert.Equal(calculee + 3, modifiee.Quantite);
        Assert.True(modifiee.Modifiee);
        Assert.True(vm.DecompteCorrige);
        Assert.Contains("1 quantité modifiée (M)", vm.ResumeDecompte);

        var ajoutee = vm.AjouterAuDecompte("XYZ99", 4)!;
        Assert.True(ajoutee.Ajoutee);
        Assert.Equal(4, ajoutee.Quantite);
        Assert.Equal(6, vm.AjouterAuDecompte("XYZ99", 2)!.Quantite); // ajoutée deux fois : quantités cumulées

        // Conservé à la génération suivante (et enregistré dans le projet).
        vm.Generer();
        Assert.Equal(6, vm.Decompte.Single(l => l.Reference == "XYZ99").Quantite);
        Assert.Equal(2, vm.Projet.CorrectionsDecompte.Count);

        vm.RetablirLigne(premiere.Reference);
        Assert.Equal(calculee, vm.Decompte.Single(l => l.Reference == premiere.Reference).Quantite);
        vm.RetablirDecompte();
        Assert.DoesNotContain(vm.Decompte, l => l.Reference == "XYZ99");
        Assert.False(vm.DecompteCorrige);
    }
}
