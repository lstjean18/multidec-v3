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
        Assert.Equal(12.0, projet.Faces[1].LongueurObtenue, 2);
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
    }

    [Fact]
    public void Structure_fermee_sans_about_et_refermee()
    {
        var projet = Projet(FormeStructure.Fermee);
        var s = GenerateurStructure.Generer(projet);

        Assert.DoesNotContain(s.Elements, e => e.Famille == Famille.GardeCorps && e.Axe == Axe.Y);
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
        Assert.NotEqual(s1, vm.Mailles.Count);

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
}
