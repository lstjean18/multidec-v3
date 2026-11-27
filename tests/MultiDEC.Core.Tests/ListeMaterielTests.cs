using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MultiDEC.Core.Decompte;
using MultiDEC.Core.Facade;
using MultiDEC.Core.MiseEnPlan;
using Xunit;

namespace MultiDEC.Core.Tests;

/// <summary>Liste du matériel A4 (Laurent, 27/11/2026) : en-tête, tableau par famille, totaux, alertes, pied.</summary>
public class ListeMaterielTests
{
    private static EnTeteListe EnTete(string? client = null) => new EnTeteListe
    {
        Chantier = "Résidence Les Tilleuls – façade nord",
        Client = "Entreprise Martin",
        ReferenceEtude = "ET-2026-118",
        Date = new DateTime(2026, 11, 27),
        LogoClient = @"C:\logos\client.png",
        NomReferencesClient = client,
    };

    private static List<LigneDecompte> Lignes(int nombre)
    {
        var lignes = new List<LigneDecompte>();
        for (int i = 0; i < nombre; i++)
        {
            string famille = i < nombre / 3 ? "Poteaux" : i < 2 * nombre / 3 ? "Plateaux Ac" : "Accessoires";
            var a = new Article("HMREF" + i, "Plateau acier 0,30 x 2,50m profil fermé (NF) avec crochets anti-soulèvement n° " + i, i % 7 == 0 ? null : 9.1 + i, famille, i);
            lignes.Add(new LigneDecompte("HMREF" + i, Famille.Poteau, 10 + i, a, i % 5 == 0 ? null : "LV" + i));
        }

        return lignes;
    }

    [Fact]
    public void Une_page_avec_entete_tableau_totaux_alertes_et_pied()
    {
        var lignes = Lignes(12);
        var feuilles = ListeMateriel.Feuilles(EnTete(), lignes, new[] { "Hauteur > 24 m : note de calcul nécessaire." });
        var f = Assert.Single(feuilles);
        Assert.Equal(210, f.Format.Largeur);
        Assert.Equal(297, f.Format.Hauteur);
        var textes = f.Textes.Select(t => t.Contenu).ToList();
        Assert.Contains("LISTE DU MATÉRIEL", textes);
        Assert.Contains("Entreprise Martin", textes);
        Assert.Contains("ET-2026-118", textes);
        Assert.Contains("27/11/2026", textes);
        Assert.Contains("Décompte réalisé avec MultiDEC v3", textes);
        Assert.Contains("Page 1 / 1", textes);
        Assert.Contains(f.Primitives.OfType<ImageFeuille>(), i => i.Source == @"C:\logos\client.png");
        foreach (var famille in new[] { "Poteaux", "Plateaux Ac", "Accessoires" })
        {
            Assert.Contains(famille, textes);
        }

        Assert.Contains("HMREF3", textes);
        Assert.Contains("Alertes (1)", textes);
        Assert.Contains("Hauteur > 24 m : note de calcul nécessaire.", textes);
        int pieces = lignes.Sum(l => l.Quantite);
        Assert.Contains($"Total : 12 références, {pieces} pièces", textes);

        // Tout dans la feuille, le tableau entre l'en-tête et le pied.
        Assert.All(f.Textes, t =>
        {
            Assert.InRange(t.Position.U, 10, 200);
            Assert.InRange(t.Position.V, 10, 290);
        });
    }

    [Fact]
    public void Logo_RETOTUB_sans_logo_client()
    {
        var e = EnTete();
        e.LogoClient = null;
        var f = ListeMateriel.Feuilles(e, Lignes(3), Array.Empty<string>()).Single();
        Assert.Contains(f.Primitives.OfType<ImageFeuille>(), i => i.Source == "RETOTUB");
    }

    [Fact]
    public void Sans_reference_RETOTUB_ni_bandeaux_de_famille()
    {
        // Remarque 58 (Laurent, 27/11/2026) : cases des Paramètres par défaut.
        var lignes = Lignes(12);
        var e = EnTete();
        e.AfficherReferenceRetotub = false;
        e.AfficherFamilles = false;
        var f = ListeMateriel.Feuilles(e, lignes, Array.Empty<string>()).Single();
        var textes = f.Textes.Select(t => t.Contenu).ToList();
        Assert.DoesNotContain("Référence", textes);
        Assert.DoesNotContain(textes, t => t.StartsWith("HMREF", StringComparison.Ordinal));
        Assert.DoesNotContain("Poteaux", textes);
        Assert.DoesNotContain(f.Primitives.OfType<Polygone>(), g => g.Encre == Encre.FondFamille);
        Assert.Contains("Désignation", textes);

        // En références client : seulement la colonne du client, vide sans correspondance.
        e.NomReferencesClient = "LV Tec";
        f = ListeMateriel.Feuilles(e, lignes, ListeMateriel.AlertesDecompte(lignes, "LV Tec", false)).Single();
        textes = f.Textes.Select(t => t.Contenu).ToList();
        Assert.Contains("Réf. LV Tec", textes);
        Assert.Contains("LV3", textes);
        Assert.DoesNotContain("Réf. RETOTUB", textes);
        Assert.DoesNotContain(textes, t => t.StartsWith("HMREF", StringComparison.Ordinal));
        Assert.Contains(textes, t => t.Contains("référence laissée vide"));
    }

    [Fact]
    public void Plusieurs_pages_quand_la_liste_est_longue()
    {
        var feuilles = ListeMateriel.Feuilles(EnTete(), Lignes(90), Array.Empty<string>());
        Assert.True(feuilles.Count >= 2, $"{feuilles.Count} page(s)");
        foreach (var f in feuilles)
        {
            Assert.Contains(f.Textes, t => t.Contenu == $"Page {f.Page.Numero} / {feuilles.Count}");
            Assert.Contains(f.Textes, t => t.Contenu == "Entreprise Martin");
            Assert.Contains(f.Textes, t => t.Contenu == "Désignation" && t.Encre == Encre.Blanc);
            Assert.All(f.Textes.Where(t => t.Contenu.StartsWith("HMREF", StringComparison.Ordinal)), t => Assert.InRange(t.Position.V, 20, 245));
        }

        // Chaque ligne une fois, et la suite de famille signalée.
        var refs = feuilles.SelectMany(f => f.Textes).Where(t => t.Contenu.StartsWith("HMREF", StringComparison.Ordinal)).Select(t => t.Contenu).ToList();
        Assert.Equal(90, refs.Distinct().Count());
        Assert.Equal(90, refs.Count);
        Assert.Contains(feuilles.Skip(1).SelectMany(f => f.Textes), t => t.Contenu.EndsWith("(suite)", StringComparison.Ordinal));
        Assert.Contains(feuilles.Last().Textes, t => t.Contenu == "Aucune alerte.");
    }

    [Fact]
    public void References_client_et_alertes_du_decompte()
    {
        var lignes = Lignes(12);
        var alertes = ListeMateriel.AlertesDecompte(lignes, "LV Tec");
        Assert.Equal(2, alertes.Count);
        Assert.StartsWith("Poids inconnu", alertes[0]);
        Assert.Contains("HMREF0, HMREF7", alertes[0]);
        Assert.Contains("Sans référence LV Tec", alertes[1]);

        var f = ListeMateriel.Feuilles(EnTete("LV Tec"), lignes, alertes).Single();
        Assert.Contains(f.Textes, t => t.Contenu == "Réf. LV Tec");
        Assert.Contains(f.Textes, t => t.Contenu == "LV3");
        Assert.Contains(f.Textes, t => t.Contenu == "Alertes (2)");
        Assert.DoesNotContain(ListeMateriel.AlertesDecompte(lignes, null), a => a.Contains("LV Tec"));
    }

    [Fact]
    public void Les_textes_tiennent_dans_leurs_colonnes()
    {
        var morceaux = ListeMateriel.Couper("Plateau acier 0,30 x 2,50m profil fermé (NF) avec crochets", 40, ListeMateriel.HauteurTexte);
        Assert.True(morceaux.Count >= 2);
        Assert.All(morceaux, m => Assert.True(ListeMateriel.Largeur(m, ListeMateriel.HauteurTexte) <= 40, m));
        Assert.Equal(new[] { "" }, ListeMateriel.Couper("", 40, 2));
        Assert.All(ListeMateriel.Couper("HMPLAF2502CL6-ACXXXXXXXXXXXX", 20, 2.1), m => Assert.True(ListeMateriel.Largeur(m, 2.1) <= 20, m));
    }

    /// <summary>Aperçu SVG (MULTIDEC_APERCU=dossier).</summary>
    [Fact]
    public void Apercu_svg()
    {
        var dossier = Environment.GetEnvironmentVariable("MULTIDEC_APERCU");
        if (string.IsNullOrEmpty(dossier))
        {
            return;
        }

        var projet = ProjetStructure.Nouveau(face: new ParametresFacade { LongueurTotaleSouhaitee = 15, LongueurMaillePreferee = 2.5, HauteurDernierPlancher = 6, NombreAcces = 1 });
        var s = GenerateurStructure.Generer(projet);
        var lignes = CalculDecompte.Calculer(s.Elements);
        var lignesLongues = Lignes(70);
        foreach (var (nom, l, al) in new[] { ("liste", lignes, s.Alertes.Concat(ListeMateriel.AlertesDecompte(lignes, null)).ToList()), ("longue", (IReadOnlyList<LigneDecompte>)lignesLongues, ListeMateriel.AlertesDecompte(lignesLongues, "LV Tec")) })
        {
            var e = EnTete(nom == "longue" ? "LV Tec" : null);
            foreach (var f in ListeMateriel.Feuilles(e, l, al))
            {
                File.WriteAllText(Path.Combine(dossier, $"{nom}-{f.Page.Numero}.svg"), MiseEnPageTests.SvgDe(f));
            }
        }
    }
}
