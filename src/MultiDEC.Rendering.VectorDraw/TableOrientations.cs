using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MultiDEC.Core.Facade;
using MultiDEC.Core.Geometrie;

namespace MultiDEC.Rendering.VectorDraw;

/// <summary>
/// Réglage d'orientation pour une famille de blocs (repérée par le début de sa référence).
/// Sert à corriger, sans recompiler, un bloc dont l'orientation dans le DXF ne suit pas la convention.
/// </summary>
public sealed class OrientationBloc
{
    /// <summary>Début de référence concerné (ex. "HMCON"). Le préfixe le plus long l'emporte.</summary>
    public string Prefixe { get; set; } = string.Empty;

    /// <summary>Rotation supplémentaire autour de Z, en degrés, appliquée après la rotation standard.</summary>
    public double RotationZ { get; set; }

    /// <summary>Retourne le bloc (symétrie de son axe Z local) quand l'élément est côté intérieur.</summary>
    public bool MiroirFaceInterieure { get; set; }

    /// <summary>Décalage en mètres dans le repère monde, appliqué en dernier.</summary>
    public double[] Decalage { get; set; } = new double[3];
}

/// <summary>
/// Calcule la matrice d'insertion de chaque élément.
/// Convention des blocs de Blocs-EM-3D.dxf (relevée le 23/11/2026) : chaque bloc est dessiné dans le plan XY,
/// la hauteur selon +Y (poteau HMPOT20 de (0,0) à (0,2) ; garde-corps HMGCSE250 de y = -0,571 à 1,00),
/// la longueur selon +X (HMLIS250 de 0 à 2,50), la largeur des plateaux et trappes selon +Z.
/// Matrice = Translation(origine + décalage) · Rz(angle) · [symétries] · Rx(+90°)
/// Rx(+90°) relève le bloc : Y local → Z monde, Z local → -Y monde (largeur des plateaux vers l'extérieur).
/// </summary>
public sealed class TableOrientations
{
    private readonly List<OrientationBloc> _reglages;

    public TableOrientations(IEnumerable<OrientationBloc>? reglages = null)
    {
        _reglages = (reglages ?? Enumerable.Empty<OrientationBloc>())
            .OrderByDescending(r => r.Prefixe.Length)
            .ToList();
    }

    /// <summary>Réglages par défaut : consoles tournées vers la façade, faces intérieures retournées.</summary>
    public static TableOrientations ParDefaut() => new TableOrientations(new[]
    {
        new OrientationBloc { Prefixe = "HMGCSE", MiroirFaceInterieure = true },
        new OrientationBloc { Prefixe = "HMPLIM", MiroirFaceInterieure = true },
        new OrientationBloc { Prefixe = "HMPLINBO", MiroirFaceInterieure = true },
        new OrientationBloc { Prefixe = "HMDIA", MiroirFaceInterieure = true },
    });

    /// <summary>Charge un fichier JSON (tableau d'<see cref="OrientationBloc"/>) ; fichier absent = réglages par défaut.</summary>
    public static TableOrientations Charger(string chemin)
    {
        if (!File.Exists(chemin))
        {
            return ParDefaut();
        }

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip };
        var liste = JsonSerializer.Deserialize<List<OrientationBloc>>(File.ReadAllText(chemin), options);
        return new TableOrientations(liste);
    }

    public OrientationBloc? Reglage(string reference) =>
        _reglages.FirstOrDefault(r => reference.StartsWith(r.Prefixe, StringComparison.OrdinalIgnoreCase));

    /// <summary>Matrice 4x4 (convention colonne : p' = M · p) pour un élément.</summary>
    public double[,] Matrice(ElementPlace e)
    {
        var r = Reglage(e.Reference);

        // 1. Relever le bloc.
        var m = Matrices.RotationX(90);

        // 2. Symétries (appliquées dans le repère relevé).
        if (r?.MiroirFaceInterieure == true && e.Face == Face.Interieure)
        {
            m = Matrices.Produit(Matrices.Echelle(1, -1, 1), m); // retourne la face (axe Y monde = ancien Z local)
        }

        if (e.Inverse && e.Axe == Axe.X)
        {
            m = Matrices.Produit(Matrices.Echelle(-1, 1, 1), m); // origine à droite, part vers la gauche
        }

        // 3. Orientation dans le plan.
        double angle = AngleStandard(e) + (r?.RotationZ ?? 0);
        m = Matrices.Produit(Matrices.RotationZ(angle), m);

        // 4. Position.
        var d = r?.Decalage ?? new double[3];
        m = Matrices.Produit(Matrices.Translation(e.Origine.X + d[0], e.Origine.Y + d[1], e.Origine.Z + d[2]), m);
        return m;
    }

    /// <summary>Rotation autour de Z selon l'axe de l'élément.</summary>
    public static double AngleStandard(ElementPlace e)
    {
        if (e.Axe != Axe.Y || e.Famille == Famille.Amarrage)
        {
            return 0; // éléments verticaux, le long de la façade, et symbole d'amarrage vu en façade
        }

        // En travers : vers l'extérieur (-Y), sauf les consoles qui partent vers la façade (+Y).
        return e.Famille == Famille.Console ? 90 : -90;
    }
}

/// <summary>Petites opérations sur matrices 4x4 (indépendantes de VectorDraw, donc testables).</summary>
public static class Matrices
{
    public static double[,] Identite()
    {
        var m = new double[4, 4];
        for (int i = 0; i < 4; i++)
        {
            m[i, i] = 1;
        }

        return m;
    }

    public static double[,] Translation(double x, double y, double z)
    {
        var m = Identite();
        m[0, 3] = x;
        m[1, 3] = y;
        m[2, 3] = z;
        return m;
    }

    public static double[,] Echelle(double sx, double sy, double sz)
    {
        var m = Identite();
        m[0, 0] = sx;
        m[1, 1] = sy;
        m[2, 2] = sz;
        return m;
    }

    public static double[,] RotationX(double degres)
    {
        double a = degres * Math.PI / 180, c = Arrondi(Math.Cos(a)), s = Arrondi(Math.Sin(a));
        var m = Identite();
        m[1, 1] = c;
        m[1, 2] = -s;
        m[2, 1] = s;
        m[2, 2] = c;
        return m;
    }

    public static double[,] RotationZ(double degres)
    {
        double a = degres * Math.PI / 180, c = Arrondi(Math.Cos(a)), s = Arrondi(Math.Sin(a));
        var m = Identite();
        m[0, 0] = c;
        m[0, 1] = -s;
        m[1, 0] = s;
        m[1, 1] = c;
        return m;
    }

    /// <summary>a · b : applique d'abord b puis a.</summary>
    public static double[,] Produit(double[,] a, double[,] b)
    {
        var m = new double[4, 4];
        for (int i = 0; i < 4; i++)
        {
            for (int j = 0; j < 4; j++)
            {
                double s = 0;
                for (int k = 0; k < 4; k++)
                {
                    s += a[i, k] * b[k, j];
                }

                m[i, j] = s;
            }
        }

        return m;
    }

    public static Point3 Appliquer(double[,] m, double x, double y, double z) => new Point3(
        m[0, 0] * x + m[0, 1] * y + m[0, 2] * z + m[0, 3],
        m[1, 0] * x + m[1, 1] * y + m[1, 2] * z + m[1, 3],
        m[2, 0] * x + m[2, 1] * y + m[2, 2] * z + m[2, 3]);

    private static double Arrondi(double v) => Math.Abs(v) < 1e-12 ? 0 : Math.Round(v, 12);
}
