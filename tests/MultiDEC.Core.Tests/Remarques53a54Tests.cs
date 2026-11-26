using System;
using System.Linq;
using MultiDEC.Core.Facade;
using MultiDEC.Core.Geometrie;
using Xunit;

namespace MultiDEC.Core.Tests;

/// <summary>Remarques de Laurent du 26/11/2026 (points 53 et 54).</summary>
public class Remarques53a54Tests
{
    private static ProjetStructure Droite(double longueur = 15, double hauteur = 6, int acces = 1) =>
        ProjetStructure.Nouveau(face: new ParametresFacade { LongueurTotaleSouhaitee = longueur, LongueurMaillePreferee = 2.5, HauteurDernierPlancher = hauteur, NombreAcces = acces });

    private static bool A(StructureFacade s, Famille famille, int maille, Face face, double z) =>
        s.Elements.Any(e => e.Famille == famille && e.NumeroMaille == maille && e.Face == face && e.Axe == Axe.X && Math.Abs(e.Origine.Z - z) < 0.01);

    [Fact]
    public void Maille_d_acces_plinthe_et_lisse_1_50_aussi_au_dessus_du_dernier_plancher()
    {
        // Point 53 : maille d'accès montée 4 m au-dessus du dernier plancher (Ht max), garde-corps côté mur.
        var projet = Droite();
        projet.General.SecuriteInterieure = TypeSecurite.Lisses;
        projet.General.PlintheInterieure = TypePlinthe.Sans;
        var face = projet.S1;
        int i = face.Mailles.FindIndex(m => m.Type == TypeMaille.Acces);
        face.Mailles[i].EcartHauteur = 4;
        var s = GenerateurStructure.Generer(projet);
        int num = i + 1;
        foreach (double z in new[] { 4.0, 6.0, 8.0, 10.0 })
        {
            Assert.True(A(s, Famille.Plinthe, num, Face.Exterieure, z), $"plinthe extérieure à {z}");
            Assert.True(A(s, Famille.Plinthe, num, Face.Interieure, z), $"plinthe côté mur à {z}");
            Assert.True(A(s, Famille.Lisse, num, Face.Exterieure, z - 0.5), $"lisse 1,50 extérieure sous {z}");
            Assert.True(A(s, Famille.Lisse, num, Face.Interieure, z - 0.5), $"lisse 1,50 côté mur sous {z}");
        }
    }

    [Fact]
    public void Maille_de_travail_plus_haute_lisse_1_50_sous_la_trappe()
    {
        // Maille de travail montée au-dessus de ses voisines : plateau d'accès (trappe) + lisse à 1,50 m + plinthe.
        var projet = Droite(acces: 0);
        projet.General.SecuriteInterieure = TypeSecurite.GardeCorpsMds2;
        var face = projet.S1;
        face.Mailles[2].EcartHauteur = 4;
        var s = GenerateurStructure.Generer(projet);
        Assert.Contains(s.Elements, e => e.Famille == Famille.Trappe && e.NumeroMaille == 3 && Math.Abs(e.Origine.Z - 8) < 0.01);
        foreach (double z in new[] { 8.0, 10.0 })
        {
            Assert.True(A(s, Famille.Lisse, 3, Face.Exterieure, z - 0.5), $"lisse 1,50 extérieure sous {z}");
            Assert.True(A(s, Famille.Lisse, 3, Face.Interieure, z - 0.5), $"lisse 1,50 côté mur sous {z}");
            Assert.True(A(s, Famille.Plinthe, 3, Face.Exterieure, z), $"plinthe extérieure à {z}");
        }
    }
}

public class PiecesSupprimeesTests
{
    [Fact]
    public void Pieces_supprimees_retirees_a_chaque_generation_et_enregistrees_dans_le_mdc3()
    {
        // Point 54 : une suppression au clic droit est gardée dans le projet ; le .mdc3 rouvert donne la même structure.
        var projet = ProjetStructure.Nouveau(face: new ParametresFacade { LongueurTotaleSouhaitee = 10, HauteurDernierPlancher = 4 });
        var s = GenerateurStructure.Generer(projet);
        var cible = s.Elements.First(e => e.Famille == Famille.Plinthe);
        projet.PiecesSupprimees.Add(PieceSupprimee.De(cible));

        var json = MultiDEC.Core.Persistance.FichierProjet.VersJson(projet);
        var rouvert = MultiDEC.Core.Persistance.FichierProjet.DepuisJson(json);
        Assert.Single(rouvert.PiecesSupprimees);
        var s2 = GenerateurStructure.Generer(rouvert);
        Assert.Equal(s.Elements.Count - 1, s2.Elements.Count);
        Assert.DoesNotContain(s2.Elements, e => PieceSupprimee.De(cible).Designe(e));
        Assert.Contains(s2.Alertes, a => a.Contains("supprimée(s) à la main"));
    }

    [Fact]
    public void Piece_supprimee_qui_ne_correspond_plus_est_signalee()
    {
        var projet = ProjetStructure.Nouveau(face: new ParametresFacade { LongueurTotaleSouhaitee = 10, HauteurDernierPlancher = 4 });
        projet.PiecesSupprimees.Add(new PieceSupprimee { NomFace = "S1", Reference = "HMPOT20", X = 99, Y = 0, Z = 0, Axe = Axe.Z });
        var s = GenerateurStructure.Generer(projet);
        Assert.Contains(s.Alertes, a => a.Contains("ne correspondent plus"));
    }
}
