using MultiDEC.App.ViewModels;
using MultiDEC.Core.Facade;
using MultiDEC.Core.Geometrie;
using Xunit;

namespace MultiDEC.Core.Tests;

/// <summary>Structure à plusieurs faces (droite, en L, en U, fermée) : onglets S1…S4 de la nouvelle interface.</summary>
public class StructureTests
{
    private static ProjetStructure Projet(FormeStructure forme, double longueur = 12)
    {
        var projet = ProjetStructure.Nouveau(face: new ParametresFacade { LongueurTotaleSouhaitee = longueur, HauteurDernierPlancher = 4 });
        projet.Forme = forme;
        projet.AjusterFaces();
        if (forme == FormeStructure.Fermee)
        {
            projet.ProposerFace(0); // S1 commence alors par le carré d'angle (raccord avec S4)
        }

        return projet;
    }

    [Theory]
    [InlineData(FormeStructure.Droite, 1)]
    [InlineData(FormeStructure.EnL, 2)]
    [InlineData(FormeStructure.EnU, 3)]
    [InlineData(FormeStructure.Fermee, 4)]
    public void La_forme_fixe_le_nombre_de_faces(FormeStructure forme, int faces)
    {
        var projet = Projet(forme);
        Assert.Equal(faces, projet.Faces.Count);
        Assert.Equal(Enumerable.Range(1, faces).Select(i => "S" + i), projet.Faces.Select(f => f.Nom));
    }

    [Fact]
    public void Les_faces_suivantes_commencent_par_le_carre_d_angle()
    {
        var projet = Projet(FormeStructure.EnL);
        Assert.Equal(0.70, projet.Faces[1].Mailles[0].Longueur);
        Assert.Equal(12.70, projet.Faces[1].LongueurObtenue, 2); // 12 m saisis + carré d'angle (Laurent, 24/11/2026)
        Assert.False(projet.CommenceParCarreDAngle(0));
        Assert.True(projet.CommenceParCarreDAngle(1));
    }

    [Fact]
    public void Face_S2_tournee_de_90_degres_au_bout_de_S1()
    {
        var projet = Projet(FormeStructure.EnL);
        var poses = GenerateurStructure.Poses(projet);
        double l1 = projet.S1.LongueurObtenue;

        Assert.Equal(0, poses[0].Angle);
        Assert.Equal(90, poses[1].Angle);
        Assert.Equal(new Point3(l1, -0.70, 0), poses[1].Origine);

        var s = GenerateurStructure.Generer(projet);
        Assert.All(s.Elements.Where(e => e.NomFace == "S2"), e => Assert.Equal(90, e.AngleFace));

        // Les poteaux de S2 sont dans le carré d'angle et au-delà (X ≥ L1) ; S1 reste à X ≤ L1.
        Assert.All(s.Elements.Where(e => e.NomFace == "S2" && e.Famille == Famille.Poteau), e => Assert.True(e.Origine.X >= l1 - 1e-6));
    }

    [Fact]
    public void Poteaux_d_angle_communs_et_pas_d_about_au_raccord()
    {
        var projet = Projet(FormeStructure.EnL);
        var s = GenerateurStructure.Generer(projet);
        double l1 = projet.S1.LongueurObtenue;

        // Une seule pile de poteaux à chaque point (X, Y).
        var piles = s.Elements.Where(e => e.Famille == Famille.Socle)
            .GroupBy(e => (Math.Round(e.Origine.X, 3), Math.Round(e.Origine.Y, 3)));
        Assert.All(piles, g => Assert.Single(g));

        // Pas d'about au bout de S1 (raccord), about conservé au début de S1 et au bout de S2.
        bool AboutEn(string face, double x) => s.Elements.Any(e =>
            e.NomFace == face && e.Famille == Famille.GardeCorps && e.Axe == Axe.Y && Cotes.Egal(e.Origine.X, x));
        Assert.True(AboutEn("S1", 0));
        Assert.False(AboutEn("S1", l1));
        Assert.Contains(s.Elements, e => e.NomFace == "S2" && e.Famille == Famille.GardeCorps && e.Axe == Axe.Y);

        // Côté extérieur du carré d'angle de S2 (dans le prolongement du garde-corps extérieur de S1) : about (Laurent 24/11/2026).
        Assert.Contains(s.Elements, e => e.NomFace == "S2" && e.NumeroMaille == 1 && e.Famille == Famille.GardeCorps && e.Axe == Axe.Y
                                         && Cotes.Egal(e.Origine.Y, -0.70) && Cotes.Egal(e.Origine.X, l1));

        // Poteau intérieur du bout de S1 (angle intérieur) : aucun garde-corps n'y est accroché, il s'arrête au plancher.
        double sommet = s.Elements.Where(e => e.Famille == Famille.Poteau && Cotes.Egal(e.Origine.X, l1) && Cotes.Egal(e.Origine.Y, 0))
                                  .Max(e => e.Origine.Z);
        Assert.True(sommet < projet.S1.Parametres.HauteurDernierPlancher - 1.0 + 0.1);
    }

    [Fact]
    public void Structure_en_L_inversee_tourne_a_droite_et_permute_cote_mur_et_cote_vide()
    {
        var projet = Projet(FormeStructure.EnL);
        projet.Inversee = true;
        var poses = GenerateurStructure.Poses(projet);
        double l1 = projet.S1.LongueurObtenue;

        // S2 tourne vers la droite (-90°), sa file intérieure contre le mur en retour (X = L1 + largeur).
        Assert.Equal(270, poses[1].Angle);
        Assert.Equal(new Point3(l1 + 0.70, 0, 0), poses[1].Origine);

        var s = GenerateurStructure.Generer(projet);
        Assert.All(s.Elements.Where(e => e.NomFace == "S2" && e.Famille == Famille.Poteau), e => Assert.True(e.Origine.Y <= 1e-6 && e.Origine.X >= l1 - 1e-6));

        // Une seule pile de poteaux à chaque point : les files du carré d'angle tombent sur celles du bout de S1.
        var piles = s.Elements.Where(e => e.Famille == Famille.Socle).GroupBy(e => (Math.Round(e.Origine.X, 3), Math.Round(e.Origine.Y, 3)));
        Assert.All(piles, g => Assert.Single(g));
        Assert.Contains(piles, g => Cotes.Egal(g.Key.Item1, l1) && Cotes.Egal(g.Key.Item2, 0));

        // Carré d'angle : pas d'about au départ (mur), pas de garde-corps ni de diagonale côté extérieur (passage vers S1).
        Assert.DoesNotContain(s.Elements, e => e.NomFace == "S2" && e.NumeroMaille == 1 && e.Famille == Famille.GardeCorps);
        Assert.DoesNotContain(s.Elements, e => e.NomFace == "S2" && e.NumeroMaille == 1 && e.Famille == Famille.DiagonaleVerticale);

        // Garde-corps extérieurs de S2 côté vide, c'est-à-dire vers l'intérieur de l'angle (X < L1 + largeur).
        Assert.Contains(s.Elements, e => e.NomFace == "S2" && e.NumeroMaille == 2 && e.Famille == Famille.GardeCorps && e.Face == Face.Exterieure && Cotes.Egal(e.Origine.X, l1));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Maille_de_jonction_lisses_de_pied_sur_ses_4_cotes(bool inversee)
    {
        // Laurent 24/11/2026 : toujours des lisses de pied sur les 4 côtés de la maille de jonction (option décochée).
        var projet = Projet(FormeStructure.EnL);
        projet.Inversee = inversee;
        Assert.False(projet.S1.Parametres.LissesEnPied);
        var s = GenerateurStructure.Generer(projet);
        double l1 = projet.S1.LongueurObtenue;

        // Le côté posé sur la dernière file de S1 garde la traverse de pied de cette file ; l'autre côté long reçoit une lisse.
        var lisse = Assert.Single(s.Elements, e => e.NomFace == "S2" && e.NumeroMaille == 1 && e.Famille == Famille.Lisse && Math.Abs(e.Origine.Z) < 1e-6);
        Assert.Equal(inversee ? Face.Interieure : Face.Exterieure, lisse.Face);
        Assert.Single(s.Elements, e => e.NomFace == "S1" && e.Famille == Famille.Traverse && Math.Abs(e.Origine.Z) < 1e-6 && Cotes.Egal(e.Origine.X, l1));
    }

    [Fact]
    public void Structure_fermee_inversee_se_referme_dans_la_cour()
    {
        var projet = Projet(FormeStructure.Fermee);
        projet.Inversee = true;
        var poses = GenerateurStructure.Poses(projet);
        Assert.Equal(new[] { 0.0, 270, 180, 90 }, poses.Select(p => p.Angle));

        var s = GenerateurStructure.Generer(projet);
        Assert.DoesNotContain(s.Alertes, a => a.Contains("ne se referme pas"));
        var piles = s.Elements.Where(e => e.Famille == Famille.Socle).GroupBy(e => (Math.Round(e.Origine.X, 3), Math.Round(e.Origine.Y, 3)));
        Assert.All(piles, g => Assert.Single(g));
        Assert.DoesNotContain(s.Elements, e => e.Famille == Famille.GardeCorps && e.Axe == Axe.Y); // aucun about : tout est raccordé ou contre le mur

        // Refermée : autant de piles que la structure fermée normale (les quatre angles sont communs).
        var normale = Projet(FormeStructure.Fermee);
        int pilesNormales = GenerateurStructure.Generer(normale).Elements.Where(e => e.Famille == Famille.Socle)
            .Select(e => (Math.Round(e.Origine.X, 3), Math.Round(e.Origine.Y, 3))).Distinct().Count();
        Assert.Equal(pilesNormales, piles.Count());

        // Sans effet sur une structure droite.
        var droite = Projet(FormeStructure.Droite);
        droite.Inversee = true;
        Assert.Equal(0, GenerateurStructure.Poses(droite)[0].Angle);
        Assert.False(droite.FacesInversees);
    }

    [Fact]
    public void Premier_plancher_different_entre_faces_refuse()
    {
        var projet = Projet(FormeStructure.EnL);
        projet.Faces[1].Niveaux.RemoveAll(n => n.Hauteur < 2.5 && n.Hauteur > 0);
        var ex = Assert.Throws<InvalidOperationException>(() => GenerateurStructure.Generer(projet));
        Assert.Contains("premier plancher", ex.Message);
    }

    [Fact]
    public void Structure_fermee_sans_about_et_refermee()
    {
        var projet = Projet(FormeStructure.Fermee);
        var s = GenerateurStructure.Generer(projet);

        // Seuls les côtés extérieurs des carrés d'angle (1re maille de chaque face) ont un about.
        Assert.All(s.Elements.Where(e => e.Famille == Famille.GardeCorps && e.Axe == Axe.Y), e => Assert.Equal(1, e.NumeroMaille));
        Assert.DoesNotContain(s.Alertes, a => a.Contains("ne se referme pas"));

        projet.Faces[2].Mailles.Add(new MailleDefinition(1.5));
        Assert.Contains(GenerateurStructure.Generer(projet).Alertes, a => a.Contains("ne se referme pas"));
    }

    [Fact]
    public void Structure_droite_identique_au_generateur_d_une_face()
    {
        var projet = Projet(FormeStructure.Droite);
        var face = GenerateurFacade.Generer(projet.S1);
        var structure = GenerateurStructure.Generer(projet);
        Assert.Equal(face.Elements.Select(e => e.ToString()), structure.Elements.Select(e => e.ToString()));
        Assert.Equal(face.Amarrages.Amarrages.Count, structure.Amarrages.Amarrages.Count);
    }

    [Fact]
    public void Parametres_generaux_appliques_a_toutes_les_faces()
    {
        var projet = Projet(FormeStructure.EnU);
        projet.General.Modele = ModeleEchafaudage.MultiReto;
        projet.General.SecuriteExterieure = TypeSecurite.Lisses;
        projet.AppliquerGeneraux();
        Assert.All(projet.Faces, f =>
        {
            Assert.Equal(ModeleEchafaudage.MultiReto, f.Parametres.Modele);
            Assert.Equal(TypeSecurite.Lisses, f.Parametres.SecuriteExterieure);
        });
    }

    [Fact]
    public void Copier_depuis_S1_garde_le_carre_d_angle()
    {
        var projet = Projet(FormeStructure.EnL);
        projet.S1.Parametres.LissesEnPied = true;
        projet.CopierDepuisS1(1);

        Assert.True(projet.Faces[1].Parametres.LissesEnPied);
        Assert.Equal("S2", projet.Faces[1].Nom);
        Assert.Equal(projet.S1.Mailles.Count + 1, projet.Faces[1].Mailles.Count);
        Assert.Equal(0.70, projet.Faces[1].Mailles[0].Longueur);
    }

    [Fact]
    public void Ecran_change_de_face_sans_perdre_les_grilles()
    {
        var vm = new FacadeViewModel();
        vm.Forme = FormeStructure.EnL;
        Assert.Equal(2, vm.Faces.Count);

        vm.Mailles.Add(new MailleDefinition(1.5));
        int s1 = vm.Mailles.Count;
        vm.IndexFaceActive = 1;
        Assert.Equal("S2", vm.NomFaceActive);
        Assert.Equal("S1", vm.Mailles[0].Jonction);
        Assert.Equal(vm.Projet.Faces[1].Mailles.Count, vm.Mailles.Count);

        vm.IndexFaceActive = 0;
        Assert.Equal(s1, vm.Mailles.Count);
        Assert.Equal(string.Empty, vm.Mailles[0].Jonction);

        var s = vm.Generer();
        Assert.Contains(s.Elements, e => e.NomFace == "S2");
        Assert.Equal(s1, vm.Projet.S1.Mailles.Count);
    }

    [Fact]
    public void Ecran_forme_fermee_puis_droite()
    {
        var vm = new FacadeViewModel();
        vm.Forme = FormeStructure.Fermee;
        vm.IndexFaceActive = 3;
        vm.Forme = FormeStructure.Droite;
        Assert.Single(vm.Faces);
        Assert.Equal(0, vm.IndexFaceActive);
        Assert.Equal("S1", vm.NomFaceActive);
    }

    [Fact]
    public void Ajouter_inserer_supprimer_des_mailles()
    {
        var vm = new FacadeViewModel();
        int n = vm.Mailles.Count;
        vm.Niveaux.First(x => x.Hauteur == 4).Console = "HMCON700E";

        Assert.Equal(n, vm.AjouterMaille(-1));
        Assert.Equal(n + 1, vm.Mailles.Count);
        Assert.Equal(new[] { 4.0 }, vm.Mailles[n].Consoles); // consoles comme la voisine

        Assert.Equal(0, vm.InsererMailleAvant(0));
        Assert.False(vm.Mailles[0].Diagonale);
        Assert.Equal(2.5, vm.Mailles[0].Longueur);

        vm.SupprimerMaille(0);
        vm.SupprimerMaille(-1);
        Assert.Equal(n + 1, vm.Mailles.Count);
        Assert.Contains("mailles", vm.ResumeFace);
    }

    [Fact]
    public void Libelles_en_francais()
    {
        Assert.Equal("MDS 2", Libelles.De(TypeSecurite.GardeCorpsMds2));
        Assert.Equal("Multi-SYSTEM", Libelles.De(ModeleEchafaudage.MultiSystem));
        Assert.Equal("Accès seul", Libelles.De(TypePlancher.AccesSeul));
        Assert.Equal("Standard NF", Libelles.De(GammePlateaux.StandardNF));
        Assert.Equal("En L", Libelles.De(FormeStructure.EnL));
    }

    [Fact]
    public void Volumique_modules_cote_a_cote_poteaux_communs_sans_protection_entre_modules()
    {
        var projet = ProjetStructure.Nouveau(face: new ParametresFacade { LongueurTotaleSouhaitee = 10, HauteurDernierPlancher = 4, NombreAcces = 0 });
        projet.Forme = FormeStructure.Volumique;
        projet.NombreModules = 3;
        projet.AjusterFaces();
        Assert.Equal(new[] { "M1", "M2", "M3" }, projet.Faces.Select(f => f.Nom));
        projet.Faces[1].Parametres.LargeurMaille = 1.05;
        Assert.All(projet.Faces, f => Assert.Equal(projet.S1.Mailles.Select(m => m.Longueur), f.Mailles.Select(m => m.Longueur)));
        Assert.False(projet.CommenceParCarreDAngle(1));

        var s = GenerateurStructure.Generer(projet);
        int files = projet.S1.Mailles.Count + 1;
        var lignes = new[] { 0.0, -0.70, -1.75, -2.45 };
        foreach (double y in lignes)
        {
            Assert.Equal(files, s.Elements.Count(e => e.Famille == Famille.Socle && Math.Abs(e.Origine.Y - y) < 1e-6));
        }

        Assert.Equal(4 * files, s.Elements.Count(e => e.Famille == Famille.Socle));

        // Garde-corps de façade seulement sur les côtés extérieurs du volume (pas sur les lignes communes).
        bool gc(double y) => s.Elements.Any(e => e.Famille == Famille.GardeCorps && e.Axe == Axe.X && Math.Abs(e.Origine.Y - y) < 1e-6);
        Assert.True(gc(-2.45));
        Assert.False(gc(-0.70));
        Assert.False(gc(-1.75));

        // Abouts à chaque module, aux deux bouts ; amarrages du seul module M1.
        Assert.Equal(3, s.Elements.Count(e => e.Axe == Axe.Y && e.Famille == Famille.GardeCorps && Math.Abs(e.Origine.X) < 1e-6 && Math.Abs(e.Origine.Z - 4) < 1e-6));
        Assert.All(s.Amarrages.Amarrages, a => Assert.Equal("M1", a.NomFace));
        Assert.DoesNotContain(s.Alertes, a => a.Contains("Aucun garde-corps"));

        // Diagonales en travers dans le module de 1,05 m ? non (seulement au-delà de 1,05 m).
        Assert.DoesNotContain(s.Elements, e => e.Reference == "HMDIA1052");
    }

    [Fact]
    public void Volumique_planchers_en_damier_decales_d_un_module_a_l_autre()
    {
        var projet = ProjetStructure.Nouveau(face: new ParametresFacade { LongueurTotaleSouhaitee = 10, HauteurDernierPlancher = 4, NombreAcces = 0 });
        projet.Forme = FormeStructure.Volumique;
        projet.PlanchersDamier = true;
        projet.AjusterFaces();
        var s = GenerateurStructure.Generer(projet);

        bool plancher(string face, int maille) => s.Elements.Any(e => e.NomFace == face && e.Famille == Famille.Plateau && e.NumeroMaille == maille && Math.Abs(e.Origine.Z - 4) < 1e-6);
        Assert.True(plancher("M1", 1));
        Assert.False(plancher("M1", 2));
        Assert.False(plancher("M2", 1));
        Assert.True(plancher("M2", 2));
    }

    [Fact]
    public void Volumique_M2_plus_long_garde_corps_cote_vide_et_pas_de_diagonale_entre_modules()
    {
        var projet = ProjetStructure.Nouveau(face: new ParametresFacade { LongueurTotaleSouhaitee = 10, HauteurDernierPlancher = 4, NombreAcces = 1 });
        projet.Forme = FormeStructure.Volumique;
        projet.AjusterFaces();
        Assert.Contains(projet.S1.Mailles, m => m.Type == TypeMaille.Acces);
        Assert.DoesNotContain(projet.Faces[1].Mailles, m => m.Type == TypeMaille.Acces); // accès proposés sur M1 seulement
        Assert.Equal(0, projet.Faces[1].Parametres.NombreAcces);

        // M2 prolongé de deux mailles au-delà de M1 (L1 = 10 m).
        projet.Faces[1].Mailles.Add(new MailleDefinition(2.5));
        projet.Faces[1].Mailles.Add(new MailleDefinition(2.5));
        double l1 = projet.S1.LongueurObtenue;
        var s = GenerateurStructure.Generer(projet);
        Assert.DoesNotContain(s.Alertes, a => a.Contains("ne tombent pas"));

        // Côté commun (y = -0,70) : pas de garde-corps ni de diagonale sur la longueur de M1 ; au-delà, garde-corps côté vide.
        bool Sur(ElementPlace e, double y) => Math.Abs(e.Origine.Y - y) < 0.1;
        Assert.DoesNotContain(s.Elements, e => e.Famille == Famille.GardeCorps && e.Axe == Axe.X && Sur(e, -0.70) && e.Origine.X < l1 - 0.01);
        Assert.Contains(s.Elements, e => e.Famille == Famille.GardeCorps && e.Axe == Axe.X && Sur(e, -0.70) && e.Origine.X > l1 - 0.01 && e.NomFace == "M2");
        Assert.DoesNotContain(s.Elements, e => e.Famille == Famille.DiagonaleVerticale && e.Axe == Axe.X && Sur(e, -0.70) && e.Origine.X < l1 - 0.01);

        // M1 plus court : about à son bout droit (côté vide).
        Assert.Contains(s.Elements, e => e.NomFace == "M1" && e.Famille == Famille.GardeCorps && e.Axe == Axe.Y && Math.Abs(e.Origine.X - l1) < 1e-6);
    }

    [Fact]
    public void Volumique_largeur_2_m_et_ajout_de_modules()
    {
        var vm = new FacadeViewModel();
        vm.Forme = FormeStructure.Volumique;
        Assert.Contains(2.00, vm.LargeursProposees);
        Assert.Contains(3.00, vm.LargeursProposees);
        vm.AjouterModule.Execute(null);
        vm.AjouterModule.Execute(null);
        Assert.Equal(4, vm.Faces.Count);
        Assert.Equal("M4", vm.NomFaceActive);
        vm.Parametres.LargeurMaille = 2.0;
        var s = vm.Generer();
        Assert.Contains(s.Elements, e => e.NomFace == "M4" && e.Reference == "HMDIA2002"); // diagonales en travers > 1,05 m

        var biblio = MultiDEC.Core.Catalogue.BibliothequeBlocs.DepuisFichier("blocs-3D-echafaudage.txt");
        foreach (double l in new[] { 1.5, 2.0, 2.5, 3.0 })
        {
            vm.Parametres.LargeurMaille = l;
            var manquantes = biblio.Manquantes(vm.Generer().Elements.Select(e => e.Reference))
                                   .Where(r => !r.StartsWith("HMTUBAMA", StringComparison.Ordinal)).ToList();
            Assert.True(manquantes.Count == 0, $"largeur {l} : blocs manquants {string.Join(", ", manquantes)}");
        }

        vm.Forme = FormeStructure.EnL;
        Assert.DoesNotContain(2.00, vm.LargeursProposees);
    }

    [Fact]
    public void Facade_droite_a_la_distance_de_la_file_interieure()
    {
        var projet = Projet(FormeStructure.Droite);
        var f = projet.S1;
        f.Parametres.DistanceFacade = 0.30;
        var mur = Assert.Single(MursFacade.Calculer(projet));
        Assert.Equal(new Point3(0, 0.30, 0), mur.Debut);
        Assert.Equal(new Point3(f.LongueurObtenue, 0.30, 0), mur.Fin);
        Assert.Equal(f.HauteurTotale, mur.Hauteur);
    }

    [Fact]
    public void Facade_en_L_murs_raccordes_dans_l_angle_sortant()
    {
        var projet = Projet(FormeStructure.EnL);
        foreach (var f in projet.Faces)
        {
            f.Parametres.DistanceFacade = 0.20;
        }

        var murs = MursFacade.Calculer(projet);
        Assert.Equal(2, murs.Count);
        double l1 = projet.S1.LongueurObtenue;
        Assert.Equal(new Point3(l1 - 0.20, 0.20, 0), murs[0].Fin);
        Assert.Equal(murs[0].Fin, murs[1].Debut);
        Assert.Equal(projet.Faces[1].LongueurObtenue - projet.S1.Parametres.LargeurMaille - 0.20, murs[1].Longueur, 3);
    }

    [Theory]
    [InlineData(FormeStructure.EnU, false)]
    [InlineData(FormeStructure.Fermee, false)]
    [InlineData(FormeStructure.EnL, true)]
    [InlineData(FormeStructure.EnU, true)]
    public void Facade_les_murs_se_rejoignent_aux_angles(FormeStructure forme, bool inversee)
    {
        var projet = Projet(forme);
        projet.Inversee = inversee;
        var murs = MursFacade.Calculer(projet);
        Assert.Equal(projet.Faces.Count, murs.Count);
        for (int i = 1; i < murs.Count; i++)
        {
            Assert.Equal(murs[i - 1].Fin, murs[i].Debut);
        }

        if (forme == FormeStructure.Fermee)
        {
            Assert.Equal(murs[3].Fin, murs[0].Debut);
        }

        // Angle rentrant (inversée) : le mur de S1 dépasse la face ; angle sortant : il s'arrête avant.
        double l1 = projet.S1.LongueurObtenue;
        Assert.True(inversee ? murs[0].Longueur > l1 : murs[0].Longueur < l1);
    }
}
