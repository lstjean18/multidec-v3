using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MultiDEC.Core.Catalogue;
using MultiDEC.Core.Decompte;
using MultiDEC.Core.Facade;
using MultiDEC.Core.Facade.Regles;
using MultiDEC.Core.Persistance;
using Xunit;

namespace MultiDEC.Core.Tests;

public class GenerateurFacadeTests
{
    private static FaceEchafaudage Standard(Action<ParametresFacade>? config = null)
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

    /// <summary>Console sur tous les niveaux complets, cochée sur les mailles données (index 0).</summary>
    private static void Consoles(FaceEchafaudage projet, string reference, params int[] mailles)
    {
        foreach (var n in projet.Niveaux.Where(n => n.Type == TypePlancher.Complet && n.Hauteur > 0))
        {
            n.Console = reference;
            foreach (int i in mailles)
            {
                projet.Mailles[i].DefinirConsole(n.Hauteur, true);
            }
        }
    }

    private static int Nb(StructureFacade s, string reference) => s.Elements.Count(e => e.Reference == reference);

    [Fact]
    public void Facade_standard_quantites_principales()
    {
        var s = GenerateurFacade.Generer(Standard());

        // 8 mailles → 9 files x 2 poteaux
        Assert.Equal(18, Nb(s, "HMSOCREG"));
        Assert.Equal(18, Nb(s, "HMEMB"));
        // Garde-corps MDS2 extérieur (par défaut) : 7 mailles de travail x 5 niveaux complets + accès sur 5 niveaux
        Assert.Equal(8 * 5, Nb(s, "HMGCSE250GT"));
        Assert.Equal(TypeSecurite.GardeCorpsMds2, new ParametresFacade().SecuriteAbouts);
        // Diagonales : mailles 1 et 6, 5 diagonales de 2 m chacune
        Assert.Equal(10, Nb(s, "HMDIA2502"));
        // Trappes aux niveaux 2 à 10 dans la maille d'accès
        Assert.Equal(5, Nb(s, "HMPLAL250602TREC"));
        Assert.DoesNotContain(s.Elements, e => e.Reference == References.Echelle); // maille > 2 m : pas d'échelle intégrée
    }

    [Fact]
    public void Diago_H_par_niveau_dans_les_mailles_de_travail_sens_alterne()
    {
        var projet = Standard();
        var n4 = projet.Niveaux.Single(n => Math.Abs(n.Hauteur - 4) < 0.001);
        n4.DiagoH = true;
        var s = GenerateurFacade.Generer(projet);

        var diagos = s.Elements.Where(e => e.Famille == Famille.DiagonaleHorizontale).OrderBy(e => e.NumeroMaille).ToList();
        var travail = projet.Mailles.Select((m, i) => (m, i)).Where(x => x.m.Type == TypeMaille.Travail).ToList();
        Assert.Equal(travail.Count, diagos.Count);
        Assert.All(diagos, d => Assert.Equal(4, d.Origine.Z, 3));
        Assert.Equal(diagos.Select((d, i) => i % 2 == 1), diagos.Select(d => d.Inverse));

        // Inversée : origine au bout droit de la maille.
        var inv = diagos.First(d => d.Inverse);
        var x = projet.AbscissesFiles();
        Assert.Equal(x[inv.NumeroMaille], inv.Origine.X, 3);
        Assert.Equal(TypePlancher.Complet, n4.Type); // possible aussi sur un niveau complet, comme la v2
    }

    [Fact]
    public void Trappes_acier_dans_les_mailles_d_acces_et_lisses_a_la_place_des_mds()
    {
        var face = Standard(p => p.TrappesAcier = true);
        var s = GenerateurFacade.Generer(face);
        int acces = face.Mailles.Count(m => m.Type == TypeMaille.Acces);
        int niveaux = face.Niveaux.Count(n => n.Hauteur >= 2);

        Assert.Equal(acces * niveaux, Nb(s, "HMPLAL100596TR"));
        Assert.Equal(acces * niveaux, Nb(s, "HMTIP070"));
        Assert.DoesNotContain(s.Elements, e => e.Reference.StartsWith("HMPLAL250", StringComparison.Ordinal));
        Assert.DoesNotContain(s.Elements, e => e.Axe == Axe.X && e.Reference.StartsWith("HMGCSE", StringComparison.Ordinal)); // abouts inchangés (v2)
        Assert.Contains(s.Alertes, a => a.Contains("MDS remplacés par des lisses"));
        Assert.Equal(TypeSecurite.GardeCorpsMds2, face.Parametres.SecuriteExterieure); // paramètre de l'utilisateur inchangé
    }

    [Fact]
    public void Lisse_gc_acces_a_1_50_au_niveau_0()
    {
        int Lisses150(FaceEchafaudage f) => GenerateurFacade.Generer(f).Elements
            .Count(e => e.Famille == Famille.Lisse && Math.Abs(e.Origine.Z - 1.5) < 0.001);
        Assert.Equal(0, Lisses150(Standard()));

        var face = Standard(p => p.LisseGcAccesNiveau0 = true);
        Assert.Equal(face.Mailles.Count(m => m.Type == TypeMaille.Acces), Lisses150(face)); // GC extérieur seulement
    }

    [Fact]
    public void Pare_gravois_seulement_sur_les_mailles_cochees()
    {
        // Laurent 24/11/2026 : colonne « PareGrav2m » dans la grille des mailles.
        var face = Standard();
        face.Niveaux.Single(n => Math.Abs(n.Hauteur - 2) < 0.001).PareGravois = true;
        face.Mailles[2].DefinirPareGravois(2, true);
        face.Mailles[3].DefinirPareGravois(2, true);
        face.Mailles[6].DefinirPareGravois(2, true);
        Assert.True(face.Mailles[2]["PG2"]);
        Assert.False(face.Mailles[2]["2"]); // la case console du même niveau reste distincte
        var s = GenerateurFacade.Generer(face);
        var x = face.AbscissesFiles();
        var pg = s.Elements.Where(e => e.Famille == Famille.PareGravois).ToList();

        // Deux tronçons : mailles 3-4 (3 files) et maille 7 (2 files).
        Assert.Equal(5, pg.Count(e => e.Reference == "HMPARGRA"));
        Assert.Equal(9, pg.Count(e => e.Axe == Axe.X && e.Reference.StartsWith("HMLIS", StringComparison.Ordinal)));
        var toles = pg.Where(e => e.Reference == "HMTOG200").ToList();
        Assert.All(toles, t => Assert.True((t.Origine.X >= x[2] - 1e-6 && t.Origine.X + 0.9 <= x[4] + 1e-6) || (t.Origine.X >= x[6] - 1e-6 && t.Origine.X + 0.9 <= x[7] + 1e-6)));
        Assert.Equal("PareGrav2m", face.Niveaux.Single(n => Math.Abs(n.Hauteur - 2) < 0.001).NomColonnePareGravois);
    }

    [Fact]
    public void Ancien_fichier_pare_gravois_coche_sur_toutes_les_mailles()
    {
        var projet = ProjetStructure.Nouveau();
        projet.S1.Niveaux.Single(n => Math.Abs(n.Hauteur - 4) < 0.001).PareGravois = true;
        var relu = FichierProjet.DepuisJson(FichierProjet.VersJson(projet));
        Assert.All(relu.S1.Mailles, m => Assert.True(m.PareGravoisA(4)));
    }

    [Fact]
    public void Pare_gravois_consoles_a_chaque_file_toles_et_lisses()
    {
        var face = Standard();
        face.Niveaux.Single(n => Math.Abs(n.Hauteur - 2) < 0.001).PareGravois = true;
        face.ConvertirPareGravoisAncienFormat(); // toutes les mailles cochées, comme à la création de la colonne
        var s = GenerateurFacade.Generer(face);
        var pg = s.Elements.Where(e => e.Famille == Famille.PareGravois).ToList();

        Assert.Equal(face.Mailles.Count + 1, pg.Count(e => e.Reference == "HMPARGRA"));
        Assert.Equal(3 * face.Mailles.Count, pg.Count(e => e.Axe == Axe.X && e.Reference.StartsWith("HMLIS", StringComparison.Ordinal)));
        int toles = (int)Math.Ceiling((face.LongueurObtenue - 0.9) / 0.8 - 1e-6) + 1;
        var tolesPlacees = pg.Where(e => e.Reference == "HMTOG200").ToList();
        Assert.Equal(toles, tolesPlacees.Count);
        Assert.Equal(0, tolesPlacees.Min(e => e.Origine.X), 6);
        Assert.Equal(face.LongueurObtenue - 0.9, tolesPlacees.Max(e => e.Origine.X), 6); // la dernière finit au bout
        Assert.Equal(3 * toles, pg.Count(e => e.Reference == "HMCROCHE"));
        Assert.All(pg.Where(e => e.Reference == "HMTOG200"), e => Assert.Equal(125, e.Inclinaison));
        Assert.All(pg, e => Assert.True(e.Origine.Y <= -0.70 + 1e-6)); // côté extérieur
    }

    [Fact]
    public void Portillon_d_about_et_echelle_quand_le_1er_plancher_est_sous_2_m()
    {
        var face = Standard(p => p.HauteurDernierPlancher = 9); // planchers 1, 3, 5, 7, 9
        var s = GenerateurFacade.Generer(face);

        var portillon = Assert.Single(s.Elements, e => e.Reference == "HMPORTILLONPLIN070");
        Assert.Equal(new MultiDEC.Core.Geometrie.Point3(0, 0, 2.0), portillon.Origine);
        Assert.Single(s.Elements, e => e.Reference == "HMECH100LIS");
        // L'about gauche du niveau 1,00 m est remplacé, celui de droite reste.
        Assert.DoesNotContain(s.Elements, e => e.Famille == Famille.GardeCorps && e.Axe == Axe.Y && e.Inverse && Math.Abs(e.Origine.Z - 1) < 0.001);
        Assert.Contains(s.Elements, e => e.Famille == Famille.GardeCorps && e.Axe == Axe.Y && !e.Inverse && Math.Abs(e.Origine.Z - 1) < 0.001);

        // Planchers à 2, 4… : pas de portillon.
        Assert.DoesNotContain(GenerateurFacade.Generer(Standard()).Elements, e => e.Reference.StartsWith("HMPORTILLON", StringComparison.Ordinal));
    }

    [Fact]
    public void Ecran_trappes_acier_coche_les_diagonales_1_sur_2()
    {
        var vm = new MultiDEC.App.ViewModels.FacadeViewModel();
        vm.TrappesAcier = true;
        Assert.True(vm.Parametres.DiagonalesRenforcees);
        Assert.True(vm.Mailles.Count(m => m.Diagonale) >= vm.Mailles.Count / 2);
    }

    [Fact]
    public void Ancien_type_acces_seul_diago_h_devient_la_case()
    {
        var n = new Niveau(4, TypePlancher.AccesSeulDiagoH);
        Assert.Equal(TypePlancher.AccesSeul, n.Type);
        Assert.True(n.DiagoH);
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
    public void Lisse_de_securite_des_acces_dans_le_plan_des_poteaux()
    {
        var s = GenerateurFacade.Generer(Standard(p => p.SecuriteInterieure = TypeSecurite.Lisses));
        var lisses = s.Elements.Where(e => e.Famille == Famille.Lisse && e.Axe == Axe.X).ToList();
        Assert.Contains(lisses, e => Math.Abs(e.Origine.Z % 2 - 1.5) < 0.001); // lisses à 1,50 m au-dessus des plateaux
        Assert.All(lisses, e => Assert.True(Math.Abs(e.Origine.Y) < 0.001 || Math.Abs(e.Origine.Y + 0.70) < 0.001, $"{e.Reference} à Y = {e.Origine.Y}"));
    }

    [Fact]
    public void Plateaux_standard_nf_par_defaut()
    {
        Assert.Equal(GammePlateaux.StandardNF, new ParametresFacade().Plateaux);
        Assert.Equal("Standard NF", LibellesPlateaux.Libelle(GammePlateaux.StandardNF));
        Assert.Equal("Anti Contamination NF", LibellesPlateaux.Libelle(GammePlateaux.AntiContaminationNF));
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
        Consoles(projet, "HMCON700E", 1, 2);
        var s = GenerateurFacade.Generer(projet);

        // 3 files x 5 niveaux complets
        Assert.Equal(15, Nb(s, "HMCON700E"));
        // Pas de lisses de GC intérieur dans les mailles à console (remplacées par celles en bout de console)
        Assert.DoesNotContain(s.Elements, e => e.NumeroMaille == 2 && e.Famille == Famille.Lisse && e.Origine.Y == 0 && e.Origine.Z > 10.4);
        Assert.Contains(s.Elements, e => e.Reference == "HMPOT10" && e.Origine.Y > 0.69);
    }

    [Theory]
    [InlineData(ModeleEchafaudage.MultiSystem, "HMCON700E", "HMPOT10", true, 0.04)]     // chape MS : embase + HMPOT10
    [InlineData(ModeleEchafaudage.MultiReto, "HMCON700E", "HMPOTDP10", false, 0.0)]     // chape MR : poteau de départ direct
    [InlineData(ModeleEchafaudage.MultiSystem, "HMCON700E2", "HMPOT10", false, 0.04)]   // nipple : embase intégrée
    [InlineData(ModeleEchafaudage.MultiReto, "HMCON750MR", "HMPOTST10", false, 0.0)]
    public void Console_bout_selon_la_fixation(ModeleEchafaudage modele, string console, string poteau, bool embase, double dz)
    {
        // Liste de Laurent du 24/11/2026 : platine / chape / nipple.
        var projet = Standard(p => { p.Modele = modele; p.SecuriteInterieure = TypeSecurite.Lisses; });
        Consoles(projet, console, 1);
        var s = GenerateurFacade.Generer(projet);
        double w = CatalogueConsoles.Liste.Single(c => c.Reference == console).Largeur;
        var bout = s.Elements.Where(e => e.NumeroMaille == 2 && Math.Abs(e.Origine.Y - w) < 1e-6 && e.Axe == Axe.Z).ToList();
        Assert.Contains(bout, e => e.Reference == poteau && Math.Abs(e.Origine.Z - 4 - dz) < 1e-6);
        Assert.Equal(embase, bout.Any(e => e.Reference == "HMEMB"));
        Assert.DoesNotContain(s.Alertes, a => a.Contains(console));
    }

    [Fact]
    public void Console_a_platine_sans_poteau_ni_lisse_en_bout()
    {
        var projet = Standard(p => p.SecuriteInterieure = TypeSecurite.Lisses);
        Consoles(projet, "HMCON300", 1);
        var s = GenerateurFacade.Generer(projet);
        Assert.Contains(s.Elements, e => e.Reference == "HMCON300");
        Assert.DoesNotContain(s.Elements, e => e.NumeroMaille == 2 && e.Origine.Y > 0.1 && (e.Famille == Famille.Poteau || e.Famille == Famille.Lisse));
        Assert.Contains(s.Alertes, a => a.Contains("HMCON300") && a.Contains("platine"));
    }

    [Fact]
    public void Console_chape_MR_sans_garde_corps_poteaux_en_bout_pour_les_abouts()
    {
        // Multi-RETO, chape, pas de garde-corps côté mur : pas de lisse le long, mais la console isolée a deux abouts,
        // chacun avec son poteau HMPOTDP10 en bout et sa plinthe (Laurent, 25/11/2026).
        var projet = Standard(p => { p.Modele = ModeleEchafaudage.MultiReto; p.SecuriteInterieure = TypeSecurite.Sans; });
        Consoles(projet, "HMCON700E", 1);
        var s = GenerateurFacade.Generer(projet);
        var x = projet.AbscissesFiles();
        int niveaux = projet.Niveaux.Count(n => n.Type == TypePlancher.Complet && n.Hauteur > 0);
        var bout = s.Elements.Where(e => e.NumeroMaille == 2 && e.Origine.Y > 0.1).ToList();

        Assert.DoesNotContain(bout, e => e.Famille == Famille.Lisse && e.Axe == Axe.X);
        var poteaux = bout.Where(e => e.Famille == Famille.Poteau).ToList();
        Assert.Equal(2 * niveaux, poteaux.Count);
        Assert.All(poteaux, e => { Assert.Equal("HMPOTDP10", e.Reference); Assert.Equal(0.70, e.Origine.Y, 6); });
        Assert.Contains(poteaux, e => Math.Abs(e.Origine.X - x[1]) < 1e-6);
        Assert.Contains(poteaux, e => Math.Abs(e.Origine.X - x[2]) < 1e-6);
        Assert.Equal(4 * niveaux, bout.Count(e => e.Famille == Famille.Traverse && e.Axe == Axe.Y)); // lisse + sous-lisse
        Assert.Equal(2 * niveaux, bout.Count(e => e.Reference == "HMPLIM070AX")); // plinthe des abouts (axée par défaut)
    }

    [Fact]
    public void Console_avec_securite_cote_mur_garde_corps_en_lisses_avec_plinthe()
    {
        // Sécurité intérieure MDS : en bout de console, le garde-corps côté mur est quand même en lisses, avec plinthe.
        var projet = Standard(p => { p.SecuriteInterieure = TypeSecurite.GardeCorpsMds2; p.PlintheInterieure = TypePlinthe.Sans; });
        Consoles(projet, "HMCON700E", 1, 2);
        var s = GenerateurFacade.Generer(projet);
        var cote = s.Elements.Where(e => (e.NumeroMaille == 2 || e.NumeroMaille == 3) && e.Origine.Y > 0.1 && e.Axe == Axe.X).ToList();
        Assert.DoesNotContain(cote, e => e.Reference.StartsWith("HMGCSE", StringComparison.Ordinal));
        Assert.Contains(cote, e => e.Famille == Famille.Plinthe);
        Assert.Contains(cote, e => e.Famille == Famille.Lisse && Math.Abs(e.Origine.Z % 2.0 - 1.0) < 1e-6);
        // Pas d'about entre les deux consoles voisines : 3 files de poteaux en bout (1 par file), abouts aux 2 extrémités.
        var x = projet.AbscissesFiles();
        var poteaux = s.Elements.Where(e => e.Famille == Famille.Poteau && Math.Abs(e.Origine.Y - 0.70) < 1e-6 && Math.Abs(e.Origine.Z - 2.04) < 1e-6).ToList();
        Assert.Equal(3, poteaux.Count);
        Assert.Equal(2, s.Elements.Count(e => e.Famille == Famille.Traverse && e.Axe == Axe.Y && Math.Abs(e.Origine.Y - 0.70) < 1e-6 && Math.Abs(e.Origine.Z - 3.0) < 1e-6));
    }

    [Theory]
    [InlineData(ModeleEchafaudage.MultiSystem)]
    [InlineData(ModeleEchafaudage.MultiReto)]
    public void Console_sur_maille_courte_traverse_au_lieu_de_lisse(ModeleEchafaudage modele)
    {
        // Pas de lisse sous 1,50 m : HMTRA… (Laurent, 25/11/2026, alerte « HMLIS070 absent »).
        var biblio = BibliothequeBlocs.DepuisFichier("blocs-3D-echafaudage.txt");
        foreach (double l in new[] { 0.70, 1.00, 1.50, 2.00, 2.50, 3.00 }) // 0,75 et 1,05 : pas de plateau NF
        {
            var projet = Standard(p => { p.Modele = modele; p.SecuriteInterieure = TypeSecurite.Lisses; });
            projet.Mailles[1].Longueur = l;
            Consoles(projet, "HMCON700E", 1);
            var s = GenerateurFacade.Generer(projet);
            var refs = s.Elements.Select(e => e.Reference).Where(r => !r.StartsWith("HMTUBAMA", StringComparison.Ordinal) && !References.NonDessinee(r)).ToList();
            Assert.True(biblio.Manquantes(refs).Count == 0, $"maille {l} : {string.Join(", ", biblio.Manquantes(refs))}");
            if (l < 1.5)
            {
                Assert.DoesNotContain(s.Elements, e => e.Reference.StartsWith("HMLIS" + MultiDEC.Core.Geometrie.Cotes.EnCm3(l), StringComparison.Ordinal));
            }
        }
    }

    [Theory]
    [InlineData(ModeleEchafaudage.MultiSystem, "HMCON750MR")]
    [InlineData(ModeleEchafaudage.MultiReto, "HMCON700E2")]
    [InlineData(ModeleEchafaudage.MultiReto, "HMTRA030")]
    public void Console_hors_liste_du_modele_signalee(ModeleEchafaudage modele, string console)
    {
        Assert.DoesNotContain(CatalogueConsoles.Pour(modele), c => c.Reference == console);
        var projet = Standard(p => p.Modele = modele);
        Consoles(projet, console, 0);
        Assert.Contains(GenerateurFacade.Generer(projet).Alertes, a => a.Contains(console));
    }

    [Fact]
    public void Consoles_de_la_liste_dans_la_bibliotheque()
    {
        var biblio = BibliothequeBlocs.DepuisFichier("blocs-3D-echafaudage.txt");
        Assert.Equal(15, CatalogueConsoles.Liste.Count); // HMCON500EE retirée des listes (Laurent, 24/11/2026 19h14)
        Assert.DoesNotContain(CatalogueConsoles.Liste, c => c.Reference == "HMCON500EE");
        Assert.Empty(biblio.Manquantes(CatalogueConsoles.Liste.Select(c => c.Reference)));
    }

    [Fact]
    public void Console_inconnue_signalee()
    {
        var projet = Standard();
        Consoles(projet, "HMCON999", 0);
        var s = GenerateurFacade.Generer(projet);
        Assert.Contains(s.Alertes, a => a.Contains("HMCON999"));
    }

    [Fact]
    public void Amarrages_places_sur_le_dessin()
    {
        var s = GenerateurFacade.Generer(Standard());
        var am = s.Elements.Where(e => e.Famille == Famille.Amarrage && e.Reference.StartsWith("HMTUBAMA", StringComparison.Ordinal)).ToList();
        int n = s.Amarrages.Amarrages.Count;
        Assert.True(n > 0);
        Assert.Equal(n, am.Count);
        Assert.All(am, e => Assert.Equal("HMTUBAMA", e.Reference)); // 0,70 + 0,20 + 0,10 → tube 1,20 m

        // Composants d'amarrage au décompte : 2 colliers, 1 piton, 1 cheville par amarrage (comme la v2).
        var d = MultiDEC.Core.Decompte.CalculDecompte.Calculer(s.Elements.Where(e => e.Famille == Famille.Amarrage));
        Assert.Equal(2 * n, d.Single(l => l.Reference == References.CollierFixe).Quantite);
        Assert.Equal(n, d.Single(l => l.Reference == "EZPITON12").Quantite);
        Assert.Equal(n, d.Single(l => l.Reference == "EZCHEVILLE14").Quantite);
    }

    [Fact]
    public void Diagonales_interieures_avec_garde_corps_interieur()
    {
        var s = GenerateurFacade.Generer(Standard(p => p.SecuriteInterieure = TypeSecurite.GardeCorpsMds2));
        var dia = s.Elements.Where(e => e.Famille == Famille.DiagonaleVerticale).ToList();
        Assert.Equal(dia.Count(e => e.Face == Face.Exterieure), dia.Count(e => e.Face == Face.Interieure));
    }

    [Fact]
    public void Pas_de_diagonale_cote_exterieur_sans_garde_corps()
    {
        var s = GenerateurFacade.Generer(Standard(p =>
        {
            p.SecuriteExterieure = TypeSecurite.Sans;
            p.SecuriteInterieure = TypeSecurite.GardeCorpsMds2;
        }));
        var dia = s.Elements.Where(e => e.Famille == Famille.DiagonaleVerticale).ToList();
        Assert.NotEmpty(dia);
        Assert.All(dia, e => Assert.Equal(Face.Interieure, e.Face));
    }

    [Fact]
    public void Pas_de_diagonale_cote_facade_sans_garde_corps()
    {
        var s = GenerateurFacade.Generer(Standard(p => p.SecuriteInterieure = TypeSecurite.Sans));
        var dia = s.Elements.Where(e => e.Famille == Famille.DiagonaleVerticale).ToList();
        Assert.NotEmpty(dia);
        Assert.All(dia, e => Assert.Equal(Face.Exterieure, e.Face));
    }

    [Fact]
    public void Aucune_diagonale_sans_aucun_garde_corps_et_alerte()
    {
        var s = GenerateurFacade.Generer(Standard(p =>
        {
            p.SecuriteExterieure = TypeSecurite.Sans;
            p.SecuriteInterieure = TypeSecurite.Sans;
        }));
        Assert.DoesNotContain(s.Elements, e => e.Famille == Famille.DiagonaleVerticale);
        Assert.Contains(s.Alertes, a => a.Contains("pas de diagonale"));
    }

    [Fact]
    public void References_des_options_presentes_dans_la_bibliotheque()
    {
        var biblio = BibliothequeBlocs.DepuisFichier("blocs-3D-echafaudage.txt");
        foreach (var socle in CatalogueSocles.Tous)
        {
            foreach (var modele in Enum.GetValues<ModeleEchafaudage>())
            {
                var face = Standard(p => { p.Modele = modele; p.Socle = socle.Reference; p.Grutable = true; p.Traverses = TypeTraverse.Renforce; p.TrappesAcier = true; p.LisseGcAccesNiveau0 = true; });
                face.Niveaux.First(n => n.Hauteur > 0).DiagoH = true;
                face.Niveaux.First(n => n.Hauteur > 0).PareGravois = true;
                face.ConvertirPareGravoisAncienFormat();
                face.Niveaux.Add(new Niveau(1.0, TypePlancher.Complet)); // portillon d'about
                var manquantes = biblio.Manquantes(GenerateurFacade.Generer(face).Elements.Select(e => e.Reference))
                                       .Where(r => !r.StartsWith("HMTUBAMA", StringComparison.Ordinal) && !References.NonDessinee(r)).ToList();
                Assert.True(manquantes.Count == 0, $"{modele} {socle.Reference} : blocs manquants {string.Join(", ", manquantes)}");
            }
        }
    }

    /// <summary>Poteaux (base des empilages) posés à une profondeur donnée.</summary>
    private static List<ElementPlace> Pieds(StructureFacade s, double y) =>
        s.Elements.Where(e => e.Famille == Famille.Socle && Math.Abs(e.Origine.Y - y) < 1e-6).ToList();

    private static double Sommet(StructureFacade s, double x, double y) =>
        s.Elements.Where(e => e.Famille == Famille.Poteau && Math.Abs(e.Origine.X - x) < 1e-6 && Math.Abs(e.Origine.Y - y) < 1e-6)
                  .Max(e => e.Origine.Z + int.Parse(e.Reference.Substring(e.Reference.Length - 2)) / 10.0)
          - ReglePoteaux.DecalageEmbaseMs; // Multi-SYSTEM : poteaux posés sur l'embase

    [Fact]
    public void Acces_rapporte_file_supplementaire_trappes_et_garde_corps_deporte()
    {
        var face = Standard(); // 8 mailles de 2,50, accès en maille 4
        face.Mailles[1].Type = TypeMaille.AccesRapporte;
        face.Mailles[2].Type = TypeMaille.AccesRapporte; // deux voisines : file du milieu partagée
        var s = GenerateurFacade.Generer(face);
        double y2 = -0.70 - 0.70;

        Assert.Equal(3, Pieds(s, y2).Count); // files 2, 3 et 4, une seule fois chacune
        Assert.Equal(11.0, Sommet(s, face.AbscissesFiles()[1], y2), 3); // dernier plancher + garde-corps

        // Trappes de 0,60 dans l'accès rapporté (y = -0,70 - 0,025) aux niveaux 2 à 10, sens alterné.
        var trappes = s.Elements.Where(e => e.Famille == Famille.Trappe && Math.Abs(e.Origine.Y + 0.725) < 1e-6 && e.NumeroMaille == 2).ToList();
        Assert.Equal(5, trappes.Count);
        Assert.Equal(new[] { false, true, false, true, false }, trappes.OrderBy(t => t.Origine.Z).Select(t => t.Inverse));

        // Garde-corps extérieur sur la file rapportée, plus sur la file extérieure de la maille.
        Assert.Contains(s.Elements, e => e.Famille == Famille.GardeCorps && e.NumeroMaille == 2 && Math.Abs(e.Origine.Y - y2) < 1e-6);
        Assert.DoesNotContain(s.Elements, e => e.Famille == Famille.GardeCorps && e.Axe == Axe.X && e.NumeroMaille == 2 && Math.Abs(e.Origine.Y + 0.70) < 1e-6);

        // Abouts de 0,70 aux deux bouts du groupe, pas entre les deux mailles.
        var aboutsRap = s.Elements.Where(e => e.Axe == Axe.Y && e.Famille == Famille.GardeCorps && Math.Abs(e.Origine.Y + 0.70) < 1e-6 && Math.Abs(e.Origine.Z - 4) < 1e-6).ToList();
        Assert.Equal(2, aboutsRap.Count);
    }

    [Fact]
    public void Module_appro_aux_niveaux_coches_seulement()
    {
        var face = Standard();
        face.Mailles[0].Type = TypeMaille.Appro;
        foreach (var n in face.Niveaux)
        {
            n.Appro = Math.Abs(n.Hauteur - 4) < 1e-6 || Math.Abs(n.Hauteur - 8) < 1e-6;
        }

        var s = GenerateurFacade.Generer(face);
        double y2 = -0.70 - 2.0;
        Assert.Equal(2, Pieds(s, y2).Count);
        Assert.Equal(9.0, Sommet(s, 0, y2), 3); // jusqu'au plus haut niveau desservi + 1 m

        Assert.Equal(2, Nb(s, "HMGCAPPROGC200")); // maille de 2,50 → garde-corps de 2,00, aux niveaux 4 et 8
        Assert.Equal(4, Nb(s, References.SupportAppro));
        Assert.Equal(4, Nb(s, References.CadreAppro));
        Assert.Contains(s.Elements, e => e.Famille == Famille.Plateau && e.NumeroMaille == 1 && e.Origine.Y < -0.70 && Math.Abs(e.Origine.Z - 8) < 1e-6);
        Assert.DoesNotContain(s.Elements, e => e.Famille == Famille.Plateau && e.NumeroMaille == 1 && e.Origine.Y < -0.70 && Math.Abs(e.Origine.Z - 6) < 1e-6);

        // Niveaux desservis : maille ouverte côté extérieur ; ailleurs garde-corps habituel.
        bool gcExt(double z) => s.Elements.Any(e => e.Famille == Famille.GardeCorps && e.NumeroMaille == 1 && e.Axe == Axe.X && Math.Abs(e.Origine.Y + 0.70) < 1e-6 && Math.Abs(e.Origine.Z - z) < 1e-6);
        Assert.False(gcExt(4));
        Assert.True(gcExt(6));
        Assert.True(gcExt(10));
    }

    [Fact]
    public void Recette_de_profondeur_choisie()
    {
        var face = Standard();
        face.Mailles[4].Type = TypeMaille.Recette;
        face.Mailles[4].ProfondeurRecette = 1.5;
        face.Niveaux.Single(n => Math.Abs(n.Hauteur - 6) < 1e-6).Recette = true;
        var s = GenerateurFacade.Generer(face);
        double y2 = -0.70 - 1.5;

        Assert.Equal(2, Pieds(s, y2).Count);
        Assert.Equal(7.0, Sommet(s, face.AbscissesFiles()[4], y2), 3);
        var plateaux = s.Elements.Where(e => e.Famille == Famille.Plateau && e.NumeroMaille == 5 && e.Origine.Y < -0.70).ToList();
        Assert.NotEmpty(plateaux);
        Assert.All(plateaux, e => Assert.Equal(6, e.Origine.Z, 6));
        Assert.Contains(s.Elements, e => e.Reference == "HMLISR150" && Math.Abs(e.Origine.Z - 6) < 1e-6); // poutrelle sous le plancher (renforcée jusqu'à 1,50 m, v2)
        Assert.Contains(s.Elements, e => e.Reference == "HMDIA1502" && e.Axe == Axe.Y); // contreventement en travers
    }

    [Fact]
    public void Stabilisation_file_lisses_et_diagonales_jusqu_a_2_m_sous_le_dernier_plancher()
    {
        var face = Standard();
        face.Mailles[0].Stabilisation = 1.5;
        var s = GenerateurFacade.Generer(face);
        double y = -0.70 - 1.5;

        Assert.Equal(2, Pieds(s, y).Count);
        Assert.Equal(8.0, Sommet(s, 0, y), 3);
        Assert.Equal(4, s.Elements.Count(e => e.Reference == "HMDIA2502" && Math.Abs(e.Origine.Y - (y - GenerateurFacade.DemiTube)) < 1e-6));
        // En travers : 2 bouts x 5 (0, 2, 4, 6 et la dernière de 8 m au dernier plancher), posées depuis la file de
        // stabilisation vers la façade (pied côté stabilisation, haut côté structure, Laurent 24/11/2026).
        var travers = s.Elements.Where(e => e.Reference == "HMDIA1502" && e.Axe == Axe.Y).ToList();
        Assert.Equal(10, travers.Count);
        Assert.All(travers, e => Assert.Equal(y, e.Origine.Y, 6));
        Assert.All(travers, e => Assert.Equal(180, e.Rotation, 6));
        Assert.Equal(8.0, travers.Max(e => e.Origine.Z), 6);
        Assert.Equal(5, s.Elements.Count(e => e.Famille == Famille.Lisse && e.Axe == Axe.X && Math.Abs(e.Origine.Y - y) < 1e-6)); // 0, 2, 4, 6, 8
    }

    [Fact]
    public void Pignon_sur_maille_d_acces_planchers_tous_les_2_m_et_abouts()
    {
        var face = Standard(); // accès en maille 4 (index 3), dernier plancher 10 m
        int acces = face.Mailles.FindIndex(m => m.Type == TypeMaille.Acces);
        face.Mailles[acces].EcartHauteur = 4; // colonne « Ht max » à 14 m
        var s = GenerateurFacade.Generer(face);
        var x = face.AbscissesFiles();

        Assert.Equal(15.0, Sommet(s, x[acces], -0.70), 3);
        Assert.Equal(15.0, Sommet(s, x[acces + 1], 0), 3);
        Assert.Equal(11.0, Sommet(s, x[0], -0.70), 3);
        Assert.Equal(7, s.Elements.Count(e => e.Famille == Famille.Trappe && e.NumeroMaille == acces + 1)); // 2 à 14

        foreach (double z in new[] { 12.0, 14.0 })
        {
            Assert.Equal(2, s.Elements.Count(e => e.Axe == Axe.Y && e.Famille == Famille.GardeCorps && Math.Abs(e.Origine.Z - z) < 1e-6));
            Assert.Contains(s.Elements, e => e.Famille == Famille.Traverse && Math.Abs(e.Origine.X - x[acces]) < 1e-6 && Math.Abs(e.Origine.Z - z) < 1e-6);
        }

        Assert.DoesNotContain(s.Elements, e => e.Origine.Z > 11.5 && e.NumeroMaille != acces + 1 && e.NumeroMaille != 0);
        Assert.Equal(14.0, face.Mailles[acces].HauteurMax(10), 6);
        Assert.Equal(10.0, face.Mailles[0].HauteurMax(10), 6);
    }

    [Fact]
    public void Ht_max_maille_plus_basse_et_plus_haute_avec_abouts_cote_vide()
    {
        var face = Standard(); // dernier plancher 10 m, mailles de 2,50, accès en maille 4
        face.Mailles[1].EcartHauteur = -4; // maille 2 arrêtée à 6 m
        face.Mailles[6].EcartHauteur = 2;  // maille 7 montée à 12 m (plancher complet)
        face.Mailles[7].EcartHauteur = 3;  // refusé : pas un multiple de 2 m
        var s = GenerateurFacade.Generer(face);
        var x = face.AbscissesFiles();
        bool A(ElementPlace e, double xf, double z) => Math.Abs(e.Origine.X - xf) < 1e-6 && Math.Abs(e.Origine.Z - z) < 1e-6;

        // Maille 2 : rien au-dessus de 6 m ; ses voisines ont un about côté maille 2 à 8 et 10 m.
        Assert.DoesNotContain(s.Elements, e => e.NumeroMaille == 2 && e.Famille == Famille.Plateau && e.Origine.Z > 6.5);
        Assert.DoesNotContain(s.Elements, e => e.NumeroMaille == 2 && e.Famille == Famille.DiagonaleVerticale && e.Origine.Z > 5);
        foreach (double z in new[] { 8.0, 10.0 })
        {
            Assert.Contains(s.Elements, e => e.Axe == Axe.Y && e.Famille == Famille.GardeCorps && e.NumeroMaille == 1 && A(e, x[1], z));
            Assert.Contains(s.Elements, e => e.Axe == Axe.Y && e.Famille == Famille.GardeCorps && e.NumeroMaille == 3 && A(e, x[2], z));
        }

        Assert.Contains(s.Elements, e => e.Famille == Famille.GardeCorps && e.NumeroMaille == 2 && Math.Abs(e.Origine.Z - 6) < 1e-6 && e.Axe == Axe.X);

        // Maille 7 : plancher complet à 12 m, abouts des deux côtés, poteaux jusqu'à 13 m.
        // (plus haute que ses voisines : plancher avec plateau d'accès)
        Assert.Contains(s.Elements, e => e.NumeroMaille == 7 && e.Famille == Famille.Trappe && Math.Abs(e.Origine.Z - 12) < 1e-6);
        Assert.Contains(s.Elements, e => e.Axe == Axe.Y && e.Famille == Famille.GardeCorps && A(e, x[6], 12));
        Assert.Contains(s.Elements, e => e.Axe == Axe.Y && e.Famille == Famille.GardeCorps && A(e, x[7], 12));
        Assert.Equal(13.0, Sommet(s, x[7], -0.70), 3);
        Assert.Contains(s.Alertes, a => a.Contains("multiple de 2 m"));
        Assert.Equal(10.0, face.Mailles[7].HauteurMax(10), 6);
    }

    [Fact]
    public void Maille_plus_haute_que_ses_voisines_plateau_d_acces_et_abouts_a_sa_hauteur()
    {
        var face = Standard(); // mailles de 2,50 m, dernier plancher 10 m
        face.Mailles[5].EcartHauteur = 4;  // maille 6 montée à 14 m : plus haute que ses deux voisines
        face.Mailles[0].EcartHauteur = -4; // maille 1 (bout gauche) arrêtée à 6 m
        var s = GenerateurFacade.Generer(face);
        var x = face.AbscissesFiles();

        // Un plateau à trappe à 12 et 14 m (aucune voisine n'y arrive), sens alterné ; pas à 10 m.
        var trappes = s.Elements.Where(e => e.NumeroMaille == 6 && e.Famille == Famille.Trappe).OrderBy(e => e.Origine.Z).ToList();
        Assert.Equal(new[] { 12.0, 14.0 }, trappes.Select(e => e.Origine.Z));
        Assert.NotEqual(trappes[0].Inverse, trappes[1].Inverse);
        Assert.DoesNotContain(s.Elements, e => e.NumeroMaille != 6 && e.Famille == Famille.Trappe && e.Origine.Z > 10.5);

        // About de bout de face : aux planchers de la maille 1 seulement (rien dans le vide au-dessus de 6 m).
        var aboutsGauche = s.Elements.Where(e => e.Axe == Axe.Y && e.Famille == Famille.GardeCorps && Math.Abs(e.Origine.X) < 1e-6).ToList();
        Assert.NotEmpty(aboutsGauche);
        Assert.All(aboutsGauche, e => Assert.True(e.Origine.Z <= 7.0 + 1e-6));
        Assert.Equal(7.0, Sommet(s, x[0], 0), 3);
        Assert.Equal(7.0, Sommet(s, x[0], -0.70), 3);
    }

    [Fact]
    public void Garde_corps_toujours_tenu_par_un_poteau_de_chaque_cote()
    {
        var face = Standard(p => { p.AboutDroit = false; p.SecuriteInterieure = TypeSecurite.GardeCorpsMds2; });
        Consoles(face, "HMCON700E", 2);
        var s = GenerateurFacade.Generer(face);
        var x = face.AbscissesFiles();

        // Protection intérieure : tous les poteaux intérieurs montent à 11 m, sauf sous la console du dernier niveau…
        Assert.Equal(11.0, Sommet(s, x[0], 0), 3);
        Assert.Equal(11.0, Sommet(s, x[1], 0), 3);
        // … où l'about en lisses de la console s'accroche aussi au poteau de la file intérieure.
        Assert.Equal(11.0, Sommet(s, x[2], 0), 3);
        Assert.Equal(11.0, Sommet(s, x[3], 0), 3);
    }

    [Fact]
    public void Portillon_au_premier_plancher_entre_0_50_et_1_50_m_meme_sans_maille_d_acces()
    {
        var face = Standard(p => p.HauteurDernierPlancher = 9.5); // planchers 1,5 / 3,5 / … / 9,5
        foreach (var m in face.Mailles)
        {
            m.Type = TypeMaille.Travail;
        }

        var s = GenerateurFacade.Generer(face);
        var portillon = Assert.Single(s.Elements, e => e.Reference.StartsWith("HMPORTILLONPLIN", StringComparison.Ordinal));
        Assert.Equal(2.5, portillon.Origine.Z, 6);
        Assert.Single(s.Elements, e => e.Reference == "HMECH150LIS");

        // Premier plancher à 2 m ou plus : pas de portillon ; plancher sous 0,50 m non plus.
        var haut = Standard(p => p.HauteurDernierPlancher = 10);
        Assert.DoesNotContain(GenerateurFacade.Generer(haut).Elements, e => e.Reference.StartsWith("HMPORTILLON", StringComparison.Ordinal));
    }

    [Fact]
    public void Poteau_interieur_sans_garde_corps_arrete_au_plancher()
    {
        var face = Standard(p => p.AboutDroit = false); // protection intérieure « Sans » par défaut
        var s = GenerateurFacade.Generer(face);
        var x = face.AbscissesFiles();
        Assert.Equal(11.0, Sommet(s, x[0], 0), 3);              // about gauche : garde-corps → +1 m
        Assert.Equal(10.0, Sommet(s, x[x.Count - 1], 0), 3);    // pas d'about : s'arrête au plancher
        Assert.Equal(10.0, Sommet(s, x[3], 0), 3);
    }

    [Fact]
    public void Poutre_de_franchissement_supprime_la_file_du_milieu_sous_le_plancher()
    {
        var face = Standard(); // 8 mailles de 2,50, accès en maille 4
        face.Mailles[5].Poutre = 4; // mailles 6 et 7, file 7 (index 6) supprimée sous 4 m
        face.Mailles[0].Poutre = 4; // refusée : première maille
        var s = GenerateurFacade.Generer(face);
        var x = face.AbscissesFiles();
        bool A(ElementPlace e, double xx) => Math.Abs(e.Origine.X - xx) < 1e-6;

        Assert.Equal(2, Nb(s, "HMPOUT500E"));
        Assert.All(s.Elements.Where(e => e.Reference == "HMPOUT500E"), e => { Assert.Equal(3.5, e.Origine.Z, 6); Assert.Equal(x[5], e.Origine.X, 6); });
        Assert.DoesNotContain(s.Elements, e => A(e, x[6]) && e.Axe == Axe.Z && e.Famille != Famille.Poutre && e.Origine.Z < 4 - 1e-6);
        Assert.Contains(s.Elements, e => A(e, x[6]) && e.Famille == Famille.Poteau && Math.Abs(e.Origine.Z - 4) < 1e-6);
        Assert.DoesNotContain(s.Elements, e => (e.NumeroMaille == 6 || e.NumeroMaille == 7) && e.Famille != Famille.Poutre && e.Famille != Famille.Traverse && e.Origine.Z < 4 - 1e-6);
        Assert.Equal(2, Nb(s, "HMTUB300"));
        Assert.Equal(4, s.Elements.Count(e => e.Reference == References.CollierFixe && e.Famille == Famille.Poutre));
        Assert.All(s.Elements.Where(e => e.Reference == "HMTUB300"), e => Assert.Equal(Math.Atan2(0.7, 2.5) * 180 / Math.PI, e.Rotation, 6));

        // Abouts des mailles voisines (5 à droite, 8 à gauche) au niveau 2, sous la poutre.
        Assert.Contains(s.Elements, e => e.Axe == Axe.Y && e.Famille == Famille.GardeCorps && A(e, x[5]) && Math.Abs(e.Origine.Z - 2) < 1e-6 && !e.Inverse);
        Assert.Contains(s.Elements, e => e.Axe == Axe.Y && e.Famille == Famille.GardeCorps && A(e, x[7]) && Math.Abs(e.Origine.Z - 2) < 1e-6 && e.Inverse);
        Assert.DoesNotContain(s.Amarrages.Amarrages, a => a.File == 6 && a.Hauteur < 4);
        Assert.Contains(s.Alertes, a => a.Contains("Maille 1 : poutre"));
        Assert.Equal(0, Nb(s, "HMPOUT500"));
    }

    [Fact]
    public void Escalier_1_50_volees_alternees_derniere_cote_facade()
    {
        var face = Standard(); // dernier plancher 10 m, niveaux tous les 2 m, mailles de 2,50 m
        face.Mailles[1].Type = TypeMaille.Escalier;
        var s = GenerateurFacade.Generer(face);
        double x0 = face.AbscissesFiles()[1];
        double x1 = face.AbscissesFiles()[2];
        double y2 = -0.70 - 1.5;

        var volees = s.Elements.Where(e => e.Reference == "HMESCALU250200").OrderBy(e => e.Origine.Z).ToList();
        Assert.Equal(new[] { 2.0, 4, 6, 8, 10 }, volees.Select(e => Math.Round(e.Origine.Z, 3)));
        var haut = volees.Last();
        Assert.False(haut.Inverse); // arrive côté façade, en haut côté x0
        Assert.Equal(x0, haut.Origine.X, 6);
        Assert.Equal(-1.40, haut.Origine.Y, 6);
        var avant = volees[3];
        Assert.True(avant.Inverse); // moitié extérieure, sens opposé
        Assert.Equal(x1, avant.Origine.X, 6);
        Assert.Equal(-2.10, avant.Origine.Y, 6);

        Assert.Equal(10, s.Elements.Count(e => e.Reference == "HMGCESCALU200"));
        Assert.Equal(3, s.Elements.Count(e => e.Reference == "HMGCESCALUEXTR")); // pied de la 1re volée (2 côtés), haut de la dernière côté vide seulement

        // Diagonales en travers côté extérieur de la tour, aucune devant l'entrée (pied de la 1re volée, au sol).
        var travers = s.Elements.Where(e => e.Reference == "HMDIA1502" && e.Axe == Axe.Y && e.NumeroMaille == 2).ToList();
        Assert.All(travers, e => Assert.True(e.Origine.X < x0 || e.Origine.X > x1));
        bool piedADroite = !volees[0].Inverse;
        Assert.DoesNotContain(travers, e => e.Origine.Z < 0.01 && (piedADroite ? e.Origine.X > x1 : e.Origine.X < x0));
        Assert.Equal(5, s.Elements.Count(e => e.Reference == "HMMONTGCE"));
        Assert.Equal(2, Pieds(s, y2).Count);
        Assert.Equal(11.0, Sommet(s, x0, y2), 3);

        // Pas de garde-corps MDS côté extérieur sur la maille d'escalier : lisse de plancher + fermeture sur 1,50 m.
        Assert.DoesNotContain(s.Elements, e => e.NumeroMaille == 2 && e.Famille == Famille.GardeCorps && e.Reference.StartsWith("HMGCSE", StringComparison.Ordinal));
        Assert.Contains(s.Elements, e => e.Reference == "HMLIS150" && Math.Abs(e.Origine.Z - 10.5) < 1e-6 && Math.Abs(e.Origine.X - (x1 - 1.5)) < 1e-6);
        Assert.Contains(s.Elements, e => e.Reference == "HMLIS150" && Math.Abs(e.Origine.Z - 8.5) < 1e-6 && Math.Abs(e.Origine.X - x0) < 1e-6);
        Assert.DoesNotContain(s.Elements, e => e.NumeroMaille == 2 && e.Famille == Famille.DiagonaleVerticale && Math.Abs(e.Origine.Y + 0.70 + GenerateurFacade.DemiTube) < 1e-6);
    }

    [Fact]
    public void Demi_escalier_volees_dans_le_meme_sens_et_garde_corps_central()
    {
        var face = Standard();
        face.Mailles[5].Type = TypeMaille.DemiEscalier100;
        var s = GenerateurFacade.Generer(face);

        var volees = s.Elements.Where(e => e.Reference == "HMESCALU25020088D").ToList();
        Assert.Equal(5, volees.Count);
        Assert.All(volees, e => Assert.False(e.Inverse));
        Assert.All(volees, e => Assert.Equal(-0.70 - 0.93, e.Origine.Y, 6));
        Assert.Equal(5, s.Elements.Count(e => e.Reference == "HMGCESCALU200")); // côté maille seulement
        Assert.Equal(8, s.Elements.Count(e => e.Reference == "HMPIQLIS-E")); // 2 par niveau de 2 à 8 m
        Assert.Equal(8, s.Elements.Count(e => e.Reference == "HMPOT10" && e.Famille == Famille.GardeCorps));
        Assert.Single(s.Elements, e => e.Reference == "HMMONTGCE"); // ouverture au dernier niveau
        Assert.Single(s.Elements, e => e.Reference == "HMGCESCALUEXTR"); // au pied seulement : on sort en haut
        double x1 = face.AbscissesFiles()[6];
        Assert.DoesNotContain(s.Elements, e => e.Reference == "HMDIA1002" && e.Axe == Axe.Y && e.Origine.Z < 0.01 && e.Origine.X > x1); // entrée dégagée
        Assert.Equal(2, Pieds(s, -0.70 - 1.0).Count);
    }

    [Fact]
    public void Premier_plancher_a_1_m_volee_de_1_m_sortie_sur_la_maille_suivante()
    {
        var face = Standard(p => p.HauteurDernierPlancher = 9); // planchers 1, 3, 5, 7, 9
        face.Mailles[5].Type = TypeMaille.DemiEscalier070;
        var s = GenerateurFacade.Generer(face);
        var x = face.AbscissesFiles();
        double y2 = -0.70 - 0.70;

        // Volée de 1 m hors de la tour : arrive au bout droit de la tour (x[6]) et descend sur la maille suivante.
        var v1 = Assert.Single(s.Elements, e => e.Reference == "HMESCALU175100");
        Assert.Equal(x[6], v1.Origine.X, 6);
        Assert.Equal(1.0, v1.Origine.Z, 6);
        Assert.False(v1.Inverse);
        Assert.Equal(4, s.Elements.Count(e => e.Reference == "HMESCALU250200")); // 1→3, 3→5, 5→7, 7→9 dans la tour

        // Pied sur une HMTIP au sol, plateaux acier de 0,75 m sur le reste de la maille de 2,50 m, poteau au bout.
        Assert.Contains(s.Elements, e => e.Reference == "HMTIP070" && Math.Abs(e.Origine.X - (x[6] + 1.75)) < 1e-6 && Math.Abs(e.Origine.Z) < 1e-6);
        Assert.Equal(2, s.Elements.Count(e => e.Reference == "HMPLA0753CL6" && Math.Abs(e.Origine.X - (x[6] + 1.75)) < 1e-6));
        Assert.Contains(Pieds(s, y2), p => Math.Abs(p.Origine.X - x[7]) < 1e-6);
        Assert.Equal(2.0, Sommet(s, x[7], y2), 3);

        // Côté extérieur : lisses à 0 et 1 m, garde-corps 1 m au-dessus du palier (lisses à 1,50 et 2,00 m).
        var lissesExt = s.Elements.Where(e => e.Famille == Famille.Lisse && Math.Abs(e.Origine.X - x[6]) < 1e-6 && Math.Abs(e.Origine.Y - y2) < 1e-6)
            .Select(e => Math.Round(e.Origine.Z, 2)).OrderBy(z => z).ToList();
        Assert.Equal(new[] { 0.0, 0.5, 1.0, 1.5, 2.0 }, lissesExt);
        Assert.Contains(s.Elements, e => e.Famille == Famille.Lisse && Math.Abs(e.Origine.X - x[6]) < 1e-6 && Math.Abs(e.Origine.Y + 0.70) < 1e-6 && Math.Abs(e.Origine.Z - 0.5) < 1e-6);

        // Pas de diagonale en travers devant l'entrée de la tour au plancher de 1 m.
        Assert.DoesNotContain(s.Elements, e => e.Reference == "HMDIA0702" && e.Axe == Axe.Y && e.Origine.X > x[6] && Math.Abs(e.Origine.Z - 1) < 1e-6);

        // Escalier 1,50 m : la volée suivante part du bout où la volée de 1 m arrive.
        var face2 = Standard(p => p.HauteurDernierPlancher = 7); // planchers 1, 3, 5, 7
        face2.Mailles[5].Type = TypeMaille.Escalier;
        var s2 = GenerateurFacade.Generer(face2);
        var x2 = face2.AbscissesFiles();
        var dehors = Assert.Single(s2.Elements, e => e.Reference == "HMESCALU175100");
        var suivante = s2.Elements.Where(e => e.Reference == "HMESCALU250200").OrderBy(e => e.Origine.Z).First();
        Assert.Equal(dehors.Inverse, suivante.Inverse);
        Assert.Equal(dehors.Origine.Y, suivante.Origine.Y, 6);
        Assert.Equal(dehors.Inverse ? x2[5] : x2[6], dehors.Origine.X, 6);
    }

    [Fact]
    public void Maille_escalier_lisses_de_pied_sur_ses_4_faces()
    {
        // Laurent 24/11/2026 : toujours des lisses de pied sur les 4 faces d'une maille escalier (option décochée).
        var face = Standard();
        Assert.False(face.Parametres.LissesEnPied);
        face.Mailles[5].Type = TypeMaille.DemiEscalier070;
        var s = GenerateurFacade.Generer(face);
        var pied = s.Elements.Where(e => e.NumeroMaille == 6 && e.Famille == Famille.Lisse && Math.Abs(e.Origine.Z) < 1e-6).Select(e => Math.Round(e.Origine.Y, 3)).ToList();
        Assert.Contains(0.0, pied);
        Assert.Contains(-0.70, pied);
        Assert.Contains(-1.40, pied);
        Assert.DoesNotContain(s.Elements, e => e.NumeroMaille == 4 && e.Famille == Famille.Lisse && Math.Abs(e.Origine.Z) < 1e-6);
    }

    [Fact]
    public void Escalier_premier_plancher_a_1_m_entree_libre_jusqu_a_2_m()
    {
        // Volée de 1 m restée dans la tour (stabilisation sur la maille voisine) : pas de traverse ni de diagonale
        // sous 2 m au bout d'entrée, première traverse à 2 m (Laurent, 24/11/2026).
        var face = Standard(p => p.HauteurDernierPlancher = 7); // planchers 1, 3, 5, 7
        face.Mailles[5].Type = TypeMaille.Escalier;
        face.Mailles[6].Stabilisation = 1.5;
        face.Mailles[4].Stabilisation = 1.5;
        var s = GenerateurFacade.Generer(face);
        var x = face.AbscissesFiles();
        Assert.Contains(s.Elements, e => e.Reference == "HMESCALU175100");

        var premiere = s.Elements.Where(e => e.Famille == Famille.Escalier && e.Reference.StartsWith("HMESCALU", StringComparison.Ordinal)).OrderBy(e => e.Origine.Z).First();
        double xEntree = premiere.Inverse ? x[5] : x[6]; // bout du pied de la première volée
        var auBout = s.Elements.Where(e => e.NumeroMaille == 6 && e.Axe == Axe.Y && e.Origine.Y < -0.70 + 1e-6 && Math.Abs(e.Origine.X - xEntree) < 1e-6).ToList();
        Assert.DoesNotContain(auBout, e => e.Origine.Z > 1e-6 && e.Origine.Z < 2 - 1e-6);
        Assert.Contains(auBout, e => e.Famille == Famille.Traverse && Math.Abs(e.Origine.Z - 2) < 1e-6);
        Assert.Contains(auBout, e => e.Famille == Famille.Traverse && Math.Abs(e.Origine.Z) < 1e-6);

        // Pied de la volée sur une HMTIP150, puis plateaux acier de 1,50 m en travers jusqu'au bout d'entrée.
        double xPied = premiere.Inverse ? x[6] - 1.75 : x[5] + 1.75;
        Assert.Contains(s.Elements, e => e.Reference == "HMTIP150" && Math.Abs(e.Origine.X - xPied) < 1e-6 && Math.Abs(e.Origine.Z) < 1e-6);
        var plateaux = s.Elements.Where(e => e.Reference == "HMPLA1503CL6" && e.NumeroMaille == 6).ToList();
        Assert.Equal(2, plateaux.Count);
        Assert.All(plateaux, e => Assert.Equal(Axe.Y, e.Axe));
        Assert.All(plateaux, e => Assert.True(Math.Abs(e.Origine.X - xPied) <= 0.75 + 1e-6 && Math.Abs(e.Origine.X - xEntree) <= 0.75 + 1e-6));
    }

    [Fact]
    public void Acces_rapporte_plus_haut_que_sa_maille_garde_corps_cote_facade()
    {
        // Laurent 24/11/2026 : la partie qui dépasse reçoit les garde-corps des paramètres (ici intérieur MDS 2).
        var face = Standard(p => p.SecuriteInterieure = TypeSecurite.GardeCorpsMds2); // planchers 2…10
        face.Mailles[2].Type = TypeMaille.AccesRapporte;
        face.Mailles[2].EcartHauteur = -4; // maille arrêtée à 6 m, colonne d'accès jusqu'à 10 m
        var s = GenerateurFacade.Generer(face);
        var x = face.AbscissesFiles();
        var gc = s.Elements.Where(e => e.NumeroMaille == 3 && e.Famille == Famille.GardeCorps && e.Axe == Axe.X && Math.Abs(e.Origine.Y + 0.70) < 1e-6 && Math.Abs(e.Origine.X - x[2]) < 1e-6)
            .Select(e => Math.Round(e.Origine.Z, 2)).OrderBy(z => z).ToList();
        Assert.Equal(new[] { 8.0, 10.0 }, gc);
        Assert.True(Sommet(s, x[2], -0.70) >= 11.0 - 1e-6); // poteaux montés pour tenir le garde-corps
    }

    [Fact]
    public void Maille_plus_haute_que_son_acces_rapporte_garde_corps_exterieur()
    {
        var face = Standard(); // planchers 2…10
        face.Mailles[2].Type = TypeMaille.AccesRapporte;
        face.Mailles[2].EcartHauteur = 2; // maille montée à 12 m, colonne d'accès jusqu'à 10 m
        var s = GenerateurFacade.Generer(face);
        var gc = s.Elements.Where(e => e.NumeroMaille == 3 && e.Famille == Famille.GardeCorps && e.Axe == Axe.X && Math.Abs(e.Origine.Y + 0.70) < 1e-6).ToList();
        Assert.Contains(gc, e => Math.Abs(e.Origine.Z - 12) < 1e-6);
        Assert.DoesNotContain(gc, e => e.Origine.Z < 10 + 1e-6);
    }

    [Fact]
    public void Escalier_sur_maille_de_2_m_ignore_avec_alerte()
    {
        var face = Standard();
        face.Mailles[0].Longueur = 2.0;
        face.Mailles[0].Type = TypeMaille.Escalier;
        var s = GenerateurFacade.Generer(face);
        Assert.DoesNotContain(s.Elements, e => e.Famille == Famille.Escalier);
        Assert.Contains(s.Alertes, a => a.Contains("escalier impossible", StringComparison.Ordinal));
        Assert.Contains(s.Elements, e => e.NumeroMaille == 1 && e.Reference.StartsWith("HMGCSE", StringComparison.Ordinal)); // garde-corps normal
    }

    [Fact]
    public void Sous_mailles_references_presentes_dans_la_bibliotheque()
    {
        var biblio = BibliothequeBlocs.DepuisFichier("blocs-3D-echafaudage.txt");
        foreach (bool acier in new[] { false, true })
        {
            var face = Standard(p => p.TrappesAcier = acier);
            face.Mailles[0].Type = TypeMaille.AccesRapporte;
            face.Mailles[1].Type = TypeMaille.Appro;
            face.Mailles[2].Type = TypeMaille.Recette;
            face.Mailles[2].ProfondeurRecette = 2.5;
            face.Mailles[5].Stabilisation = 2.0;
            face.Mailles[4].Poutre = 4;
            face.Mailles[6].Type = TypeMaille.Escalier;
            face.Mailles[7].Type = acier ? TypeMaille.DemiEscalier100 : TypeMaille.DemiEscalier070;
            foreach (var n in face.Niveaux)
            {
                n.Recette = true;
            }

            var manquantes = biblio.Manquantes(GenerateurFacade.Generer(face).Elements.Select(e => e.Reference))
                                   .Where(r => !r.StartsWith("HMTUBAMA", StringComparison.Ordinal) && !References.NonDessinee(r)).ToList();
            Assert.True(manquantes.Count == 0, $"blocs manquants {string.Join(", ", manquantes)}");
        }
    }

    [Fact]
    public void References_generees_presentes_dans_la_bibliotheque()
    {
        var biblio = BibliothequeBlocs.DepuisFichier("blocs-3D-echafaudage.txt");
        Assert.True(biblio.Nombre > 500);

        foreach (var modele in Enum.GetValues<ModeleEchafaudage>())
        {
            var s = GenerateurFacade.Generer(Standard(p => { p.Modele = modele; p.SecuriteInterieure = TypeSecurite.GardeCorpsMds2; }));
            var manquantes = biblio.Manquantes(s.Elements.Select(e => e.Reference))
                                   .Where(r => !r.StartsWith("HMTUBAMA", StringComparison.Ordinal) && !References.NonDessinee(r)) // bloc provisoire (décision 23/11/2026)
                                   .ToList();
            Assert.True(manquantes.Count == 0, $"{modele} : blocs manquants {string.Join(", ", manquantes)}");
        }
    }

    [Theory]
    [InlineData(GammePlateaux.StandardNF, "HMPLAF2503NF")]
    [InlineData(GammePlateaux.AntiContaminationNF, "HMPLAF2503CL6-AC")]
    public void Plateaux_NF_de_la_bibliotheque_du_24_11(GammePlateaux gamme, string courant)
    {
        // Laurent 24/11/2026 : Standard NF = HMPLAF…3NF ; compléments de 0,20 m = HMPLAF…2CL6 dans les deux gammes NF.
        var biblio = BibliothequeBlocs.DepuisFichier("blocs-3D-echafaudage.txt");
        var s = GenerateurFacade.Generer(Standard(p => { p.Plateaux = gamme; p.LargeurMaille = 1.0; }));
        Assert.Contains(s.Elements, e => e.Reference == courant);
        Assert.Equal("HMPLAF2502CL6", References.Plateau(gamme, 2.5, 0.70, 2));
        Assert.Empty(biblio.Manquantes(s.Elements.Where(e => e.Famille == Famille.Plateau).Select(e => e.Reference)));
    }

    [Fact]
    public void Decompte_regroupe_par_reference()
    {
        var s = GenerateurFacade.Generer(Standard());
        var d = CalculDecompte.Calculer(s.Elements);
        Assert.Equal(s.Elements.Count, d.Sum(l => l.Quantite));
        Assert.Equal(d.Count, d.Select(l => l.Reference).Distinct().Count());
    }

    /// <summary>Extrait du multidec.bib de Laurent (24/11/2026) : DataSet .NET, prix ignorés.</summary>
    private const string ExtraitBib = @"<?xml version=""1.0"" standalone=""yes""?>
<MultiDECDataSet xmlns=""http://tempuri.org/MultiDECDataSet.xsd"">
  <xs:schema id=""MultiDECDataSet"" targetNamespace=""http://tempuri.org/MultiDECDataSet.xsd"" xmlns:xs=""http://www.w3.org/2001/XMLSchema""><xs:element name=""Tarif"" /></xs:schema>
  <Tarif><Référence>TARIF</Référence><Désignation>Tarif général échafaudage 06/2016</Désignation><Prix_x0020_U>0.0000</Prix_x0020_U><Poids_x0020_U>0</Poids_x0020_U><Famille>2.2.0</Famille><Classement>0</Classement></Tarif>
  <Tarif><Référence>HMPOT20</Référence><Désignation>Poteau de 2,00m</Désignation><Prix_x0020_U>30.5</Prix_x0020_U><Poids_x0020_U>9.1</Poids_x0020_U><Famille>Poteaux</Famille><Classement>2.06</Classement></Tarif>
  <Tarif><Référence>HMSOCREG</Référence><Désignation>Socle réglable</Désignation><Poids_x0020_U>3.5</Poids_x0020_U><Famille>Socles</Famille><Classement>1.01</Classement></Tarif>
  <Tarif><Référence>HMCHAPEDOUBLE</Référence><Désignation>Double tête</Désignation><Poids_x0020_U>0.9</Poids_x0020_U><Famille>Accessoires</Famille><Classement>21.18</Classement><RefClient>DT-01</RefClient></Tarif>
</MultiDECDataSet>";

    [Fact]
    public void Multidec_bib_lu_sans_les_prix()
    {
        var c = CatalogueArticles.LireTexte(ExtraitBib);
        Assert.Equal(3, c.Nombre);
        Assert.Equal("2.2.0", c.Version);
        Assert.False(c.TryGet("TARIF", out _));
        Assert.True(c.TryGet("hmpot20", out var pot));
        Assert.Equal("Poteau de 2,00m", pot.Designation);
        Assert.Equal(9.1, pot.PoidsUnitaire);
        Assert.Equal("Poteaux", pot.Famille);
        Assert.Equal(2.06, pot.Classement);
        Assert.True(c.TryGet("HMCHAPEDOUBLE", out var dt));
        Assert.Equal("DT-01", dt.RefClient);
    }

    [Fact]
    public void Decompte_avec_designations_et_poids()
    {
        var c = CatalogueArticles.LireTexte(ExtraitBib);
        var s = GenerateurFacade.Generer(Standard());
        var d = CalculDecompte.Calculer(s.Elements, c);

        // Les articles du fichier d'abord, dans l'ordre du classement (socles 1.01 avant poteaux 2.06).
        Assert.Equal("HMSOCREG", d[0].Reference);
        Assert.Equal("Socles", d[0].FamilleAffichee);
        var pot = d.Single(l => l.Reference == "HMPOT20");
        Assert.Equal("Poteau de 2,00m", pot.Designation);
        Assert.Equal(9.1 * pot.Quantite, pot.PoidsTotal!.Value, 6);
        Assert.Same(pot, d[1]);

        // Références absentes : sans poids, en fin de liste, famille interne.
        var autre = d.First(l => l.Article == null);
        Assert.Null(autre.PoidsTotal);
        Assert.Equal(autre.Famille.ToString(), autre.FamilleAffichee);
        Assert.Contains(autre.Reference, CalculDecompte.SansPoids(d));
        Assert.Equal(d.Where(l => l.PoidsTotal != null).Sum(l => l.PoidsTotal!.Value), CalculDecompte.PoidsTotal(d), 6);
    }

    [Fact]
    public void About_remplace_par_un_deport_en_console_ou_un_autre_garde_corps()
    {
        var face = Standard(p => p.SecuriteInterieure = TypeSecurite.Lisses); // maille de 0,70, Multi-SYSTEM
        face.DefinirAbout(false, 4, null, "HMCON700E");                     // déport à gauche, niveau 4
        face.DefinirAbout(true, 6, TypeSecurite.GardeCorpsAbout, null);     // garde-corps d'about à droite, niveau 6
        var s = GenerateurFacade.Generer(face);
        double L = face.LongueurObtenue;
        bool Z(ElementPlace e, double z) => Math.Abs(e.Origine.Z - z) < 1e-6;

        var deport = s.Elements.Where(e => e.About is { Droit: false } a && Math.Abs(a.Hauteur - 4) < 1e-6).ToList();
        var consoles = deport.Where(e => e.Reference == "HMCON700E").ToList();
        Assert.Equal(2, consoles.Count);
        Assert.All(consoles, e => { Assert.Equal(Axe.X, e.Axe); Assert.Equal(180, e.Rotation); Assert.Equal(0, e.Origine.X, 6); });
        var poteaux = deport.Where(e => e.Famille == Famille.Poteau).ToList();
        Assert.Equal(2, poteaux.Count);
        Assert.All(poteaux, e => Assert.Equal(-0.70, e.Origine.X, 6));
        Assert.Equal(2, deport.Count(e => e.Famille == Famille.Embase)); // chape en Multi-SYSTEM
        Assert.Equal(2, deport.Count(e => e.Famille == Famille.Plateau && Math.Abs(e.Origine.X + 0.70) < 1e-6));
        Assert.Equal(3, deport.Count(e => e.Famille == Famille.Plinthe));                             // about, extérieur, côté mur
        Assert.Equal(4, deport.Count(e => e.Famille == Famille.Lisse && e.Axe == Axe.X));             // 2 côtés × 2 lisses
        Assert.Equal(3, deport.Count(e => e.Famille == Famille.Traverse && e.Axe == Axe.Y));          // pied + lisse + sous-lisse
        // L'about d'origine (x = 0) n'est plus posé à ce niveau, mais il l'est aux autres.
        Assert.DoesNotContain(deport, e => e.Famille == Famille.GardeCorps); // about MDS 2 des paramètres remplacé
        Assert.Contains(s.Elements, e => e.Axe == Axe.Y && Math.Abs(e.Origine.X) < 1e-6 && e.About is { Droit: false } a && Math.Abs(a.Hauteur - 6) < 1e-6);

        var droite = s.Elements.Where(e => e.About is { Droit: true } a && Math.Abs(a.Hauteur - 6) < 1e-6).ToList();
        Assert.Contains(droite, e => e.Reference == "HMGCA070" && Math.Abs(e.Origine.X - L) < 1e-6 && Z(e, 7));

        var biblio = BibliothequeBlocs.DepuisFichier("blocs-3D-echafaudage.txt");
        Assert.Empty(biblio.Manquantes(deport.Select(e => e.Reference)));

        // Mémorisé dans le projet, puis retour aux paramètres de la face.
        var projet = new ProjetStructure();
        projet.Faces.Add(face);
        var relu = FichierProjet.DepuisJson(FichierProjet.VersJson(projet)).S1;
        Assert.Equal("HMCON700E", relu.AboutRemplace(false, 4)?.Console);
        Assert.Equal(TypeSecurite.GardeCorpsAbout, relu.AboutRemplace(true, 6)?.Securite);
        relu.DefinirAbout(false, 4, null, null);
        Assert.Null(relu.AboutRemplace(false, 4));
        Assert.Contains(GenerateurFacade.Generer(relu).Elements, e => e.Reference == "HMGCSE070GT" && Math.Abs(e.Origine.X) < 1e-6 && Z(e, 4));
    }

    [Theory]
    [InlineData(ModeleEchafaudage.MultiSystem, 0.70, new[] { "HMCON700E", "HMCON700E2", "HMCON700EE", "HMCON700EE2", "HMCON1000E", "HMCON1000E2" })]
    [InlineData(ModeleEchafaudage.MultiReto, 0.75, new[] { "HMCON750E", "HMCON750MR", "HMCON750EE", "HMCON750MR2", "HMCON1050E", "HMCON1050MR" })]
    public void Consoles_possibles_en_deport_d_about(ModeleEchafaudage modele, double largeur, string[] attendues)
    {
        Assert.Equal(attendues, CatalogueConsoles.PourDeport(modele, largeur).Select(c => c.Reference));
        var biblio = BibliothequeBlocs.DepuisFichier("blocs-3D-echafaudage.txt");
        foreach (string console in attendues)
        {
            var face = Standard(p => { p.Modele = modele; p.LargeurMaille = largeur; });
            face.DefinirAbout(true, 2, null, console);
            var s = GenerateurFacade.Generer(face);
            Assert.DoesNotContain(s.Alertes, a => a.Contains("impossible"));
            var refs = s.Elements.Where(e => e.About is { Droit: true } a && Math.Abs(a.Hauteur - 2) < 1e-6).Select(e => e.Reference).ToList();
            Assert.Contains(console, refs);
            Assert.True(biblio.Manquantes(refs).Count == 0, $"{console} : {string.Join(", ", biblio.Manquantes(refs))}");
        }
    }

    [Fact]
    public void Enregistrer_puis_ouvrir_donne_le_meme_projet()
    {
        var face = Standard();
        Consoles(face, "HMCON700E", 0, 3);
        face.Mailles[1].Diagonale = true;
        var projet = new ProjetStructure { Chantier = "Essai", General = { Bache = true } };
        projet.Faces.Add(face);

        var relu = FichierProjet.DepuisJson(FichierProjet.VersJson(projet));
        var f = relu.S1;

        Assert.Equal("Essai", relu.Chantier);
        Assert.True(relu.General.Bache);
        Assert.True(f.Parametres.Bache);
        Assert.Equal(face.Mailles.Count, f.Mailles.Count);
        Assert.Equal(face.Niveaux.Select(n => n.Console), f.Niveaux.Select(n => n.Console));
        Assert.Equal(new[] { 2.0, 4, 6, 8, 10 }, f.Mailles[3].Consoles);
        Assert.Empty(f.Mailles[2].Consoles);
        Assert.Equal(face.Mailles.Select(m => m.Diagonale), f.Mailles.Select(m => m.Diagonale));
        Assert.Equal(face.Niveaux.Select(n => n.Hauteur), f.Niveaux.Select(n => n.Hauteur));
        projet.AppliquerGeneraux();
        Assert.Equal(GenerateurFacade.Generer(face).Elements.Count, GenerateurFacade.Generer(f).Elements.Count);
    }

    [Fact]
    public void Parametres_generaux_enregistres_une_seule_fois()
    {
        var projet = ProjetStructure.Nouveau();
        projet.Forme = FormeStructure.EnL;
        projet.AjusterFaces();
        projet.General.SecuriteExterieure = TypeSecurite.Lisses;

        string json = FichierProjet.VersJson(projet);
        var relu = FichierProjet.DepuisJson(json);

        Assert.Single(Regex.Matches(json, "\"securiteExterieure\""));
        Assert.Equal(2, relu.Faces.Count);
        Assert.All(relu.Faces, f => Assert.Equal(TypeSecurite.Lisses, f.Parametres.SecuriteExterieure));
    }

    [Fact]
    public void Lisse_de_securite_des_acces_ordinaire_sans_collier()
    {
        var s = GenerateurFacade.Generer(Standard(p => p.SecuriteInterieure = TypeSecurite.Lisses));
        Assert.DoesNotContain(s.Elements, e => e.Reference.StartsWith("HMLISSEC", StringComparison.Ordinal));
        Assert.DoesNotContain(s.Elements, e => e.Reference == "HMCOL49ROSACE");
    }

    [Fact]
    public void Diagonales_sur_les_mailles_cochees()
    {
        var projet = Standard();
        foreach (var m in projet.Mailles)
        {
            m.Diagonale = false;
        }

        projet.Mailles[2].Diagonale = true;
        projet.Mailles[4].Diagonale = true;
        var dia = GenerateurFacade.Generer(projet).Elements.Where(e => e.Famille == Famille.DiagonaleVerticale).ToList();

        Assert.Equal(new[] { 3, 5 }, dia.Select(e => e.NumeroMaille).Distinct().OrderBy(n => n));
        Assert.Equal(5, dia.Count(e => e.NumeroMaille == 3));   // toute la hauteur : 5 x 2 m
        Assert.All(dia.Where(e => e.NumeroMaille == 3), e => Assert.False(e.Inverse));
        Assert.All(dia.Where(e => e.NumeroMaille == 5), e => Assert.True(e.Inverse)); // sens alterné
    }

    [Fact]
    public void Console_seulement_aux_niveaux_et_mailles_coches()
    {
        var projet = Standard();
        var n4 = projet.Niveaux.Single(n => n.Hauteur == 4);
        n4.Console = "HMCON700E";
        projet.Mailles[1].DefinirConsole(4, true);
        var s = GenerateurFacade.Generer(projet);

        var consoles = s.Elements.Where(e => e.Reference == "HMCON700E").ToList();
        Assert.Equal(2, consoles.Count);                         // 2 files bordant la maille 2
        Assert.All(consoles, e => Assert.Equal(4, e.Origine.Z, 3));
    }

    [Fact]
    public void About_en_lisses_au_bout_d_une_console_sans_voisine()
    {
        var projet = Standard(p => p.SecuriteInterieure = TypeSecurite.Lisses);
        projet.Niveaux.Single(n => n.Hauteur == 4).Console = "HMCON700E";
        projet.Mailles[1].DefinirConsole(4, true);
        projet.Mailles[2].DefinirConsole(4, true);
        var s = GenerateurFacade.Generer(projet);

        // Abouts en travers de la console (axe Y, partant du bout de console), à 4,5 et 5 m.
        var abouts = s.Elements.Where(e => e.Axe == Axe.Y && Math.Abs(e.Origine.Y - 0.70) < 0.001 && e.Origine.Z > 4.1 && e.Origine.Z < 5.1).ToList();
        Assert.Equal(4, abouts.Count);                                         // lisse + sous-lisse aux deux bouts
        Assert.All(abouts, e => Assert.Equal("HMTRA070", e.Reference));
        Assert.Equal(new[] { 2.5, 7.5 }, abouts.Select(e => e.Origine.X).Distinct().OrderBy(x => x));
        Assert.Equal(2, abouts.Count(e => e.Inverse));                         // côté gauche en miroir
        Assert.DoesNotContain(s.Elements, e => e.Reference.StartsWith("HMGCA", StringComparison.Ordinal));
    }

    [Fact]
    public void About_de_console_en_bout_de_facade_selon_l_option()
    {
        var projet = Standard();
        projet.Niveaux.Single(n => n.Hauteur == 4).Console = "HMCON700E";
        projet.Mailles[0].DefinirConsole(4, true);

        bool AboutGaucheConsole(FaceEchafaudage p) => GenerateurFacade.Generer(p).Elements
            .Any(e => e.Axe == Axe.Y && Math.Abs(e.Origine.X) < 0.001 && Math.Abs(e.Origine.Y - 0.70) < 0.001 && e.Inverse);

        Assert.True(AboutGaucheConsole(projet));
        projet.Parametres.AboutGauche = false;
        Assert.False(AboutGaucheConsole(projet));
    }

    [Fact]
    public void Ancien_fichier_mds1_et_console_par_maille_converti()
    {
        // Fichier v1 : une seule façade, paramètres généraux et informations d'étude dans le nœud « facade ».
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new JsonStringEnumConverter() } };
        var facade = JsonSerializer.SerializeToNode(Standard(), options)!.AsObject();
        facade["chantier"] = "Ancien";
        facade["parametres"]!["securiteExterieure"] = "GardeCorpsMds1";
        facade["parametres"]!["bache"] = true;
        facade["parametres"]!["renforce"] = true;
        facade["mailles"]![0]!["console"] = "HMCON700E";
        var racine = new JsonObject { ["application"] = "MultiDEC", ["version"] = 1, ["facade"] = facade };

        var projet = FichierProjet.DepuisJson(racine.ToJsonString());
        var relu = projet.S1;

        Assert.Equal("Ancien", projet.Chantier);
        Assert.Equal(FormeStructure.Droite, projet.Forme);
        Assert.True(projet.General.Bache);
        Assert.Equal(TypeSecurite.GardeCorpsMds2, projet.General.SecuriteExterieure);
        Assert.Equal(TypeSecurite.GardeCorpsMds2, relu.Parametres.SecuriteExterieure);
        Assert.Equal(TypeTraverse.Renforce, relu.Parametres.Traverses);
        Assert.DoesNotContain("\"renforce\"", FichierProjet.VersJson(projet));
        Assert.All(relu.Niveaux.Where(n => n.Type == TypePlancher.Complet), n => Assert.Equal("HMCON700E", n.Console));
        Assert.Equal(new[] { 2.0, 4, 6, 8, 10 }, relu.Mailles[0].Consoles);
        Assert.Empty(relu.Mailles[1].Consoles);
        Assert.Null(relu.Mailles[0].ConsoleAncienFormat);
        Assert.True(relu.Mailles[0].Diagonale);
        Assert.All(FichierProjet.DepuisJson(FichierProjet.VersJson(projet)).S1.Mailles, m => Assert.Null(m.ConsoleAncienFormat));
    }

    [Theory]
    [InlineData(4.0, "Con4m")]
    [InlineData(4.5, "Con4,5m")]
    [InlineData(10.0, "Con10m")]
    public void Nom_des_colonnes_de_console(double hauteur, string nom) => Assert.Equal(nom, Niveau.NomColonne(hauteur));
}
