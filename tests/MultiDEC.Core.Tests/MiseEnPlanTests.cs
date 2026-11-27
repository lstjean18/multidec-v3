using System;
using System.Linq;
using MultiDEC.Core.Facade;
using MultiDEC.Core.Geometrie;
using MultiDEC.Core.MiseEnPlan;
using Xunit;

namespace MultiDEC.Core.Tests;

/// <summary>Mise en plan (demande de Laurent du 27/11/2026) : vues, échelles, cotes, cartouche, format A3.</summary>
public class MiseEnPlanTests
{
    private static ProjetStructure Droite(double longueur = 15, double hauteur = 6) =>
        ProjetStructure.Nouveau(face: new ParametresFacade { LongueurTotaleSouhaitee = longueur, LongueurMaillePreferee = 2.5, HauteurDernierPlancher = hauteur });

    private static PlanDeStructure Plan(ProjetStructure projet)
    {
        var s = GenerateurStructure.Generer(projet);
        var c = Cartouche.De(projet, s, "3.0.0.1", 1234.4, null, o => o.ToString()!);
        return GenerateurPlan.Generer(projet, s, c);
    }

    private static void Proche(double attendu, double obtenu, double tol = 1e-6) =>
        Assert.True(Math.Abs(attendu - obtenu) < tol, $"attendu {attendu}, obtenu {obtenu}");

    [Fact]
    public void Elevation_vue_de_l_exterieur_X_vers_la_droite_Z_vers_le_haut()
    {
        var p = Projection.Elevation(0);
        var a = p.Projeter(new Point3(3, -1, 2));
        Proche(3, a.U);
        Proche(2, a.V);
        // Observateur côté extérieur (Y négatif).
        Proche(-1, p.Vers.Y);
    }

    [Fact]
    public void Elevation_d_une_face_tournee_suit_la_face()
    {
        // Face tournée de 90° : la longueur de la face va vers +Y, l'extérieur vers +X.
        var p = Projection.Elevation(90);
        var a = p.Projeter(new Point3(0, 4, 1));
        Proche(4, a.U);
        Proche(1, a.V);
        Proche(1, p.Vers.X);
    }

    [Fact]
    public void Dessus_X_vers_la_droite_Y_vers_le_haut()
    {
        var p = Projection.Dessus();
        var a = p.Projeter(new Point3(2, -1, 7));
        Proche(2, a.U);
        Proche(-1, a.V);
    }

    [Fact]
    public void Profils_droit_et_gauche_sont_symetriques()
    {
        var g = Projection.About(0, droit: false);
        var d = Projection.About(0, droit: true);
        Proche(-1, g.Vers.X);
        Proche(1, d.Vers.X);
        var pt = new Point3(0, -1, 2);
        Proche(2, g.Projeter(pt).V);
        Proche(2, d.Projeter(pt).V);
        Proche(-g.Projeter(pt).U, d.Projeter(pt).U);
    }

    [Theory]
    [InlineData(20, 8, 380, 200, 75)]
    [InlineData(10, 4, 380, 200, 50)]
    [InlineData(60, 30, 380, 200, 200)]
    [InlineData(3, 2, 150, 100, 20)]
    [InlineData(3.2, 2, 150, 100, 25)]
    public void Echelle_normalisee_qui_tient(double l, double h, double place, double placeH, int attendu)
    {
        Assert.Equal(attendu, GenerateurPlan.EchelleQuiTient(new Rect2(0, 0, l, h), place, placeH));
    }

    [Fact]
    public void Facade_droite_pages_dessus_elevation_profils_isos()
    {
        var plan = Plan(Droite());
        var titres = plan.Pages.Select(p => p.Titre).ToList();
        Assert.Equal("Vue de dessus", titres[0]);
        Assert.Equal("Élévation S1", titres[1]);
        Assert.Equal("Vues de profil", titres[2]);
        Assert.Equal("Vues isométriques", titres[3]);
        Assert.Equal(Enumerable.Range(1, plan.Pages.Count), plan.Pages.Select(p => p.Numero));
        Assert.Equal(420, plan.Format.Largeur);
        Assert.Equal(297, plan.Format.Hauteur);
    }

    [Fact]
    public void Chaine_des_mailles_egale_la_longueur_de_la_face()
    {
        var projet = Droite();
        var plan = Plan(projet);
        var face = projet.S1;
        var vue = plan.Pages[1].Vues[0];
        var mailles = vue.Cotes.Where(c => c.DecalageMm == -8 && Math.Abs(c.Direction.U - 1) < 1e-9).ToList();
        Assert.Equal(face.Mailles.Count, mailles.Count);
        Proche(face.LongueurObtenue, mailles.Sum(c => c.Valeur), 1e-6);
        Assert.Contains(vue.Cotes, c => c.DecalageMm == -16 && Math.Abs(c.Valeur - face.LongueurObtenue) < 1e-6);
        Assert.All(mailles, c => Assert.Equal(Cote.Format(c.Valeur), c.Texte));
    }

    [Fact]
    public void Elevation_cote_les_niveaux_et_la_hauteur()
    {
        var projet = Droite(hauteur: 6);
        var plan = Plan(projet);
        var vue = plan.Pages[1].Vues[0];
        var verticales = vue.Cotes.Where(c => Math.Abs(c.Direction.V - 1) < 1e-9).ToList();
        var chaine = verticales.Where(c => c.DecalageMm == 8).ToList();
        var total = verticales.Single(c => c.DecalageMm == 16);
        Proche(total.Valeur, chaine.Sum(c => c.Valeur), 1e-6);
        Assert.True(total.Valeur >= 7, "hauteur avec garde-corps au-dessus du dernier plancher");
        Assert.Contains(vue.Reperes, r => r.Type == TypeRepere.Niveau && r.Texte == "+6,00");
    }

    [Fact]
    public void Amarrages_reportes_sur_l_elevation()
    {
        var projet = Droite(hauteur: 10);
        var s = GenerateurStructure.Generer(projet);
        var plan = GenerateurPlan.Generer(projet, s, Cartouche.De(projet, s, "3", null, null, o => o.ToString()!));
        var vue = plan.Pages[1].Vues[0];
        Assert.Equal(s.Amarrages.Amarrages.Count(a => a.NomFace == "S1"), vue.Reperes.Count(r => r.Type == TypeRepere.Amarrage));
        Assert.True(vue.Reperes.Count(r => r.Type == TypeRepere.Amarrage) > 0);
    }

    [Fact]
    public void Vues_placees_dans_la_zone_de_dessin_a_l_echelle_annoncee()
    {
        var plan = Plan(Droite(30, 12));
        var zone = plan.Format.ZoneDessin;
        foreach (var vue in plan.Pages.SelectMany(p => p.Vues))
        {
            Assert.True(vue.Cadre.UMin >= zone.UMin - 1e-6 && vue.Cadre.UMax <= zone.UMax + 1e-6, vue.Titre + " déborde en largeur");
            Assert.True(vue.Cadre.VMin >= zone.VMin - 1e-6 && vue.Cadre.VMax <= zone.VMax + 1e-6, vue.Titre + " déborde en hauteur");
            if (vue.Type != TypeVue.Iso)
            {
                Assert.Contains(vue.Echelle, GenerateurPlan.Echelles);
                Proche(1000.0 / vue.Echelle, vue.MmParMetre);
            }
        }
    }

    [Fact]
    public void Profils_de_la_meme_page_a_la_meme_echelle()
    {
        var plan = Plan(Droite());
        var profils = plan.Pages.Single(p => p.Titre == "Vues de profil").Vues;
        Assert.Equal(2, profils.Count);
        Assert.Equal(profils[0].Echelle, profils[1].Echelle);
    }

    [Fact]
    public void Structure_en_L_une_elevation_par_face_et_dessus_cote()
    {
        var projet = Droite();
        projet.Forme = FormeStructure.EnL;
        projet.AjusterFaces();
        var plan = Plan(projet);
        Assert.Equal(new[] { "Vue de dessus", "Élévation S1", "Élévation S2", "Vues de profil", "Vues isométriques" },
            plan.Pages.Take(5).Select(p => p.Titre));
        var dessus = plan.Pages[0].Vues[0];
        foreach (var f in projet.Faces)
        {
            Assert.Contains(dessus.Cotes, c => c.DecalageMm == -16 && Math.Abs(c.Valeur - f.LongueurObtenue) < 1e-6);
        }
    }

    [Fact]
    public void Acces_montre_en_detail_et_renvoye_sur_l_elevation()
    {
        var projet = Droite();
        var plan = Plan(projet);
        var details = plan.Pages.Where(p => p.Titre == "Détails").SelectMany(p => p.Vues).ToList();
        var acces = Assert.Single(details, v => v.Titre.StartsWith("Accès", StringComparison.Ordinal));
        Assert.Equal("A", acces.Lettre);
        Assert.Contains(plan.Pages[1].Vues[0].Reperes, r => r.Type == TypeRepere.Detail && r.Texte == "A");
    }

    [Fact]
    public void Cartouche_reprend_version_projet_et_faces()
    {
        var projet = Droite();
        projet.Client = "Client essai";
        projet.Chantier = "Chantier essai";
        var plan = Plan(projet);
        var c = plan.Cartouche;
        Assert.Equal("3.0.0.1", c.Version);
        Assert.Contains(c.Projet, kv => kv.Key == "Client" && kv.Value == "Client essai");
        Assert.Contains(c.Projet, kv => kv.Key == "Chantier" && kv.Value == "Chantier essai");
        Assert.Contains(c.Structure, kv => kv.Key == "Forme");
        Assert.Equal(2, c.Faces.Count);
        Assert.Equal("S1", c.Faces[1][0]);
        Assert.Equal("1234kg", new string(c.Poids.Where(char.IsLetterOrDigit).ToArray()));
    }
}
