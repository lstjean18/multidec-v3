using MultiDEC.Core.Facade;
using Xunit;

namespace MultiDEC.Core.Tests;

public class ProposeurStructureTests
{
    [Fact]
    public void Longueur_multiple_exact_sans_acces()
    {
        var p = new ParametresFacade { LongueurTotaleSouhaitee = 20, LongueurMaillePreferee = 2.5, NombreAcces = 0 };
        var mailles = ProposeurStructure.ProposerMailles(p);
        Assert.Equal(8, mailles.Count);
        Assert.All(mailles, m => Assert.Equal(2.5, m.Longueur));
    }

    [Fact]
    public void Acces_reparti_entre_blocs_de_travail()
    {
        var p = new ParametresFacade { LongueurTotaleSouhaitee = 20, LongueurMaillePreferee = 2.5, NombreAcces = 1 };
        var mailles = ProposeurStructure.ProposerMailles(p);
        Assert.Equal(8, mailles.Count);
        int iAcces = mailles.FindIndex(m => m.Type == TypeMaille.Acces);
        Assert.Equal(4, iAcces); // 4 mailles de travail, l'accès, 3 mailles de travail
        Assert.Single(mailles, m => m.Type == TypeMaille.Acces);
    }

    [Fact]
    public void Reste_complete_avec_longueurs_standard()
    {
        // 21,20 m en 2,50 : 8 x 2,50 = 20, reste 1,20 → on rend une maille : 3,70 = 3,00 + 0,70
        var p = new ParametresFacade { LongueurTotaleSouhaitee = 21.2, LongueurMaillePreferee = 2.5, NombreAcces = 0 };
        var projet = ProposeurStructure.NouveauProjet(p);
        Assert.Equal(21.2, projet.LongueurObtenue, 3);
        Assert.Equal(new[] { 3.0, 0.7 }, projet.Mailles.Skip(7).Select(m => m.Longueur));
    }

    [Fact]
    public void Complement_utilise_1m50()
    {
        Assert.Equal(new[] { 1.5 }, ProposeurStructure.Completer(1.5));
        Assert.Equal(new[] { 3.0, 1.5 }, ProposeurStructure.Completer(4.5));
        Assert.Equal(new[] { 1.0, 0.7 }, ProposeurStructure.Completer(1.7));
        Assert.Equal(new[] { 1.0 }, ProposeurStructure.Completer(1.2)); // reliquat 0,20 abandonné
        Assert.Empty(ProposeurStructure.Completer(0.5));
    }

    [Fact]
    public void Grille_proposee_avec_cases_diagonales_pre_cochees_et_sans_console()
    {
        var p = new ParametresFacade { LongueurTotaleSouhaitee = 30, LongueurMaillePreferee = 2.5, NombreAcces = 1 };
        var mailles = ProposeurStructure.ProposerMailles(p);
        Assert.Equal(new[] { 1, 6, 11 }, mailles.Select((m, i) => (m, i)).Where(x => x.m.Diagonale).Select(x => x.i + 1));
        Assert.All(mailles, m => Assert.Empty(m.Consoles));
    }

    [Fact]
    public void Niveaux_tous_les_2m_depuis_le_dernier_plancher()
    {
        var niveaux = ProposeurStructure.ProposerNiveaux(new ParametresFacade { HauteurDernierPlancher = 10 });
        Assert.Equal(new[] { 0.0, 2, 4, 6, 8, 10 }, niveaux.Select(n => n.Hauteur));
        Assert.Equal(TypePlancher.AccesSeul, niveaux[0].Type);
        Assert.All(niveaux.Skip(1), n => Assert.Equal(TypePlancher.Complet, n.Type));
    }

    [Fact]
    public void Niveaux_hauteur_impaire()
    {
        var niveaux = ProposeurStructure.ProposerNiveaux(new ParametresFacade { HauteurDernierPlancher = 9 });
        Assert.Equal(new[] { 1.0, 3, 5, 7, 9 }, niveaux.Select(n => n.Hauteur));
    }
}
