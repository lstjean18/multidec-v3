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
        // Les plateaux acier du DXF ont leur largeur vers -Z local (relevé du 23/11/2026).
        var e = new ElementPlace("HMPLA250298CL6", Famille.Plateau, new Point3(0, -0.025, 2), Axe.X);
        Assert.Equal(new Point3(0, -0.325, 2), Transforme(e, 0, 0, -0.30));
    }

    [Fact]
    public void Cadre_d_about_retourne_vers_l_exterieur()
    {
        // Laurent 24/11/2026 : les cadres HMGCSE…GT étaient tournés vers l'intérieur ; l'about gauche reste le miroir du droit.
        var droit = new ElementPlace("HMGCSE070GT", Famille.GardeCorps, new Point3(20, 0, 2), Axe.Y);
        var gauche = new ElementPlace("HMGCSE070GT", Famille.GardeCorps, new Point3(0, 0, 2), Axe.Y, Face.Aucune, 1, inverse: true);
        var lisse = new ElementPlace("HMTRA070", Famille.Traverse, new Point3(20, 0, 2), Axe.Y);
        var lisseGauche = new ElementPlace("HMTRA070", Famille.Traverse, new Point3(0, 0, 2), Axe.Y, Face.Aucune, 1, inverse: true);
        Assert.Equal(-Transforme(lisse, 0.3, 0, 0.2).X + 40, Transforme(droit, 0.3, 0, 0.2).X, 6);
        Assert.Equal(-Transforme(lisseGauche, 0.3, 0, 0.2).X, Transforme(gauche, 0.3, 0, 0.2).X, 6);
    }

    [Fact]
    public void Volee_large_88D_deja_debout_non_relevee()
    {
        // Relevé serveur du 24/11/2026 : les volées 88D relevées étaient couchées (largeur selon Z) ;
        // la correction de -90° autour de X les remet comme les volées de 0,65 m (descente selon -Z, largeur selon +Y).
        var e = new ElementPlace("HMESCALU25020088D", Famille.Escalier, new Point3(5, -1.0, 4), Axe.X);
        Assert.Equal(new Point3(7.5, -0.1, 2), Transforme(e, 2.5, 0.9, -2.0));
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
    public void About_gauche_miroir_de_l_about_droit()
    {
        var droit = new ElementPlace("HMGCSE070", Famille.GardeCorps, new Point3(10, 0, 2), Axe.Y, Face.Aucune, 4);
        var gauche = new ElementPlace("HMGCSE070", Famille.GardeCorps, new Point3(0, 0, 2), Axe.Y, Face.Aucune, 1, inverse: true);

        // Même tracé en travers (de y = 0 vers l'extérieur)...
        Assert.Equal(new Point3(10, -0.7, 2), Transforme(droit, 0.7, 0, 0));
        Assert.Equal(new Point3(0, -0.7, 2), Transforme(gauche, 0.7, 0, 0));

        // ... mais le côté « face » du bloc est retourné selon X.
        double dxDroit = Transforme(droit, 0, 0, 0.1).X - 10;
        double dxGauche = Transforme(gauche, 0, 0, 0.1).X;
        Assert.Equal(-dxDroit, dxGauche, 3);
        Assert.NotEqual(0, dxGauche, 3);
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

public class PoseInsertTests
{
    private static readonly TableOrientations Table = TableOrientations.ParDefaut();

    public static IEnumerable<object[]> Elements() => new[]
    {
        new object[] { new ElementPlace("HMPOT20", Famille.Poteau, new Point3(2.5, -0.7, 0.046), Axe.Z) },
        new object[] { new ElementPlace("HMLIS250", Famille.Lisse, new Point3(5, 0, 4), Axe.X) },
        new object[] { new ElementPlace("HMTRA070", Famille.Traverse, new Point3(5, 0, 4), Axe.Y) },
        new object[] { new ElementPlace("HMCON700E", Famille.Console, new Point3(5, 0, 4), Axe.Y, Face.Interieure) },
        new object[] { new ElementPlace("HMDIA2502", Famille.DiagonaleVerticale, new Point3(2.5, -0.75, 0), Axe.X, Face.Exterieure, 1, inverse: true) },
        new object[] { new ElementPlace("HMGCSE250", Famille.GardeCorps, new Point3(0, 0, 2), Axe.X, Face.Interieure) },
        new object[] { new ElementPlace("HMDIA2502", Famille.DiagonaleVerticale, new Point3(2.5, 0.05, 0), Axe.X, Face.Interieure, 1, inverse: true) },
    };

    [Theory]
    [MemberData(nameof(Elements))]
    public void Pose_reproduit_la_matrice(ElementPlace e)
    {
        var m = Table.Matrice(e);
        var r = PoseInsert.Depuis(m).VersMatrice();
        foreach (var p in new[] { (0.0, 0.0, 0.0), (1.0, 0.0, 0.0), (0.0, 1.0, 0.0), (0.0, 0.0, 1.0), (0.3, 2.0, -0.4) })
        {
            Assert.Equal(Matrices.Appliquer(m, p.Item1, p.Item2, p.Item3), Matrices.Appliquer(r, p.Item1, p.Item2, p.Item3));
        }
    }

    [Fact]
    public void Poteau_comme_l_ancienne_version_extrusion_horizontale()
    {
        // Relevage Rx(+90°) = extrusion (0 ; -1 ; 0) sans rotation : Y du bloc → Z monde.
        var pose = PoseInsert.Depuis(Table.Matrice(new ElementPlace("HMPOT20", Famille.Poteau, new Point3(1, 2, 3), Axe.Z)));
        Assert.Equal(new Point3(0, -1, 0), pose.Extrusion);
        Assert.Equal(0, pose.Rotation, 9);
        Assert.Equal(1, pose.EchelleZ);
        Assert.Equal(new Point3(1, 2, 3), pose.Insertion);
    }
}

public class InsertionOcsTests
{
    [Fact]
    public void Point_monde_converti_dans_le_repere_objet_puis_retrouve()
    {
        var table = TableOrientations.ParDefaut();
        foreach (var e in PoseInsertTests.Elements().Select(o => (ElementPlace)o[0]))
        {
            var pose = PoseInsert.Depuis(table.Matrice(e));
            Assert.Equal(pose.Insertion, pose.OcsVersMonde(pose.InsertionOcs));
        }
    }

    [Fact]
    public void Poteau_a_10m_de_haut_reste_a_10m_en_Z()
    {
        // Cas de la capture du 23/11 : avec l'extrusion (0 ; -1 ; 0), le Z monde devient le Y objet.
        var pose = PoseInsert.Depuis(TableOrientations.ParDefaut().Matrice(
            new ElementPlace("HMPOT20", Famille.Poteau, new Point3(5, -0.7, 10), Axe.Z)));
        Assert.Equal(new Point3(5, 10, 0.7), pose.InsertionOcs);
    }
}

public class PlateauxOrientationTests
{
    [Theory]
    [InlineData("HMPLA250298CL6", -0.298)] // dessiné largeur vers -Z → retourné
    [InlineData("HMPLAF2503CL6", -0.298)]
    public void Plateau_acier_largeur_vers_l_exterieur(string reference, double zLocal)
    {
        var e = new ElementPlace(reference, Famille.Plateau, new Point3(0, -0.025, 2), Axe.X);
        var p = Matrices.Appliquer(TableOrientations.ParDefaut().Matrice(e), 0, 0, zLocal);
        Assert.Equal(-0.323, p.Y, 3);
    }

    [Fact]
    public void Trappe_alu_largeur_vers_l_exterieur()
    {
        var e = new ElementPlace("HMPLAL250602TREC", Famille.Trappe, new Point3(0, -0.025, 2), Axe.X);
        var p = Matrices.Appliquer(TableOrientations.ParDefaut().Matrice(e), 0, 0, 0.596);
        Assert.Equal(-0.621, p.Y, 3);
    }
}
