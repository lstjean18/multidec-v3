using System;
using System.Linq;
using MultiDEC.Core.Facade;
using MultiDEC.Core.Facade.Regles;
using MultiDEC.Core.Geometrie;
using Xunit;

namespace MultiDEC.Core.Tests;

/// <summary>Remarques de Laurent du 25/11/2026 au soir (points 43 à 49).</summary>
public class Remarques43a49Tests
{
    private static ProjetStructure Droite(double longueur = 15, double hauteur = 6, int acces = 0) =>
        ProjetStructure.Nouveau(face: new ParametresFacade { LongueurTotaleSouhaitee = longueur, LongueurMaillePreferee = 2.5, HauteurDernierPlancher = hauteur, NombreAcces = acces });

    [Fact]
    public void Diagonales_sur_la_maille_avant_et_apres_une_poutre_meme_decochees()
    {
        var projet = Droite();
        foreach (var m in projet.S1.Mailles)
        {
            m.Diagonale = false;
        }

        projet.S1.Mailles[2].Poutre = 4;
        var s = GenerateurStructure.Generer(projet);
        Assert.DoesNotContain(s.Alertes, a => a.Contains("poutre"));
        var mailles = s.Elements.Where(e => e.Famille == Famille.DiagonaleVerticale && e.Face == Face.Exterieure && e.Axe == Axe.X)
            .Select(e => e.NumeroMaille).Distinct().OrderBy(n => n).ToList();
        Assert.Contains(2, mailles);
        Assert.Contains(5, mailles);
        Assert.DoesNotContain(1, mailles);
        Assert.DoesNotContain(6, mailles);
    }

    [Fact]
    public void Volumique_cote_expose_sans_maille_cochee_recoit_des_diagonales()
    {
        var projet = Droite(10, 4, 1);
        projet.Forme = FormeStructure.Volumique;
        projet.AjusterFaces();
        projet.Faces[1].Mailles.Add(new MailleDefinition(2.5));
        projet.Faces[1].Mailles.Add(new MailleDefinition(2.5));
        double l1 = projet.S1.LongueurObtenue;
        var s = GenerateurStructure.Generer(projet);

        // M2 dépasse M1 : son côté façade au-delà de M1 donne dans le vide (garde-corps) et reçoit des diagonales.
        Assert.Contains(s.Elements, e => e.NomFace == "M2" && e.Famille == Famille.DiagonaleVerticale && e.Axe == Axe.X
            && Math.Abs(e.Origine.Y + 0.70) < 0.1 && e.Origine.X > l1 - 0.01);

        // Bout droit de M1 (about face au vide) : diagonale en travers.
        Assert.Contains(s.Elements, e => e.NomFace == "M1" && e.Famille == Famille.DiagonaleVerticale && e.Axe == Axe.Y && Math.Abs(e.Origine.X - l1) < 0.1);
    }

    [Fact]
    public void Volumique_large_traverses_porteuses_renforcees_des_1_50_m()
    {
        var projet = Droite(10, 4, 0);
        projet.Forme = FormeStructure.Volumique;
        projet.AjusterFaces();
        projet.Faces[1].Parametres.LargeurMaille = 2.0;
        var s = GenerateurStructure.Generer(projet);
        var traverses = s.Elements.Where(e => e.NomFace == "M2" && e.Famille == Famille.Traverse && Math.Abs(e.Origine.Z - 4) < 1e-6).Select(e => e.Reference).Distinct().ToList();
        Assert.Equal(new[] { "HMLISR200" }, traverses);
    }

    private static ProjetStructure Roulante(double largeur, double longueur, double hauteur)
    {
        var projet = ProjetStructure.Nouveau(
            new ParametresGeneraux { Socle = "HMSOCROU" },
            new ParametresFacade { LongueurTotaleSouhaitee = longueur, LongueurMaillePreferee = Math.Min(longueur, 2.5), HauteurDernierPlancher = hauteur, LargeurMaille = largeur });
        return projet;
    }

    [Fact]
    public void Tour_roulante_a_plusieurs_mailles_refusee_avec_rappel_de_la_regle()
    {
        var projet = Roulante(1.5, 5.0, 4);
        Assert.True(projet.S1.Mailles.Count > 1);
        var ex = Assert.Throws<InvalidOperationException>(() => GenerateurStructure.Generer(projet));
        Assert.Contains("4 pieds", ex.Message);
        Assert.Contains(RegleTourRoulante.Rappel, ex.Message);
    }

    [Fact]
    public void Tour_roulante_trop_haute_pour_sa_largeur_refusee()
    {
        var projet = Roulante(1.05, 2.0, 3.0);
        Assert.Single(projet.S1.Mailles);
        var ex = Assert.Throws<InvalidOperationException>(() => GenerateurStructure.Generer(projet));
        Assert.Contains("maximum 2,00 m", ex.Message);
    }

    [Fact]
    public void Tour_roulante_trop_courte_pour_sa_largeur_refusee()
    {
        var projet = Roulante(2.0, 1.5, 4.0);
        Assert.Single(projet.S1.Mailles);
        var ex = Assert.Throws<InvalidOperationException>(() => GenerateurStructure.Generer(projet));
        Assert.Contains("minimum 2,00 m", ex.Message);
    }

    [Fact]
    public void Tour_roulante_conforme_sans_amarrage_et_protegee_cote_interieur()
    {
        var projet = Roulante(1.5, 2.0, 4.0);
        Assert.Single(projet.S1.Mailles);
        Assert.Empty(RegleTourRoulante.Ecarts(projet));
        var s = GenerateurStructure.Generer(projet);
        Assert.Empty(s.Amarrages.Amarrages);
        Assert.Contains(s.Elements, e => e.Famille == Famille.Socle && e.Reference.StartsWith("HMSOCROU", StringComparison.Ordinal));
        Assert.Contains(s.Elements, e => e.Famille == Famille.GardeCorps && Math.Abs(e.Origine.Y) < 0.1);
    }

    [Fact]
    public void Grutable_reserve_aux_structures_a_4_pieds()
    {
        var projet = Droite(10, 4);
        projet.S1.Parametres.Grutable = true;
        var ex = Assert.Throws<InvalidOperationException>(() => GenerateurStructure.Generer(projet));
        Assert.Contains("Grutable", ex.Message);
    }

    [Fact]
    public void Maille_d_acces_de_moins_de_1_50_m_refusee()
    {
        var projet = Droite(10, 4, 1);
        var acces = projet.S1.Mailles.First(m => m.Type == TypeMaille.Acces);
        Assert.True(acces.Longueur >= 1.5);
        acces.Longueur = 1.0;
        var ex = Assert.Throws<InvalidOperationException>(() => GenerateurStructure.Generer(projet));
        Assert.Contains("maille d'accès de 1,00 m", ex.Message);
        Assert.Contains(RegleMaillesAcces.Rappel, ex.Message);
    }

    [Fact]
    public void Proposition_avec_mailles_courtes_garde_des_acces_de_1_50_m()
    {
        var projet = ProjetStructure.Nouveau(face: new ParametresFacade { LongueurTotaleSouhaitee = 8, LongueurMaillePreferee = 1.0, HauteurDernierPlancher = 4, NombreAcces = 1 });
        var acces = Assert.Single(projet.S1.Mailles, m => m.Type == TypeMaille.Acces);
        Assert.Equal(1.5, acces.Longueur, 6);
        Assert.Empty(RegleMaillesAcces.Ecarts(projet));
    }

    [Fact]
    public void Volumique_chaque_maille_protegee_a_sa_diagonale()
    {
        // Point 52 (Laurent 25/11/2026) : chaque maille à garde-corps reçoit une diagonale sur cette face.
        var projet = Droite(10, 4, 1);
        projet.Forme = FormeStructure.Volumique;
        projet.AjusterFaces();
        foreach (var f in projet.Faces)
        {
            foreach (var m in f.Mailles)
            {
                m.Diagonale = false;
            }
        }

        var s = GenerateurStructure.Generer(projet);
        var m2 = projet.Faces[1];
        var avecDiago = s.Elements.Where(e => e.NomFace == "M2" && e.Famille == Famille.DiagonaleVerticale && e.Axe == Axe.X && Math.Abs(e.Origine.Y + 1.40) < 0.05)
            .Select(e => e.NumeroMaille).Distinct().OrderBy(n => n).ToList();
        Assert.Equal(Enumerable.Range(1, m2.Mailles.Count).ToList(), avecDiago);

        // Côté commun M1 / M2 : pas de diagonale.
        Assert.DoesNotContain(s.Elements, e => e.Famille == Famille.DiagonaleVerticale && e.Axe == Axe.X && Math.Abs(e.Origine.Y + 0.70) < 0.05);
    }

    [Fact]
    public void Volumique_pas_de_diagonale_en_travers_a_l_interieur()
    {
        // Laurent 25/11/2026 23h54 : diagonales seulement sur les faces extérieures (côté extérieur, côté mur, abouts).
        var projet = Droite(10, 4, 1);
        projet.General.SecuriteAbouts = TypeSecurite.Lisses;
        projet.Forme = FormeStructure.Volumique;
        projet.AjusterFaces();
        foreach (var f in projet.Faces)
        {
            f.Parametres.LargeurMaille = 2.0;
        }

        var s = GenerateurStructure.Generer(projet);
        Assert.Contains(s.Elements, e => e.Famille == Famille.DiagonaleVerticale && e.Axe == Axe.Y);
        foreach (var f in projet.Faces)
        {
            double l = f.LongueurObtenue;
            Assert.All(s.Elements.Where(e => e.NomFace == f.Nom && e.Famille == Famille.DiagonaleVerticale && e.Axe == Axe.Y),
                e => Assert.True(e.Origine.X < 0.1 || e.Origine.X > l - 0.1, $"{f.Nom} : diagonale en travers à x = {e.Origine.X}"));
        }
    }

    [Fact]
    public void About_de_plus_de_1_05_m_toujours_en_lisses()
    {
        // Point 51 : module volumique de 2,00 m de large, abouts MDS 2 demandés → lisses.
        var projet = Droite(10, 4, 0);
        projet.General.SecuriteAbouts = TypeSecurite.GardeCorpsMds2;
        projet.Forme = FormeStructure.Volumique;
        projet.AjusterFaces();
        projet.Faces[1].Parametres.LargeurMaille = 2.0;
        var s = GenerateurStructure.Generer(projet);
        Assert.DoesNotContain(s.Elements, e => e.NomFace == "M2" && e.Famille == Famille.GardeCorps && e.Axe == Axe.Y);
        Assert.Contains(s.Elements, e => e.NomFace == "M2" && e.Reference == "HMLIS200" && e.Axe == Axe.Y && Math.Abs(e.Origine.Z - 5.0) < 1e-6);
        Assert.Contains(s.Alertes, a => a.Contains("About de 2,00 m") || a.Contains("About de 2.00 m"));

        // M1 (0,70 m) garde ses cadres d'about.
        Assert.Contains(s.Elements, e => e.NomFace == "M1" && e.Famille == Famille.GardeCorps && e.Axe == Axe.Y);
    }

    [Fact]
    public void Damier_garde_corps_en_lisses_sur_les_mailles_a_lisses_porteuses()
    {
        var projet = Droite(10, 4, 0);
        projet.General.SecuriteExterieure = TypeSecurite.GardeCorpsMds2;
        projet.Forme = FormeStructure.Volumique;
        projet.AjusterFaces();
        projet.PlanchersDamier = true;

        var s = GenerateurStructure.Generer(projet);
        // Mailles à plateaux selon Y (lisses porteuses) : pas de cadre MDS sur leurs faces exposées.
        var selonY = s.Elements.Where(e => e.Famille == Famille.Plateau && e.Axe == Axe.Y).Select(e => (e.NomFace, e.NumeroMaille)).Distinct().ToList();
        Assert.NotEmpty(selonY);
        Assert.DoesNotContain(s.Elements, e => e.Famille == Famille.GardeCorps && e.Axe == Axe.X && selonY.Contains((e.NomFace, e.NumeroMaille)));
        Assert.Contains(s.Elements, e => e.Famille == Famille.GardeCorps && e.Axe == Axe.X && !selonY.Contains((e.NomFace, e.NumeroMaille)));
    }
}
