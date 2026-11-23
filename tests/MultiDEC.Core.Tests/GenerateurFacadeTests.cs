using MultiDEC.Core.Catalogue;
using MultiDEC.Core.Decompte;
using MultiDEC.Core.Facade;
using MultiDEC.Core.Persistance;
using Xunit;

namespace MultiDEC.Core.Tests;

public class GenerateurFacadeTests
{
    private static ProjetFacade Standard(Action<ParametresFacade>? config = null)
    {
        var p = new ParametresFacade
        {
            Modele = ModeleEchafaudage.MultiSystem,
            LargeurMaille = 0.70,
            LongueurTotaleSouhaitee = 20,
            LongueurMaillePreferee = 2.5,
            NombreAcces = 1,
            HauteurDernierPlancher = 10,
        };
        config?.Invoke(p);
        return ProposeurStructure.NouveauProjet(p);
    }

    private static int Nb(StructureFacade s, string reference) => s.Elements.Count(e => e.Reference == reference);

    [Fact]
    public void Facade_standard_quantites_principales()
    {
        var s = GenerateurFacade.Generer(Standard());

        // 8 mailles → 9 files x 2 poteaux
        Assert.Equal(18, Nb(s, "HMSOCREG"));
        Assert.Equal(18, Nb(s, "HMEMB"));
        // Garde-corps MDS1 extérieur : 7 mailles de travail x 5 niveaux complets + accès sur 5 niveaux
        Assert.Equal(8 * 5, Nb(s, "HMGCSE250"));
        // Diagonales : mailles 1 et 6, 5 diagonales de 2 m chacune
        Assert.Equal(10, Nb(s, "HMDIA2502"));
        // Trappes aux niveaux 2 à 10 dans la maille d'accès
        Assert.Equal(5, Nb(s, "HMPLAL250602TREC"));
        Assert.DoesNotContain(s.Elements, e => e.Reference == References.Echelle); // maille > 2 m : pas d'échelle intégrée
    }

    private static int LissesA(StructureFacade s, int maille, double y, double z) =>
        s.Elements.Count(e => e.Famille == Famille.Lisse && e.NumeroMaille == maille && e.Axe == Axe.X
                              && Math.Abs(e.Origine.Y - y) < 0.001 && Math.Abs(e.Origine.Z - z) < 0.001);

    [Theory]
    [InlineData(TypePlancher.AccesSeul)]
    [InlineData(TypePlancher.AccesSeulDiagoH)]
    public void Niveau_acces_seul_lisse_des_deux_cotes_sur_chaque_maille(TypePlancher type)
    {
        var projet = Standard();
        projet.Niveaux.Single(n => Math.Abs(n.Hauteur - 4) < 0.001).Type = type;
        var s = GenerateurFacade.Generer(projet);

        for (int m = 1; m <= projet.Mailles.Count; m++)
        {
            Assert.Equal(1, LissesA(s, m, 0, 4));      // côté façade
            Assert.Equal(1, LissesA(s, m, -0.70, 4));  // côté extérieur
        }
    }

    [Fact]
    public void Plateaux_au_niveau_0_avec_lisses_des_deux_cotes()
    {
        var s = GenerateurFacade.Generer(Standard());
        var maillesAuSol = s.Elements.Where(e => (e.Famille == Famille.Plateau || e.Famille == Famille.Trappe) && Math.Abs(e.Origine.Z) < 0.001)
                                     .Select(e => e.NumeroMaille).Distinct().ToList();
        Assert.NotEmpty(maillesAuSol); // plateaux de départ au pied de l'échelle
        foreach (var m in maillesAuSol)
        {
            Assert.Equal(1, LissesA(s, m, 0, 0));
            Assert.Equal(1, LissesA(s, m, -0.70, 0));
        }
    }

    [Fact]
    public void Lisses_de_pied_sans_doublon_si_diagonale_et_plateaux_au_sol()
    {
        var s = GenerateurFacade.Generer(Standard(p => p.LissesEnPied = true));
        foreach (var m in s.Elements.Select(e => e.NumeroMaille).Where(m => m > 0).Distinct())
        {
            Assert.Equal(1, LissesA(s, m, 0, 0));
            Assert.Equal(1, LissesA(s, m, -0.70, 0));
        }
    }

    [Fact]
    public void Poteaux_interieurs_sans_protection_s_arretent_au_plancher_sauf_extremites()
    {
        var s = GenerateurFacade.Generer(Standard());
        var tetes = s.Elements.Where(e => e.Famille == Famille.Poteau && e.Face == Face.Interieure)
                              .GroupBy(e => e.Origine.X)
                              .ToDictionary(g => g.Key, g => g.Max(e => e.Origine.Z));
        Assert.Equal(10.046, tetes[0], 3);      // extrémité : poteau de tête 1 m posé à 10,046
        Assert.Equal(8.046, tetes[2.5], 3);     // courant : dernier poteau 2 m posé à 8,046 (sommet 10)
    }

    [Fact]
    public void Console_par_maille_sans_doublon_entre_mailles_voisines()
    {
        var projet = Standard(p => p.SecuriteInterieure = TypeSecurite.Lisses);
        projet.Mailles[1].Console = "HMCON700E";
        projet.Mailles[2].Console = "HMCON700E";
        var s = GenerateurFacade.Generer(projet);

        // 3 files x 5 niveaux complets
        Assert.Equal(15, Nb(s, "HMCON700E"));
        // Pas de lisses de GC intérieur dans les mailles à console (remplacées par celles en bout de console)
        Assert.DoesNotContain(s.Elements, e => e.NumeroMaille == 2 && e.Famille == Famille.Lisse && e.Origine.Y == 0 && e.Origine.Z > 10.4);
        Assert.Contains(s.Elements, e => e.Reference == "HMPOT10" && e.Origine.Y > 0.69);
    }

    [Fact]
    public void Console_inconnue_signalee()
    {
        var projet = Standard();
        projet.Mailles[0].Console = "HMCON999";
        var s = GenerateurFacade.Generer(projet);
        Assert.Contains(s.Alertes, a => a.Contains("HMCON999"));
    }

    [Fact]
    public void Amarrages_places_sur_le_dessin()
    {
        var s = GenerateurFacade.Generer(Standard());
        var am = s.Elements.Where(e => e.Famille == Famille.Amarrage).ToList();
        Assert.Equal(s.Amarrages.Amarrages.Count, am.Count);
        Assert.All(am, e => Assert.Equal("HMTUBAMA", e.Reference)); // 0,70 + 0,20 + 0,10 → tube 1,20 m
    }

    [Fact]
    public void Diagonales_interieures_avec_garde_corps_interieur()
    {
        var s = GenerateurFacade.Generer(Standard(p => p.SecuriteInterieure = TypeSecurite.GardeCorpsMds1));
        var dia = s.Elements.Where(e => e.Famille == Famille.DiagonaleVerticale).ToList();
        Assert.Equal(dia.Count(e => e.Face == Face.Exterieure), dia.Count(e => e.Face == Face.Interieure));
    }

    [Fact]
    public void References_generees_presentes_dans_la_bibliotheque()
    {
        var biblio = BibliothequeBlocs.DepuisFichier("blocs-EM-3D.txt");
        Assert.True(biblio.Nombre > 500);

        foreach (var modele in Enum.GetValues<ModeleEchafaudage>())
        {
            var s = GenerateurFacade.Generer(Standard(p => { p.Modele = modele; p.SecuriteInterieure = TypeSecurite.GardeCorpsMds1; }));
            var manquantes = biblio.Manquantes(s.Elements.Select(e => e.Reference))
                                   .Where(r => !r.StartsWith("HMTUBAMA", StringComparison.Ordinal)) // bloc provisoire (décision 23/11/2026)
                                   .ToList();
            Assert.True(manquantes.Count == 0, $"{modele} : blocs manquants {string.Join(", ", manquantes)}");
        }
    }

    [Fact]
    public void Decompte_regroupe_par_reference()
    {
        var s = GenerateurFacade.Generer(Standard());
        var d = CalculDecompte.Calculer(s.Elements);
        Assert.Equal(s.Elements.Count, d.Sum(l => l.Quantite));
        Assert.Equal(d.Count, d.Select(l => l.Reference).Distinct().Count());
    }

    [Fact]
    public void Enregistrer_puis_ouvrir_donne_le_meme_projet()
    {
        var projet = Standard(p => p.Bache = true);
        projet.Chantier = "Essai";
        projet.Mailles[0].Console = "HMCON700E";

        var relu = FichierProjet.DepuisJson(FichierProjet.VersJson(projet));

        Assert.Equal("Essai", relu.Chantier);
        Assert.True(relu.Parametres.Bache);
        Assert.Equal(projet.Mailles.Count, relu.Mailles.Count);
        Assert.Equal("HMCON700E", relu.Mailles[0].Console);
        Assert.Equal(projet.Niveaux.Select(n => n.Hauteur), relu.Niveaux.Select(n => n.Hauteur));
        Assert.Equal(GenerateurFacade.Generer(projet).Elements.Count, GenerateurFacade.Generer(relu).Elements.Count);
    }
}
