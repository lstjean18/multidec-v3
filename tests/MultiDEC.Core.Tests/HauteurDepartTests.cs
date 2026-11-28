using MultiDEC.Core.Facade;
using MultiDEC.Core.Geometrie;
using Xunit;

namespace MultiDEC.Core.Tests;

/// <summary>Colonne « Départ » : hauteur du sol sous une maille (sol en pente, Laurent 24/11/2026).</summary>
public class HauteurDepartTests
{
    private static FaceEchafaudage Face()
    {
        var p = new ParametresFacade
        {
            Modele = ModeleEchafaudage.MultiSystem,
            LargeurMaille = 0.70,
            LongueurTotaleSouhaitee = 7.5,
            LongueurMaillePreferee = 2.5,
            NombreAcces = 0,
            HauteurDernierPlancher = 6,
            LissesEnPied = true,
        };
        return ProposeurStructure.NouveauProjet(p);
    }

    [Fact]
    public void Maille_en_hauteur_part_de_son_depart()
    {
        var f = Face();
        Assert.Equal(3, f.Mailles.Count);
        f.Mailles[2].HauteurDepart = 1.5;
        var s = GenerateurFacade.Generer(f);
        double x2 = f.AbscissesFiles()[2], x3 = f.AbscissesFiles()[3];

        // Dernière file (seulement la maille 3) : socle à 1,50 m, rien dessous.
        var file3 = s.Elements.Where(e => Cotes.Egal(e.Origine.X, x3) && (e.Famille == Famille.Poteau || e.Famille == Famille.Socle)).ToList();
        Assert.NotEmpty(file3);
        Assert.True(file3.Min(e => e.Origine.Z) >= 1.5 - 0.3);
        Assert.InRange(file3.Where(e => e.Famille == Famille.Poteau).Min(e => e.Origine.Z), 1.5 - Cotes.Tolerance, 1.6); // MS : poteau sur embase

        // File partagée avec la maille 2 (départ 0) : poteaux depuis le sol.
        Assert.Contains(s.Elements, e => Cotes.Egal(e.Origine.X, x2) && e.Famille == Famille.Poteau && e.Origine.Z < 0.1);

        // Maille 3 : rien sous 1,50 m, lisses de pied à 1,50 m, pas de plancher à 0.
        Assert.DoesNotContain(s.Elements, e => e.NumeroMaille == 3 && e.Origine.Z < 1.5 - Cotes.Tolerance);
        Assert.Contains(s.Elements, e => e.NumeroMaille == 3 && e.Famille == Famille.Lisse && Cotes.Egal(e.Origine.Z, 1.5));
        Assert.Contains(s.Elements, e => e.NumeroMaille == 3 && e.Famille == Famille.Plateau && Cotes.Egal(e.Origine.Z, 2.0));

        // Traverse de pied de la dernière file à 1,50 m (pas de niveau à cette cote).
        Assert.Contains(s.Elements, e => Cotes.Egal(e.Origine.X, x3) && e.Famille == Famille.Traverse && Cotes.Egal(e.Origine.Z, 1.5));
        Assert.DoesNotContain(s.Elements, e => Cotes.Egal(e.Origine.X, x3) && e.Famille == Famille.Traverse && e.Origine.Z < 1.5 - Cotes.Tolerance);
    }

    [Fact]
    public void Diagonales_depuis_le_depart()
    {
        var f = Face();
        f.Mailles[0].HauteurDepart = 2.0;
        f.Mailles[0].Diagonale = true;
        var s = GenerateurFacade.Generer(f);
        var dia = s.Elements.Where(e => e.NumeroMaille == 1 && e.Famille == Famille.DiagonaleVerticale).ToList();
        Assert.NotEmpty(dia);
        Assert.Equal(2.0, dia.Min(e => e.Origine.Z), 3);
    }

    [Theory]
    [InlineData(0.75, "multiple de 0,50")]
    [InlineData(6.0, "0,50 m sous le dernier plancher")]
    public void Depart_invalide_ignore_avec_alerte(double depart, string message)
    {
        var f = Face();
        f.Mailles[1].HauteurDepart = depart;
        var s = GenerateurFacade.Generer(f);
        Assert.Contains(s.Alertes, a => a.Contains("hauteur de départ") && a.Contains(message));
        Assert.Contains(s.Elements, e => e.NumeroMaille == 2 && e.Famille == Famille.Lisse && Cotes.Egal(e.Origine.Z, 0));
    }

    [Fact]
    public void Depart_enregistre_et_relu()
    {
        var f = Face();
        f.Mailles[1].HauteurDepart = 1.0;
        var projet = ProjetStructure.Nouveau();
        projet.Faces[0] = f;
        var relu = MultiDEC.Core.Persistance.FichierProjet.DepuisJson(MultiDEC.Core.Persistance.FichierProjet.VersJson(projet));
        Assert.Equal(1.0, relu.S1.Mailles[1].HauteurDepart);
        Assert.Equal(0, relu.S1.Mailles[0].HauteurDepart);
    }

    [Fact]
    public void Sans_depart_rien_ne_change()
    {
        var a = GenerateurFacade.Generer(Face());
        var f = Face();
        f.Mailles[1].HauteurDepart = 0;
        var b = GenerateurFacade.Generer(f);
        Assert.Equal(a.Elements.Select(e => e.ToString()), b.Elements.Select(e => e.ToString()));
    }
}
