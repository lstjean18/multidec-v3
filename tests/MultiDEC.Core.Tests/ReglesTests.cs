using MultiDEC.Core.Catalogue;
using MultiDEC.Core.Facade;
using MultiDEC.Core.Facade.Regles;
using Xunit;

namespace MultiDEC.Core.Tests;

public class ReglesTests
{
    private static FaceEchafaudage Projet(int nbMailles, double longueur = 2.5, Action<ParametresFacade>? config = null)
    {
        var p = new ParametresFacade { HauteurDernierPlancher = 10 };
        config?.Invoke(p);
        var projet = new FaceEchafaudage
        {
            Parametres = p,
            Mailles = Enumerable.Range(0, nbMailles).Select(_ => new MailleDefinition(longueur)).ToList(),
            Niveaux = ProposeurStructure.ProposerNiveaux(p),
        };
        RegleDiagonales.Proposer(p, projet.Mailles);
        return projet;
    }

    [Theory]
    [InlineData(TypeSecurite.GardeCorpsMds2, false, 5)]
    [InlineData(TypeSecurite.Lisses, false, 5)]
    [InlineData(TypeSecurite.GardeCorpsMds2, true, 2)]
    [InlineData(TypeSecurite.Lisses, true, 2)]
    public void Pas_des_diagonales(TypeSecurite secu, bool renforce, int pas)
    {
        var p = new ParametresFacade { SecuriteExterieure = secu, DiagonalesRenforcees = renforce };
        Assert.Equal(pas, RegleDiagonales.Pas(p));
    }

    [Fact]
    public void Diagonales_une_maille_sur_cinq_en_alternance()
    {
        var d = RegleDiagonales.Calculer(Projet(12));
        Assert.Equal(new[] { 1, 6, 11 }, d.Select(x => x.NumeroMaille));
        Assert.Equal(new[] { false, true, false }, d.Select(x => x.Inverse));
        Assert.All(d, x => Assert.False(x.Interieure));
    }

    [Fact]
    public void Diagonales_interieures_si_protection_interieure()
    {
        var d = RegleDiagonales.Calculer(Projet(6, config: p => p.SecuriteInterieure = TypeSecurite.GardeCorpsMds2));
        Assert.All(d, x => Assert.True(x.Interieure));
    }

    [Fact]
    public void Petite_facade_diagonale_premiere_maille_sur_option()
    {
        Assert.Single(RegleDiagonales.Calculer(Projet(3, config: p => p.DiagonalePremiereMaille = true)));
        Assert.Empty(RegleDiagonales.Calculer(Projet(3, config: p => p.DiagonalePremiereMaille = false)));
    }

    [Fact]
    public void Amarrages_non_bache_extremites_4m_courantes_8m_quinconce()
    {
        var projet = Projet(4, config: p => p.HauteurDernierPlancher = 18);
        var a = RegleAmarrages.Calculer(projet).Amarrages;

        double[] Hauteurs(int file) => a.Where(x => x.File == file).Select(x => x.Hauteur).ToArray();

        Assert.Equal(new[] { 2.0, 6, 10, 14, 18 }, Hauteurs(0));
        Assert.Equal(new[] { 2.0, 6, 14 }, Hauteurs(1));   // quinconce : 2e amarrage décalé d'un demi-pas
        Assert.Equal(new[] { 2.0, 10, 18 }, Hauteurs(2));
        Assert.Equal(new[] { 2.0, 6, 14 }, Hauteurs(3));
        Assert.Equal(new[] { 2.0, 6, 10, 14, 18 }, Hauteurs(4));
    }

    [Fact]
    public void Amarrages_bache_4m_partout()
    {
        var projet = Projet(2, config: p => { p.HauteurDernierPlancher = 10; p.Bache = true; });
        var a = RegleAmarrages.Calculer(projet).Amarrages;
        Assert.Equal(new[] { 2.0, 6, 10 }, a.Where(x => x.File == 0).Select(x => x.Hauteur));
        Assert.Equal(new[] { 2.0, 4, 8 }, a.Where(x => x.File == 1).Select(x => x.Hauteur));
    }

    [Fact]
    public void Controle_surface_et_note_de_calcul()
    {
        var projet = Projet(8, config: p => p.HauteurDernierPlancher = 24);
        var bilan = RegleAmarrages.Calculer(projet);
        Assert.Equal(20 * 25, bilan.Surface, 3);
        Assert.Equal(21, bilan.MinimumParSurface); // 500 / 24 = 20,8
        Assert.True(bilan.NoteDeCalculObligatoire);  // 25 m > 24 m
    }

    [Fact]
    public void Poteau_multi_system_10m_avec_garde_corps()
    {
        var p = new ParametresFacade { Modele = ModeleEchafaudage.MultiSystem, HauteurDernierPlancher = 10 };
        var t = ReglePoteaux.Empiler(p, depart3m: false, gardeCorps: true);
        Assert.Equal(new[] { "HMCALEBOIS", "HMSOCREG", "HMEMB", "HMPOT20", "HMPOT20", "OECGOUP8S", "HMPOT20", "OECGOUP8S", "HMPOT20", "OECGOUP8S", "HMPOT20", "OECGOUP8S", "HMPOT10", "OECGOUP8S" },
            t.Select(x => x.Reference));
        Assert.Equal(10.046, t.Last().Z, 3);
    }

    [Fact]
    public void Poteau_multi_reto_depart_3m_et_couvreur()
    {
        var p = new ParametresFacade { Modele = ModeleEchafaudage.MultiReto, HauteurDernierPlancher = 10, GardeCorpsCouvreur = true };
        var t = ReglePoteaux.Empiler(p, depart3m: true, gardeCorps: true).Where(x => x.Famille == Famille.Poteau);
        // 11,50 m = 3,00 + 4 x 2,00 + 0,50
        Assert.Equal(new[] { "HMPOTDP30", "HMPOTST20", "HMPOTST20", "HMPOTST20", "HMPOTST20", "HMPOTST05" }, t.Select(x => x.Reference));
    }

    [Fact]
    public void Poteau_interieur_sans_garde_corps_s_arrete_au_plancher()
    {
        var p = new ParametresFacade { Modele = ModeleEchafaudage.MultiReto, HauteurDernierPlancher = 9 };
        var t = ReglePoteaux.Empiler(p, depart3m: false, gardeCorps: false).Where(x => x.Famille == Famille.Poteau);
        Assert.Equal(new[] { "HMPOTDP20", "HMPOTST20", "HMPOTST20", "HMPOTST20", "HMPOTST10" }, t.Select(x => x.Reference));
    }

    [Fact]
    public void Poteau_multi_reto_embase()
    {
        var p = new ParametresFacade { Modele = ModeleEchafaudage.MultiRetoEmbase, HauteurDernierPlancher = 4 };
        var t = ReglePoteaux.Empiler(p, depart3m: true, gardeCorps: true).Where(x => x.Famille != Famille.Calage && x.Famille != Famille.Socle);
        // départ 3 m sans effet en MRE ; 1,00 + 2,00 + 2,00 = 5,00
        Assert.Equal(new[] { "HMEMB", "HMPOTDP10E", "HMPOTST20", "HMPOTST20" }, t.Select(x => x.Reference));
    }

    [Fact]
    public void Socle_a_roue_sans_cale_et_verin_avec_cale()
    {
        var roue = ReglePoteaux.Empiler(new ParametresFacade { Socle = "HMSOCROU" }, false, true);
        Assert.DoesNotContain(roue, x => x.Famille == Famille.Calage);
        Assert.Equal("HMSOCROU", roue.Single(x => x.Famille == Famille.Socle).Reference);

        var verin = ReglePoteaux.Empiler(new ParametresFacade { Socle = "HMVERORI38780" }, false, true);
        Assert.Contains(verin, x => x.Famille == Famille.Calage);
        Assert.Equal("HMVERORI38780", verin.Single(x => x.Famille == Famille.Socle).Reference);
    }

    [Fact]
    public void Grutable_support_au_pied_et_vis_ecrou_a_la_place_des_goupilles()
    {
        var t = ReglePoteaux.Empiler(new ParametresFacade { Grutable = true }, false, true);
        Assert.Contains(t, x => x.Reference == "HMSUPSUNI");
        Assert.DoesNotContain(t, x => x.Reference == "OECGOUP8S");
        Assert.Equal(t.Count(x => x.Reference == "VSZ10070"), t.Count(x => x.Reference == "VEF10"));
        Assert.True(t.Count(x => x.Reference == "VSZ10070") > 0);
    }

    [Fact]
    public void Socles_du_catalogue_dans_la_bibliotheque()
    {
        var blocs = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "blocs-3D-echafaudage.txt"));
        Assert.All(CatalogueSocles.Tous, s => Assert.Contains(s.Reference, blocs));
    }

    [Theory]
    [InlineData(0.70, TypeTraverse.Renforce, "HMTRA070")]   // pas de R jusqu'à 0,75 m (v2)
    [InlineData(1.00, TypeTraverse.Renforce, "HMTRA100R")]
    [InlineData(1.00, TypeTraverse.Poutre, "HMTRA100R")]
    [InlineData(1.00, TypeTraverse.Lisse, "HMTRA100")]
    [InlineData(2.50, TypeTraverse.Renforce, "HMLISR250")]
    [InlineData(2.50, TypeTraverse.Poutrelle, "HMPTRL250")]
    [InlineData(1.50, TypeTraverse.Poutrelle, "HMLISR150")]
    [InlineData(3.00, TypeTraverse.Poutre, "HMPOUT300")]
    [InlineData(2.00, TypeTraverse.Lisse, "HMLIS200")]
    public void Reference_des_traverses_comme_la_v2(double l, TypeTraverse type, string attendu) =>
        Assert.Equal(attendu, References.LisseOuTraverse(l, type));
}
