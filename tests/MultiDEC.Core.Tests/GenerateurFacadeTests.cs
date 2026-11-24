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
    public void Pare_gravois_consoles_a_chaque_file_toles_et_lisses()
    {
        var face = Standard();
        face.Niveaux.Single(n => Math.Abs(n.Hauteur - 2) < 0.001).PareGravois = true;
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
        var am = s.Elements.Where(e => e.Famille == Famille.Amarrage).ToList();
        Assert.Equal(s.Amarrages.Amarrages.Count, am.Count);
        Assert.All(am, e => Assert.Equal("HMTUBAMA", e.Reference)); // 0,70 + 0,20 + 0,10 → tube 1,20 m
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
        var biblio = BibliothequeBlocs.DepuisFichier("blocs-EM-3D.txt");
        foreach (var socle in CatalogueSocles.Tous)
        {
            foreach (var modele in Enum.GetValues<ModeleEchafaudage>())
            {
                var face = Standard(p => { p.Modele = modele; p.Socle = socle.Reference; p.Grutable = true; p.Traverses = TypeTraverse.Renforce; p.TrappesAcier = true; p.LisseGcAccesNiveau0 = true; });
                face.Niveaux.First(n => n.Hauteur > 0).DiagoH = true;
                face.Niveaux.First(n => n.Hauteur > 0).PareGravois = true;
                face.Niveaux.Add(new Niveau(1.0, TypePlancher.Complet)); // portillon d'about
                var manquantes = biblio.Manquantes(GenerateurFacade.Generer(face).Elements.Select(e => e.Reference))
                                       .Where(r => !r.StartsWith("HMTUBAMA", StringComparison.Ordinal)).ToList();
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
        Assert.Equal(8, s.Elements.Count(e => e.Reference == "HMDIA1502" && e.Axe == Axe.Y)); // 2 bouts x 4 niveaux
        Assert.Equal(5, s.Elements.Count(e => e.Famille == Famille.Lisse && e.Axe == Axe.X && Math.Abs(e.Origine.Y - y) < 1e-6)); // 0, 2, 4, 6, 8
    }

    [Fact]
    public void Pignon_sur_maille_d_acces_planchers_tous_les_2_m_et_abouts()
    {
        var face = Standard(); // accès en maille 4 (index 3), dernier plancher 10 m
        int acces = face.Mailles.FindIndex(m => m.Type == TypeMaille.Acces);
        face.Mailles[acces].Pignon = 4;
        face.Mailles[0].Pignon = 4; // maille de travail : ignoré (v2 : pignon seulement sur un accès)
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
    public void Sous_mailles_references_presentes_dans_la_bibliotheque()
    {
        var biblio = BibliothequeBlocs.DepuisFichier("blocs-EM-3D.txt");
        foreach (bool acier in new[] { false, true })
        {
            var face = Standard(p => p.TrappesAcier = acier);
            face.Mailles[0].Type = TypeMaille.AccesRapporte;
            face.Mailles[1].Type = TypeMaille.Appro;
            face.Mailles[2].Type = TypeMaille.Recette;
            face.Mailles[2].ProfondeurRecette = 2.5;
            face.Mailles[5].Stabilisation = 2.0;
            foreach (var n in face.Niveaux)
            {
                n.Recette = true;
            }

            var manquantes = biblio.Manquantes(GenerateurFacade.Generer(face).Elements.Select(e => e.Reference))
                                   .Where(r => !r.StartsWith("HMTUBAMA", StringComparison.Ordinal)).ToList();
            Assert.True(manquantes.Count == 0, $"blocs manquants {string.Join(", ", manquantes)}");
        }
    }

    [Fact]
    public void References_generees_presentes_dans_la_bibliotheque()
    {
        var biblio = BibliothequeBlocs.DepuisFichier("blocs-EM-3D.txt");
        Assert.True(biblio.Nombre > 500);

        foreach (var modele in Enum.GetValues<ModeleEchafaudage>())
        {
            var s = GenerateurFacade.Generer(Standard(p => { p.Modele = modele; p.SecuriteInterieure = TypeSecurite.GardeCorpsMds2; }));
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
