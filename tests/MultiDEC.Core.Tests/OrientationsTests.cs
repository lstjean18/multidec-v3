using MultiDEC.Core.Facade;
using MultiDEC.Core.Geometrie;
using MultiDEC.Rendering.VectorDraw;
using Xunit;

namespace MultiDEC.Core.Tests;

/// <summary>
/// Vérifie la convention de relevage des blocs de Blocs-EM-3D.dxf (dessinés dans XY, hauteur selon +Y).
/// </summary>
public class OrientationsTests
{
    private static readonly TableOrientations Table = TableOrientations.ParDefaut();

    private static Point3 Transforme(ElementPlace e, double x, double y, double z) => Matrices.Appliquer(Table.Matrice(e), x, y, z);

    [Fact]
    public void Poteau_releve_a_la_verticale()
    {
        var e = new ElementPlace("HMPOT20", Famille.Poteau, new Point3(2.5, -0.7, 0.046), Axe.Z);
        Assert.Equal(new Point3(2.5, -0.7, 2.046), Transforme(e, 0, 2, 0)); // sommet du poteau (0 ; 2) du bloc
    }

    [Fact]
    public void Lisse_le_long_de_la_facade()
    {
        var e = new ElementPlace("HMLIS250", Famille.Lisse, new Point3(5, 0, 4), Axe.X);
        Assert.Equal(new Point3(7.5, 0, 4), Transforme(e, 2.5, 0, 0));
    }

    [Fact]
    public void Traverse_vers_l_exterieur()
    {
        var e = new ElementPlace("HMTRA070", Famille.Traverse, new Point3(5, 0, 4), Axe.Y);
        Assert.Equal(new Point3(5, -0.7, 4), Transforme(e, 0.7, 0, 0));
    }

    [Fact]
    public void Console_vers_la_facade_et_jambe_de_force_en_dessous()
    {
        var e = new ElementPlace("HMCON700E", Famille.Console, new Point3(5, 0, 4), Axe.Y, Face.Interieure);
        Assert.Equal(new Point3(5, 0.7, 4), Transforme(e, 0.7, 0, 0));
        Assert.True(Transforme(e, 0.039, -0.464, 0).Z < 4); // pied de la jambe de force sous le plancher
    }

    [Fact]
    public void Plateau_largeur_vers_l_exterieur()
    {
        var e = new ElementPlace("HMPLA250298CL6", Famille.Plateau, new Point3(0, -0.025, 2), Axe.X);
        Assert.Equal(new Point3(0, -0.325, 2), Transforme(e, 0, 0, 0.30));
    }

    [Fact]
    public void Diagonale_inversee_monte_vers_la_gauche()
    {
        var e = new ElementPlace("HMDIA2502", Famille.DiagonaleVerticale, new Point3(2.5, -0.75, 0), Axe.X, Face.Exterieure, 1, inverse: true);
        Assert.Equal(new Point3(0, -0.75, 2), Transforme(e, 2.5, 2.0, 0));
    }

    [Fact]
    public void Garde_corps_interieur_retourne()
    {
        var ext = new ElementPlace("HMGCSE250", Famille.GardeCorps, new Point3(0, -0.7, 2), Axe.X, Face.Exterieure);
        var inte = new ElementPlace("HMGCSE250", Famille.GardeCorps, new Point3(0, 0, 2), Axe.X, Face.Interieure);
        Assert.Equal(-0.8, Transforme(ext, 0, 0, 0.1).Y, 3);
        Assert.Equal(0.1, Transforme(inte, 0, 0, 0.1).Y, 3);
    }

    [Fact]
    public void Reglage_par_prefixe_le_plus_long()
    {
        var t = new TableOrientations(new[]
        {
            new OrientationBloc { Prefixe = "HMCON", RotationZ = 10 },
            new OrientationBloc { Prefixe = "HMCON700", RotationZ = 20 },
        });
        Assert.Equal(20, t.Reglage("HMCON700E")!.RotationZ);
        Assert.Equal(10, t.Reglage("HMCON1000E")!.RotationZ);
        Assert.Null(t.Reglage("HMPOT20"));
    }
}
