using MultiDEC.Core.Catalogue;
using MultiDEC.Core.Facade;
using MultiDEC.Core.Geometrie;
using MultiDEC.Core.Persistance;
using MultiDEC.Core.TourEscalier;
using Xunit;

namespace MultiDEC.Core.Tests;

/// <summary>Tour escalier à volées alu (port de clTESCALU, version2024) et projet multi-ouvrages (Laurent, 28/11/2026).</summary>
public class TourEscalierTests
{
    private static readonly ParametresGeneraux General = new ParametresGeneraux();

    private static TourEscalierAlu Tour(double hauteur, double largeur = 1.50, ConfigurationTour config = ConfigurationTour.Niveaux2m)
    {
        var t = new TourEscalierAlu { Hauteur = hauteur, Largeur = largeur, Configuration = config };
        t.SortieParDefaut();
        return t;
    }

    private static List<ElementPlace> Elements(TourEscalierAlu t, ParametresGeneraux? g = null) =>
        GenerateurTourEscalier.Generer(t, g ?? General).Elements.ToList();

    [Theory]
    [InlineData(6.0, ConfigurationTour.Niveaux2m, "0 2 4 6")]
    [InlineData(7.0, ConfigurationTour.Niveaux2m, "0 2 4 6 7")]
    [InlineData(6.5, ConfigurationTour.Niveaux2m, "0.5 2.5 4.5 6.5")]
    [InlineData(7.5, ConfigurationTour.Niveaux2m, "0.5 2.5 4.5 6.5 7.5")]
    [InlineData(7.0, ConfigurationTour.Alternes1m2m, "0 1 3 4 6 7")]
    [InlineData(6.0, ConfigurationTour.Alternes1m2m, "0 1 3 4 6")]
    [InlineData(3.5, ConfigurationTour.Alternes1m2m, "0.5 1.5 3.5")]
    public void Paliers_selon_la_configuration(double hauteur, ConfigurationTour config, string attendus)
    {
        var t = Tour(hauteur, config: config);
        Assert.Null(t.ErreurHauteur());
        Assert.Equal(attendus.Split(' ').Select(s => double.Parse(s, System.Globalization.CultureInfo.InvariantCulture)), t.Paliers());
    }

    [Theory]
    [InlineData(8.0, ConfigurationTour.Alternes1m2m)] // 8 = 3 × 2 + 2 (v2)
    [InlineData(5.0, ConfigurationTour.Alternes1m2m)]
    [InlineData(6.3, ConfigurationTour.Niveaux2m)]
    [InlineData(0.5, ConfigurationTour.Niveaux2m)]
    public void Hauteur_impossible_signalee_et_tour_non_dessinee(double hauteur, ConfigurationTour config)
    {
        var t = new TourEscalierAlu { Hauteur = hauteur, Configuration = config };
        Assert.NotNull(t.ErreurHauteur());
        var r = GenerateurTourEscalier.Generer(t, General);
        Assert.Empty(r.Elements);
        Assert.Contains(r.Alertes, a => a.Contains("non dessinée"));
    }

    public static IEnumerable<object[]> Configurations()
    {
        foreach (var modele in new[] { ModeleEchafaudage.MultiSystem, ModeleEchafaudage.MultiReto, ModeleEchafaudage.MultiRetoEmbase })
        {
            foreach (double largeur in TourEscalierAlu.Largeurs)
            {
                foreach (var (h, config) in new[] { (6.0, ConfigurationTour.Niveaux2m), (7.0, ConfigurationTour.Niveaux2m), (8.5, ConfigurationTour.Niveaux2m), (7.0, ConfigurationTour.Alternes1m2m), (9.5, ConfigurationTour.Alternes1m2m) })
                {
                    yield return new object[] { modele, largeur, h, config };
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Configurations))]
    public void References_de_la_tour_dans_la_bibliotheque(ModeleEchafaudage modele, double largeur, double hauteur, ConfigurationTour config)
    {
        var biblio = BibliothequeBlocs.DepuisFichier("blocs-3D-echafaudage.txt");
        var t = Tour(hauteur, largeur, config);
        var paliers = t.Paliers();
        // Une sortie par palier sur chaque grand côté (0,50 et 1,00 m) et au bout d'arrivée.
        for (int k = 1; k < paliers.Count; k++)
        {
            t.Sorties.Add(new SortieTour(paliers[k], k % 2 == 0 ? CoteTour.A : CoteTour.C, k % 3 == 0 ? 0.5 : 1.0));
            t.Sorties.Add(new SortieTour(paliers[k], t.BoutArrivee(k)));
        }

        var g = new ParametresGeneraux { Modele = modele };
        var r = GenerateurTourEscalier.Generer(t, g);
        var refs = r.Elements.Select(e => e.Reference).Where(x => !References.NonDessinee(x) && !x.StartsWith("HMTUBAMA", StringComparison.Ordinal)).ToList();
        Assert.True(biblio.Manquantes(refs).Count == 0, string.Join(", ", biblio.Manquantes(refs)));
        Assert.DoesNotContain(r.Alertes, a => a.Contains("ignorée"));
    }

    [Fact]
    public void Une_volee_par_palier_en_sens_alternes_la_derniere_cote_A()
    {
        var t = Tour(8.0);
        var volees = Elements(t).Where(e => e.Reference.StartsWith("HMESCALU", StringComparison.Ordinal)).OrderBy(e => e.Origine.Z).ToList();
        Assert.Equal(4, volees.Count);
        Assert.Equal(new[] { 2.0, 4.0, 6.0, 8.0 }, volees.Select(v => v.Origine.Z));
        Assert.All(volees, v => Assert.Equal("HMESCALU250200", v.Reference));

        // Entrée par le bout D : la première volée monte vers B (symétrisée), puis le sens alterne.
        Assert.Equal(new[] { true, false, true, false }, volees.Select(v => v.Inverse));

        // Dernière volée dans la moitié côté A (Y entre -0,75 et 0), l'avant-dernière côté C.
        Assert.InRange(volees[3].Origine.Y, -0.75, 0);
        Assert.InRange(volees[2].Origine.Y, -1.50, -0.75);
    }

    [Fact]
    public void Tour_de_2m_de_large_en_volees_larges()
    {
        var volees = Elements(Tour(6.0, 2.0)).Where(e => e.Famille == Famille.Escalier && e.Reference.StartsWith("HMESCALU", StringComparison.Ordinal)).ToList();
        Assert.All(volees, v => Assert.Equal("HMESCALU25020088D", v.Reference));
    }

    [Fact]
    public void Hauteur_impaire_derniere_volee_de_1m_en_haut()
    {
        // Laurent 28/11/2026 : la volée de 1 m en haut de la tour (inverse de la v2), palier en plateaux de 0,75 m qui
        // s'arrêtent à la traverse intermédiaire.
        var el = Elements(Tour(5.0));
        var v1 = Assert.Single(el, e => e.Reference == "HMESCALU175100");
        Assert.Equal(5.0, v1.Origine.Z);
        Assert.Contains(el, e => e.Reference == "HMTIP150" && Cotes.Egal(e.Origine.Z, 5.0));
        Assert.DoesNotContain(el, e => e.Reference == "HMTIP150" && Cotes.Egal(e.Origine.Z, 0));

        // Palier en 2 plateaux de 1,50 m en travers ; traverse du bout normale (rien ne s'y accroche).
        var plateaux = el.Where(e => e.Famille == Famille.Plateau && Cotes.Egal(e.Origine.Z, 5.0)).ToList();
        Assert.Equal(2, plateaux.Count);
        Assert.All(plateaux, p => Assert.True(p.Reference == "HMPLA1503CL6" && p.Axe == Axe.Y));
        var tip = el.Single(e => e.Reference == "HMTIP150" && Cotes.Egal(e.Origine.Z, 5.0));
        double xBout = tip.Origine.X < 1.25 ? 0 : 2.5;
        Assert.All(plateaux, p => Assert.True(p.Origine.X >= Math.Min(xBout, tip.Origine.X) && p.Origine.X <= Math.Max(xBout, tip.Origine.X) + Cotes.Tolerance));
        Assert.DoesNotContain(el, e => e.Axe == Axe.Y && Cotes.Egal(e.Origine.X, xBout) && Cotes.Egal(e.Origine.Z, 5.0) && e.Reference.Contains("R"));

        // Montant de garde-corps sur la traverse intermédiaire, relié au bord de la sortie par 2 tubes et 4 colliers.
        Assert.Single(el, e => e.Reference.StartsWith("HMMONTGC", StringComparison.Ordinal) && Cotes.Egal(e.Origine.X, tip.Origine.X) && Cotes.Egal(e.Origine.Z, 5.0));
        Assert.Equal(2, el.Count(e => e.Reference.StartsWith("HMTUB", StringComparison.Ordinal) && e.Famille == Famille.GardeCorps));
        Assert.Equal(4, el.Count(e => e.Reference == References.CollierFixe && e.Famille == Famille.GardeCorps));

        var biblio = BibliothequeBlocs.DepuisFichier("blocs-3D-echafaudage.txt");
        Assert.Empty(biblio.Manquantes(el.Select(e => e.Reference).Where(r => !r.StartsWith("HMTUBAMA", StringComparison.Ordinal) && !References.NonDessinee(r)).ToList()));
    }

    [Fact]
    public void Depart_de_50_cm_et_volee_de_1m_au_pied_plateaux_sans_couper_la_traverse_intermediaire()
    {
        var el = Elements(Tour(3.5, config: ConfigurationTour.Alternes1m2m));
        var tip = Assert.Single(el, e => e.Reference == "HMTIP150" && Cotes.Egal(e.Origine.Z, 0.5));
        foreach (var p in el.Where(e => e.Famille == Famille.Plateau && Cotes.Egal(e.Origine.Z, 0.5) && e.Axe == Axe.X))
        {
            double x0 = p.Origine.X, x1 = x0 + 1.75;
            Assert.True(p.Reference == "HMPLA1753CL6" && (Cotes.Egal(x0, tip.Origine.X) || Cotes.Egal(x1, tip.Origine.X)), $"{p.Reference} en X = {x0}, traverse en {tip.Origine.X}");
        }
    }

    [Fact]
    public void Paliers_alternes_volees_de_1m_avec_palier_en_plateaux()
    {
        var el = Elements(Tour(7.0, config: ConfigurationTour.Alternes1m2m));
        Assert.Equal(3, el.Count(e => e.Reference == "HMESCALU175100"));
        Assert.Equal(2, el.Count(e => e.Reference == "HMESCALU250200"));
        // Volées de 1 m à 4 et 7 m : traverse intermédiaire ; plateaux de 0,75 m au palier intermédiaire, de 1,50 m en
        // travers au dernier.
        foreach (double z in new[] { 4.0, 7.0 })
        {
            Assert.Contains(el, e => e.Reference == "HMTIP150" && Cotes.Egal(e.Origine.Z, z));
        }

        Assert.Contains(el, e => e.Reference == "HMPLA0753CL6" && Cotes.Egal(e.Origine.Z, 4.0));
        Assert.Contains(el, e => e.Reference == "HMPLA1503CL6" && Cotes.Egal(e.Origine.Z, 7.0));

        // Les volées de 1 m sont toutes dans la même moitié.
        Assert.Single(el.Where(e => e.Reference == "HMESCALU175100").Select(e => e.Origine.Y < -0.75).Distinct());
    }

    [Fact]
    public void Depart_de_50_cm_pour_une_hauteur_en_xx_50()
    {
        var t = Tour(6.5);
        Assert.Equal(0.5, t.HauteurDepart);
        var r = GenerateurTourEscalier.Generer(t, General);
        Assert.Contains(r.Elements, e => e.Reference == "HMPLA2503CL6" && Cotes.Egal(e.Origine.Z, 0.5));
        Assert.Contains(r.Alertes, a => a.Contains("Départ à 0,50 m"));
        Assert.Equal(new[] { 2.5, 4.5, 6.5 }, r.Elements.Where(e => e.Reference.StartsWith("HMESCALU", StringComparison.Ordinal)).Select(e => e.Origine.Z).OrderBy(z => z));
    }

    [Theory]
    [InlineData(CoteTour.D, 1.50)]
    [InlineData(CoteTour.B, 2.00)]
    public void Depart_de_50_cm_maille_d_entree_de_70_cm(CoteTour entree, double largeur)
    {
        var t = new TourEscalierAlu { Hauteur = 6.5, Largeur = largeur, Entree = entree };
        t.SortieParDefaut();
        var el = Elements(t);
        double x0 = entree == CoteTour.D ? -0.70 : 2.50, x1 = x0 + 0.70, xLoin = entree == CoteTour.D ? x0 : x1;

        // Deux plateaux au niveau 0, de la largeur de la tour, dans la maille d'entrée.
        var plateaux = el.Where(e => e.Famille == Famille.Plateau && Cotes.Egal(e.Origine.Z, 0)).ToList();
        Assert.Equal(2, plateaux.Count);
        Assert.All(plateaux, p => Assert.True(p.Origine.X > x0 && p.Origine.X <= x1 + Cotes.Tolerance && p.Axe == Axe.Y));

        // Poteaux de 1 m au bout extérieur, lisses à 0,50 et 1,00 m et plinthes de 0,70 m sur les grands côtés.
        Assert.Equal(2, el.Where(e => e.Famille == Famille.Poteau && Cotes.Egal(e.Origine.X, xLoin)).Select(e => e.Origine.Y).Distinct().Count());
        Assert.DoesNotContain(el, e => e.Famille == Famille.Poteau && Cotes.Egal(e.Origine.X, xLoin) && e.Origine.Z > 0.5);
        foreach (double z in new[] { 0.5, 1.0 })
        {
            Assert.Equal(2, el.Count(e => e.Famille == Famille.Lisse && e.Reference == "HMTRA070" && Cotes.Egal(e.Origine.X, x0) && Cotes.Egal(e.Origine.Z, z)));
        }

        Assert.Equal(2, el.Count(e => e.Famille == Famille.Plinthe && Cotes.Egal(e.Origine.X, x0) && Cotes.Egal(e.Origine.Z, 0)));
        Assert.DoesNotContain(el, e => e.Axe == Axe.Y && Cotes.Egal(e.Origine.X, xLoin) && e.Origine.Z > Cotes.Tolerance);

        // Plancher à 0,50 m seulement hors de la volée (pas de plateaux sous l'escalier).
        var volee = el.First(e => e.Famille == Famille.Escalier && e.Reference.StartsWith("HMESCALU", StringComparison.Ordinal) && Cotes.Egal(e.Origine.Z, 2.5));
        bool voleeCoteA = volee.Origine.Y > -largeur / 2;
        var plancher = el.Where(e => e.Famille == Famille.Plateau && Cotes.Egal(e.Origine.Z, 0.5)).ToList();
        Assert.NotEmpty(plancher);
        Assert.All(plancher, p => Assert.True(voleeCoteA ? p.Origine.Y < -largeur / 2 + 0.01 : p.Origine.Y > -largeur / 2 - 0.01, $"plateau à Y = {p.Origine.Y}"));

        // L'autre moitié du bout d'entrée fermée : montant de garde-corps et lisses courtes à +0,50 / +1,00 m, sans plinthe.
        double xEntree = entree == CoteTour.D ? 0 : 2.5;
        Assert.Single(el, e => e.Famille == Famille.GardeCorps && e.Reference.StartsWith("HMMONTGC", StringComparison.Ordinal) && Cotes.Egal(e.Origine.X, xEntree) && Cotes.Egal(e.Origine.Z, 0.5));
        foreach (double z in new[] { 1.0, 1.5 })
        {
            Assert.Single(el, e => e.Axe == Axe.Y && Cotes.Egal(e.Origine.X, xEntree) && Cotes.Egal(e.Origine.Z, z));
        }

        Assert.DoesNotContain(el, e => e.Famille == Famille.Plinthe && e.Axe == Axe.Y && Cotes.Egal(e.Origine.X, xEntree) && Cotes.Egal(e.Origine.Z, 0.5));

        // Marche à 0,25 m : 2 colliers rosace et 2 consoles HMCON300 sur les poteaux du bout d'entrée, un plateau dessus.
        Assert.Equal(2, el.Count(e => e.Reference == "HMCOL49ROSACE" && Cotes.Egal(e.Origine.X, xEntree) && Cotes.Egal(e.Origine.Z, 0.25)));
        Assert.Equal(2, el.Count(e => e.Reference == "HMCON300" && Cotes.Egal(e.Origine.X, xEntree) && Cotes.Egal(e.Origine.Z, 0.25)));
        var marche = Assert.Single(el, e => e.Famille == Famille.Plateau && Cotes.Egal(e.Origine.Z, 0.25));
        Assert.Equal(largeur == 2.0 ? "HMPLA2003CL6" : "HMPLA1503CL6", marche.Reference);
        Assert.True(entree == CoteTour.D ? marche.Origine.X <= 0 && marche.Origine.X > -0.31 : marche.Origine.X > 2.5 && marche.Origine.X <= 2.81, $"marche en X = {marche.Origine.X}");

        // Multi-RETO : poteaux de départ de 1,00 m (HMPOTDP10), pas de 2 m.
        var mr = GenerateurTourEscalier.Generer(t, new ParametresGeneraux { Modele = ModeleEchafaudage.MultiReto }).Elements;
        Assert.Equal(2, mr.Count(e => e.Reference == "HMPOTDP10" && Cotes.Egal(e.Origine.X, xLoin)));

        var biblio = BibliothequeBlocs.DepuisFichier("blocs-3D-echafaudage.txt");
        Assert.Empty(biblio.Manquantes(el.Select(e => e.Reference).Where(r => !r.StartsWith("HMTUBAMA", StringComparison.Ordinal) && !References.NonDessinee(r)).ToList()));
    }

    [Fact]
    public void Poteaux_jusqu_au_garde_corps_du_dernier_palier()
    {
        var el = Elements(Tour(6.0));
        foreach (var (x, y) in new[] { (0.0, 0.0), (2.5, 0.0), (0.0, -1.5), (2.5, -1.5) })
        {
            var pile = el.Where(e => e.Famille == Famille.Poteau && Cotes.Egal(e.Origine.X, x) && Cotes.Egal(e.Origine.Y, y)).ToList();
            Assert.NotEmpty(pile);
        }

        Assert.Contains(el, e => e.Famille == Famille.Lisse && Cotes.Egal(e.Origine.Z, 7.0)); // garde-corps à +1 m
        Assert.DoesNotContain(el, e => (e.Famille == Famille.Lisse || e.Famille == Famille.Traverse) && e.Origine.Z > 7.0 + Cotes.Tolerance);
    }

    [Fact]
    public void Entree_au_sol_sans_traverse_sous_2m()
    {
        var el = Elements(Tour(6.0)); // entrée côté D (X = 0)
        var bout = el.Where(e => e.Axe == Axe.Y && Cotes.Egal(e.Origine.X, 0) && (e.Famille == Famille.Traverse)).Select(e => e.Origine.Z).ToList();
        Assert.Contains(0.0, bout);
        Assert.DoesNotContain(bout, z => z > 0 && z < 2.0);
        Assert.Contains(2.0, bout);
        Assert.DoesNotContain(el, e => e.Famille == Famille.DiagonaleVerticale && e.Axe == Axe.Y && e.Origine.X < 0 && Cotes.Egal(e.Origine.Z, 0));
    }

    [Fact]
    public void Traverses_renforcees_au_bout_d_arrivee_des_volees()
    {
        var t = Tour(6.0);
        var el = Elements(t);
        for (int k = 1; k < t.Paliers().Count; k++)
        {
            double x = t.BoutArrivee(k) == CoteTour.B ? 2.5 : 0;
            Assert.Contains(el, e => e.Reference == "HMLISR150" && Cotes.Egal(e.Origine.X, x) && Cotes.Egal(e.Origine.Z, t.Paliers()[k]));
        }
    }

    [Fact]
    public void Sortie_sur_un_grand_cote_lisses_raccourcies_et_montant()
    {
        var t = Tour(6.0);
        t.Sorties.Clear();
        t.Sorties.Add(new SortieTour(4.0, CoteTour.A, 0.5));
        var el = Elements(t);
        // Palier de 4 m au bout D (deuxième volée) : ouverture de 0,50 m à X = 0, lisse de 2,00 m de X = 0,50 à 2,50.
        Assert.Equal(CoteTour.D, t.BoutArrivee(2));
        foreach (double z in new[] { 4.5, 5.0, 5.5 })
        {
            Assert.Contains(el, e => e.Reference == "HMLIS200" && Cotes.Egal(e.Origine.X, 0.5) && Cotes.Egal(e.Origine.Y, 0) && Cotes.Egal(e.Origine.Z, z));
            Assert.DoesNotContain(el, e => e.Reference == "HMLIS250" && Cotes.Egal(e.Origine.Y, 0) && Cotes.Egal(e.Origine.Z, z));
        }

        Assert.Contains(el, e => e.Reference == "HMMONTGCE" && Cotes.Egal(e.Origine.X, 0.5) && Cotes.Egal(e.Origine.Z, 4.0));
        Assert.Contains(el, e => e.Reference == "HMPOT05" && Cotes.Egal(e.Origine.X, 0.5) && Cotes.Egal(e.Origine.Z, 5.05));
        // Plus de diagonale côté A dans la tranche 4-6 m, mais toujours côté C.
        Assert.DoesNotContain(el, e => e.Famille == Famille.DiagonaleVerticale && e.Axe == Axe.X && e.Origine.Y > 0 && Cotes.Egal(e.Origine.Z, 4.0));
        Assert.Contains(el, e => e.Famille == Famille.DiagonaleVerticale && e.Axe == Axe.X && e.Origine.Y < -1.5 && Cotes.Egal(e.Origine.Z, 4.0));
    }

    [Fact]
    public void Sortie_sur_le_mauvais_bout_ignoree()
    {
        var t = Tour(6.0);
        t.Sorties.Clear();
        t.Sorties.Add(new SortieTour(6.0, t.BoutArrivee(3) == CoteTour.B ? CoteTour.D : CoteTour.B));
        t.Sorties.Add(new SortieTour(5.0, CoteTour.A));
        var r = GenerateurTourEscalier.Generer(t, General);
        Assert.Equal(2, r.Alertes.Count(a => a.Contains("ignorée")));
    }

    [Fact]
    public void Sortie_par_defaut_au_bout_d_arrivee_du_dernier_palier()
    {
        var t = Tour(6.0);
        var s = Assert.Single(t.Sorties);
        Assert.Equal(6.0, s.Hauteur);
        Assert.Equal(t.BoutArrivee(3), s.Cote);
        var el = Elements(t);
        double x = s.Cote == CoteTour.B ? 2.5 : 0;
        // Bout de 1,50 m : ouverture de 0,80 m devant la moitié côté A, traverse de 0,70 m.
        Assert.Contains(el, e => e.Reference == "HMTRA070" && Cotes.Egal(e.Origine.X, x) && Cotes.Egal(e.Origine.Y, -0.80) && Cotes.Egal(e.Origine.Z, 6.5));
    }

    [Fact]
    public void Tour_seule_amarree_par_le_cote_A_tous_les_4m()
    {
        var r = GenerateurTourEscalier.Generer(Tour(10.0), General);
        Assert.Equal(new[] { 2.0, 2.0, 6.0, 6.0, 10.0, 10.0 }, r.Amarrages.Select(a => a.Hauteur).OrderBy(z => z));
        Assert.All(r.Amarrages, a => Assert.Equal("T1", a.NomFace));

        var lestee = Tour(10.0);
        lestee.Stabilite = StabiliteTour.Lestee;
        var rl = GenerateurTourEscalier.Generer(lestee, General);
        Assert.Empty(rl.Amarrages);
        Assert.Contains(rl.Alertes, a => a.Contains("lest"));
    }

    [Fact]
    public void Garde_corps_de_volee_et_extremites()
    {
        var el = Elements(Tour(6.0));
        Assert.Equal(6, el.Count(e => e.Reference == "HMGCESCALU200"));
        Assert.Equal(3, el.Count(e => e.Reference == References.GardeCorpsVoleeExtremite)); // 2 au pied, 1 en haut
    }

    // ---- Projet multi-ouvrages ----------------------------------------------------------

    private static ProjetStructure ProjetFacade(double hauteur = 6.0)
    {
        return ProjetStructure.Nouveau(face: new ParametresFacade { LongueurTotaleSouhaitee = 10, HauteurDernierPlancher = hauteur });
    }

    [Fact]
    public void Tour_seule_sans_facade()
    {
        var projet = ProjetFacade();
        projet.AvecFacade = false;
        var tour = projet.AjouterTour();
        tour.PositionX = 3;
        tour.PositionY = 2;
        var s = GenerateurStructure.Generer(projet);
        Assert.All(s.Elements, e => Assert.Equal("T1", e.NomFace));
        Assert.All(s.Elements.Where(e => e.Famille == Famille.Poteau), e => Assert.InRange(e.Origine.X, 3 - 1e-6, 5.5 + 1e-6));
        Assert.All(s.Elements.Where(e => e.Famille == Famille.Poteau), e => Assert.InRange(e.Origine.Y, 0.5 - 1e-6, 2 + 1e-6));
    }

    [Fact]
    public void Projet_sans_ouvrage_refuse()
    {
        var projet = ProjetFacade();
        projet.AvecFacade = false;
        Assert.Throws<InvalidOperationException>(() => GenerateurStructure.Generer(projet));
    }

    [Fact]
    public void Tours_numerotees_et_placees_a_gauche_de_la_facade()
    {
        var projet = ProjetFacade();
        var t1 = projet.AjouterTour();
        var t2 = projet.AjouterTour();
        Assert.Equal("T1", t1.Nom);
        Assert.Equal("T2", t2.Nom);
        Assert.True(t1.PositionX + TourEscalierAlu.Longueur < 0);
        Assert.True(t2.PositionX + TourEscalierAlu.Longueur < t1.PositionX);
        Assert.Equal(6.0, t1.Hauteur); // hauteur de la façade
        projet.Tours.Remove(t1);
        Assert.Equal("T1", projet.AjouterTour().Nom);
    }

    [Fact]
    public void Tour_accolee_cote_exterieur_de_la_face_avec_liaisons()
    {
        var projet = ProjetFacade();
        var tour = projet.AjouterTour();
        tour.FaceAccolee = "S1";
        tour.Abscisse = 2.5;
        tour.Ecart = 0.10;
        var s = GenerateurStructure.Generer(projet);
        double l = projet.S1.Parametres.LargeurMaille;
        var poteaux = s.Elements.Where(e => e.NomFace == "T1" && e.Famille == Famille.Poteau).ToList();
        Assert.Contains(poteaux, e => Cotes.Egal(e.Origine.X, 2.5) && Cotes.Egal(e.Origine.Y, -l - 0.10));
        Assert.Contains(poteaux, e => Cotes.Egal(e.Origine.X, 5.0) && Cotes.Egal(e.Origine.Y, -l - 0.10 - 1.5));
        Assert.DoesNotContain(s.Elements, e => e.NomFace == "T1" && e.Reference == "EZPITON12"); // pas d'amarrage au bâtiment
        Assert.Contains(s.Elements, e => e.NomFace == "T1" && e.Famille == Famille.Amarrage && e.Reference.StartsWith("HMTUB", StringComparison.Ordinal));
    }

    [Fact]
    public void Tour_accolee_le_garde_corps_de_la_facade_s_ouvre_devant_la_sortie()
    {
        var projet = ProjetFacade();
        var sans = GenerateurStructure.Generer(projet);
        var tour = projet.AjouterTour();
        tour.FaceAccolee = "S1";
        tour.Abscisse = 0;
        tour.Sorties.Clear();
        tour.Sorties.Add(new SortieTour(4.0, CoteTour.A, 1.0));
        var avec = GenerateurStructure.Generer(projet);

        double l = projet.S1.Parametres.LargeurMaille;
        bool GcExterieur(ElementPlace e, double z) => e.NomFace == "S1" && e.Famille == Famille.GardeCorps && Cotes.Egal(e.Origine.Y, -l) && Cotes.Egal(e.Origine.Z, z) && e.NumeroMaille == 1;
        Assert.Contains(sans.Elements, e => GcExterieur(e, 4.0));
        Assert.DoesNotContain(avec.Elements, e => GcExterieur(e, 4.0));
        Assert.Contains(avec.Elements, e => GcExterieur(e, 2.0));
    }

    [Fact]
    public void Paliers_communs_avec_la_facade()
    {
        var projet = ProjetFacade(6.0);
        var tour = projet.AjouterTour();
        tour.FaceAccolee = "S1";
        Assert.Equal(new[] { 2.0, 4.0, 6.0 }, ToursDansLaStructure.PaliersCommuns(projet, tour));
    }

    [Fact]
    public void Enregistrement_des_tours()
    {
        var projet = ProjetFacade();
        projet.AvecFacade = false;
        var tour = projet.AjouterTour();
        tour.Largeur = 2.0;
        tour.Configuration = ConfigurationTour.Alternes1m2m;
        tour.Hauteur = 7;
        tour.Sorties.Add(new SortieTour(4.0, CoteTour.C, 0.5));
        tour.FaceAccolee = "S1";

        var relu = FichierProjet.DepuisJson(FichierProjet.VersJson(projet));
        Assert.False(relu.AvecFacade);
        var t = Assert.Single(relu.Tours);
        Assert.Equal("T1", t.Nom);
        Assert.Equal(2.0, t.Largeur);
        Assert.Equal(ConfigurationTour.Alternes1m2m, t.Configuration);
        Assert.Equal("S1", t.FaceAccolee);
        Assert.Contains(t.Sorties, s => s.Cote == CoteTour.C && Cotes.Egal(s.Largeur, 0.5));
        Assert.Contains("\"version\": 4", FichierProjet.VersJson(projet));
    }

    [Fact]
    public void Ancien_projet_sans_tour_toujours_lisible()
    {
        string json = FichierProjet.VersJson(ProjetFacade()).Replace("\"version\": 4", "\"version\": 3");
        var relu = FichierProjet.DepuisJson(json);
        Assert.True(relu.AvecFacade);
        Assert.Empty(relu.Tours);
    }

    [Fact]
    public void Diagonales_dans_l_emprise_de_la_tour()
    {
        // Laurent, 28/11/2026 : des diagonales partaient dans le vide (sens inversé posé depuis X = 0).
        var t = new TourEscalierAlu { Hauteur = 8 };
        var dias = GenerateurTourEscalier.Generer(t, General).Elements.Where(e => e.Famille == Famille.DiagonaleVerticale).ToList();
        Assert.NotEmpty(dias);
        foreach (var d in dias.Where(d => d.Axe == Axe.X))
        {
            Assert.Equal(d.Inverse ? TourEscalierAlu.Longueur : 0, d.Origine.X, 3);
        }

        foreach (var d in dias.Where(d => d.Axe == Axe.Y))
        {
            Assert.Equal(d.Inverse ? -t.Largeur : 0, d.Origine.Y, 3);
            Assert.Equal(d.Inverse ? 180 : 0, d.Rotation, 3);
        }

        // Zigzag : les deux sens existent sur les grands côtés et sur les bouts.
        Assert.Contains(dias, d => d.Axe == Axe.X && d.Inverse);
        Assert.Contains(dias, d => d.Axe == Axe.X && !d.Inverse);
        Assert.Contains(dias, d => d.Axe == Axe.Y && d.Inverse);
    }

    [Fact]
    public void Pas_de_plinthe_sans_palier_au_dernier_niveau()
    {
        // Laurent, 28/11/2026 : au dernier palier, le bout de plinthe à côté de la sortie de bout ne sert à rien.
        foreach (double largeur in TourEscalierAlu.Largeurs)
        {
            var t = new TourEscalierAlu { Largeur = largeur, Hauteur = 6 };
            t.Sorties.Clear();
            t.SortieParDefaut();
            var bout = t.BoutArrivee(t.Paliers().Count - 1);
            double x = bout == CoteTour.B ? TourEscalierAlu.Longueur : 0;
            var el = Elements(t);
            Assert.DoesNotContain(el, e => e.Famille == Famille.Plinthe && Cotes.Egal(e.Origine.Z, 6) && e.Axe == Axe.Y && Math.Abs(e.Origine.X - x) < 0.1);
            // Aux paliers intermédiaires, les plinthes des bouts restent.
            Assert.Contains(el, e => e.Famille == Famille.Plinthe && Cotes.Egal(e.Origine.Z, 4) && e.Axe == Axe.Y);
        }
    }

    [Fact]
    public void Plinthes_seulement_le_long_d_un_palier()
    {
        // Laurent, 28/11/2026 : une plinthe seulement le long d'un plateau ou d'un palier d'escalier.
        var t = new TourEscalierAlu { Hauteur = 8 };
        var el = Elements(t);
        var plinthes = el.Where(e => e.Famille == Famille.Plinthe).ToList();
        Assert.DoesNotContain(plinthes, e => e.Axe == Axe.X); // pas sur les grands côtés
        var paliers = t.Paliers();
        for (int k = 1; k < paliers.Count; k++)
        {
            double x = t.BoutArrivee(k) == CoteTour.B ? TourEscalierAlu.Longueur : 0;
            Assert.All(plinthes.Where(e => Cotes.Egal(e.Origine.Z, paliers[k])), e => Assert.True(Math.Abs(e.Origine.X - x) < 0.1));
        }

        // Départ de 0,50 m : plancher en plateaux, plinthes sur les grands côtés.
        var depart = new TourEscalierAlu { Hauteur = 6.5 };
        Assert.Contains(Elements(depart), e => e.Famille == Famille.Plinthe && e.Axe == Axe.X && Cotes.Egal(e.Origine.Z, 0.5));
    }

    // ---- Sorties acrotère (v2 « Ouvrir + Sortie Acrotère », Laurent 28/11/2026) ----

    private static TourEscalierAlu TourAcrotere(CoteTour cote, bool tournee, double largeur = 1.50, double pont = 1.50)
    {
        var t = new TourEscalierAlu { Hauteur = 6, Largeur = largeur };
        t.Sorties.Clear();
        var bout = t.BoutArrivee(t.Paliers().Count - 1);
        t.Sorties.Add(new SortieTour(6, cote == CoteTour.B || cote == CoteTour.D ? bout : cote) { Acrotere = true, LongueurPont = pont, Tournee = tournee });
        return t;
    }

    [Theory]
    [InlineData(CoteTour.A, false)]
    [InlineData(CoteTour.C, false)]
    [InlineData(CoteTour.B, false)]
    [InlineData(CoteTour.A, true)]
    [InlineData(CoteTour.C, true)]
    [InlineData(CoteTour.B, true)]
    public void Sortie_acrotere_hors_de_la_tour_et_au_toit(CoteTour cote, bool tournee)
    {
        foreach (double largeur in TourEscalierAlu.Largeurs)
        {
            var t = TourAcrotere(cote, tournee, largeur);
            var r = GenerateurTourEscalier.Generer(t, General);
            var acro = r.Elements.Where(e => e.CoteTour != null && e.Famille == Famille.Escalier && e.Reference.StartsWith("HMESCALU175", StringComparison.Ordinal)
                && Cotes.Egal(e.Origine.Z, 6) && (e.Origine.X < -0.01 || e.Origine.X > 2.51 || e.Origine.Y > 0.01 || e.Origine.Y < -largeur - 0.01)).ToList();
            Assert.Single(acro); // la volée de sortie, hors de l'emprise de la tour

            // Poteaux de la sortie posés sur le toit, 1,00 m sous le palier, hors de la tour du bon côté.
            var socles = r.Elements.Where(e => e.Famille == Famille.Socle && Cotes.Egal(e.Origine.Z, 5 + CatalogueSocles.De(new ParametresFacade().Socle).ZSocle)).ToList();
            Assert.Equal(tournee ? 5 : 4, socles.Count); // pont : 2 ; droite : 2 de plus ; tournée : 3 de plus (v2)
            Assert.All(socles, e => Assert.True(cote switch
            {
                CoteTour.A => e.Origine.Y > 0.5,
                CoteTour.C => e.Origine.Y < -largeur - 0.5,
                _ => e.Origine.X > 2.5 + 0.5 || e.Origine.X < -0.5,
            }, e.ToString()));
            Assert.Contains(r.Alertes, a => a.Contains("acrotère"));
        }
    }

    [Fact]
    public void Sortie_acrotere_ouverture_de_1m_et_pont()
    {
        var t = TourAcrotere(CoteTour.B, false, 1.50, 2.00);
        var el = Elements(t);
        var bout = t.Sorties[0].Cote;
        double x = bout == CoteTour.B ? TourEscalierAlu.Longueur : 0;
        // Ouverture de 1,00 m sur le bout d'une tour de 1,50 m : il reste une traverse de 0,50 m (v2 HMTRA050).
        Assert.Contains(el, e => e.Reference == "HMTRA050" && Cotes.Egal(e.Origine.X, x) && Cotes.Egal(e.Origine.Z, 6.5));
        // Pont de 2,00 m : trois plateaux, deux plinthes, deux tubes de garde-corps par côté, tube de contreventement.
        Assert.Equal(3, el.Count(e => e.Famille == Famille.Plateau && e.Reference.Contains("200") && Cotes.Egal(e.Origine.Z, 6)));
        Assert.Equal(2, el.Count(e => e.Reference == "HMPLIM200AX"));
        Assert.Contains(el, e => e.Reference.StartsWith("HMTUB", StringComparison.Ordinal) && Cotes.Egal(e.Origine.Z, 5.9));
    }

    [Fact]
    public void Sortie_acrotere_tournee_vers_le_milieu_du_cote()
    {
        var t = TourAcrotere(CoteTour.A, true);
        var bout = t.BoutArrivee(t.Paliers().Count - 1);
        var el = Elements(t);
        var volee = Assert.Single(el, e => e.Reference.StartsWith("HMESCALU175", StringComparison.Ordinal) && e.Origine.Y > 0.5);
        // Ouverture au bout d'arrivée : la volée de sortie part vers le milieu du côté A.
        Assert.Equal(bout == CoteTour.D ? 0.0 : 180.0, ((volee.Rotation % 360) + 360) % 360, 3);
    }

    [Fact]
    public void Sortie_acrotere_references_au_catalogue()
    {
        var biblio = BibliothequeBlocs.DepuisFichier("blocs-3D-echafaudage.txt");
        foreach (var cote in new[] { CoteTour.A, CoteTour.B, CoteTour.C })
        {
            foreach (bool tournee in new[] { false, true })
            {
                foreach (double largeur in TourEscalierAlu.Largeurs)
                {
                    foreach (double pont in TourEscalierAlu.LongueursPont)
                    {
                        var t = TourAcrotere(cote, tournee, largeur, pont);
                        var manquantes = biblio.Manquantes(Elements(t).Select(e => e.Reference).Where(r => !r.StartsWith("HMTUBAMA", StringComparison.Ordinal) && !References.NonDessinee(r)).ToList());
                        Assert.True(manquantes.Count == 0, $"{cote} {(tournee ? "T" : "D")} {largeur} {pont} : {string.Join(", ", manquantes)}");
                    }
                }
            }
        }
    }

    // ---- Stabilisation sur deux côtés (Laurent, 28/11/2026, plan LV Tec O240610) ----

    [Theory]
    [InlineData(CoteTour.D, 1.50)]
    [InlineData(CoteTour.B, 1.50)]
    [InlineData(CoteTour.D, 2.00)]
    public void Stabilisation_cote_C_vers_le_bout_oppose_a_l_entree(CoteTour entree, double largeur)
    {
        var t = new TourEscalierAlu { Hauteur = 7, Largeur = largeur, Entree = entree, Stabilite = StabiliteTour.Stabilisee };
        t.SortieParDefaut();
        var r = GenerateurTourEscalier.Generer(t, General);
        Assert.Empty(r.Amarrages);
        Assert.Contains(r.Alertes, a => a.Contains("85 km/h"));

        // Emprise de 5,00 × (largeur + 2,50), vers le bout opposé à l'entrée.
        var socles = r.Elements.Where(e => e.Famille == Famille.Socle).Select(e => (e.Origine.X, e.Origine.Y)).Distinct().ToList();
        Assert.Equal(8, socles.Count); // pas de poteau au coin isolé du module d'angle (Laurent, 28/11/2026)
        Assert.DoesNotContain(socles, p => Cotes.Egal(p.Y, -largeur - 2.5) && Cotes.Egal(Math.Abs(p.X - 1.25), 3.75));
        Assert.Equal(-largeur - 2.5, socles.Min(p => p.Y), 3);
        Assert.Equal(entree == CoteTour.D ? 5.0 : 2.5, socles.Max(p => p.X), 3);
        Assert.Equal(entree == CoteTour.D ? 0.0 : -2.5, socles.Min(p => p.X), 3);

        // Poteaux de stabilisation jusqu'à 3,00 m ; diagonales horizontales 10 cm sous la dernière rosace.
        Assert.Equal(3, r.Elements.Count(e => e.Famille == Famille.DiagonaleHorizontale && e.Reference.StartsWith("HMTUB", StringComparison.Ordinal) && Cotes.Egal(e.Origine.Z, 2.9)));
        Assert.Equal(6, r.Elements.Count(e => e.Reference == References.CollierFixe && Cotes.Egal(e.Origine.Z, 2.9)));
        // Bracons : 4 diagonales du haut de la stabilisation (3 m) aux poteaux de la tour.
        Assert.Equal(4, r.Elements.Count(e => e.Famille == Famille.DiagonaleVerticale && Cotes.Egal(e.Origine.Z, 3.0)));

        var biblio = BibliothequeBlocs.DepuisFichier("blocs-3D-echafaudage.txt");
        var refs = r.Elements.Select(e => e.Reference).Where(x => !x.StartsWith("HMTUBAMA", StringComparison.Ordinal) && !References.NonDessinee(x)).ToList();
        Assert.True(biblio.Manquantes(refs).Count == 0, string.Join(", ", biblio.Manquantes(refs)));
    }

    [Fact]
    public void Stabilisation_module_d_angle_en_tubes_seuls()
    {
        var t = new TourEscalierAlu { Hauteur = 7, Entree = CoteTour.D, Stabilite = StabiliteTour.Stabilisee };
        var el = Elements(t);
        // Module d'angle (X 2,50 à 5,00 ; Y -1,50 à -4,00) : pas de lisse sur ses côtés extérieurs.
        Assert.DoesNotContain(el, e => e.Famille == Famille.Lisse && Cotes.Egal(e.Origine.Y, -4.0) && Cotes.Egal(e.Origine.X, 2.5));
        Assert.DoesNotContain(el, e => e.Famille == Famille.Traverse && Cotes.Egal(e.Origine.X, 5.0) && Cotes.Egal(e.Origine.Y, -1.5));
        // Module contre C et module de bout : lisses.
        Assert.Contains(el, e => e.Famille == Famille.Lisse && Cotes.Egal(e.Origine.Y, -4.0) && Cotes.Egal(e.Origine.X, 0));
        Assert.Contains(el, e => e.Famille == Famille.Traverse && Cotes.Egal(e.Origine.X, 5.0) && Cotes.Egal(e.Origine.Y, 0));
    }
}
