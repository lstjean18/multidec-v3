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

    /// <summary>
    /// Rotation supplémentaire autour de X monde, en degrés, appliquée juste après le relevage : pour un bloc
    /// déjà dessiné debout (volées larges HMESCALU…88D, relevé serveur du 24/11/2026 : -90°).
    /// </summary>
    public double RotationX { get; set; }

    /// <summary>
    /// Rotation du bloc lui-même autour de la verticale, en degrés, juste après le relevage et avant les symétries
    /// (sens inversé, face intérieure) : pour un bloc dessiné tourné dans la bibliothèque. Volée HMESCALU300200 de
    /// « Blocs 3D - Echafaudage.dwg » du 26/11/2026 : longueur selon -Y au lieu de +X (relevé serveur) → +90°.
    /// </summary>
    public double RotationZBloc { get; set; }

    /// <summary>Retourne le bloc (symétrie de son axe Z local) quand l'élément est côté intérieur.</summary>
    public bool MiroirFaceInterieure { get; set; }

    /// <summary>
    /// Retourne toujours le bloc (symétrie de son axe Z local). Sert aux plateaux HMPLA / HMPLAF et aux trappes
    /// HMPLAL100602TR / 650TR, dessinés avec la largeur vers -Z, alors que les autres HMPLAL l'ont vers +Z.
    /// </summary>
    public bool MiroirZ { get; set; }

    /// <summary>
    /// Bloc posé en travers (axe Y) dessiné tourné vers l'intérieur : retourné pour qu'il soit tourné vers
    /// l'extérieur de la structure (cadres d'about HMGCSE…GT, Laurent 24/11/2026).
    /// </summary>
    public bool RetourneEnTravers { get; set; }

    /// <summary>
    /// Bloc posé en long (axe X) dont la face est retournée : cadres courts HMGCSE070/075/100/105, dont la
    /// profondeur de 10 cm partait vers la structure (relevé serveur et Laurent, 24/11/2026). Se cumule avec
    /// <see cref="MiroirFaceInterieure"/>, si bien que le cadre reste tourné vers l'extérieur de la structure des deux côtés.
    /// </summary>
    public bool RetourneEnLong { get; set; }

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

    /// <summary>Réglages par défaut : faces intérieures retournées, plateaux acier retournés (relevé du DXF du 23/11/2026).</summary>
    public static TableOrientations ParDefaut() => new TableOrientations(new[]
    {
        new OrientationBloc { Prefixe = "HMPLA", MiroirZ = true },
        new OrientationBloc { Prefixe = "HMPLAL", MiroirZ = false },
        // Trappes de 1,00 m ajoutées à la bibliothèque le 26/11/2026 : largeur vers -Z comme les plateaux, alors que les
        // autres HMPLAL l'ont vers +Z (relevé serveur : trappes côté façade, Y 0..+0,59, au lieu de -0,59..0).
        new OrientationBloc { Prefixe = "HMPLAL100602TR", MiroirZ = true },
        new OrientationBloc { Prefixe = "HMPLAL100650TR", MiroirZ = true },
        new OrientationBloc { Prefixe = "HMGCSE", MiroirFaceInterieure = true },
        new OrientationBloc { Prefixe = "HMGCSE070", MiroirFaceInterieure = true, RetourneEnLong = true },
        new OrientationBloc { Prefixe = "HMGCSE075", MiroirFaceInterieure = true, RetourneEnLong = true },
        new OrientationBloc { Prefixe = "HMGCSE100", MiroirFaceInterieure = true, RetourneEnLong = true },
        new OrientationBloc { Prefixe = "HMGCSE105", MiroirFaceInterieure = true, RetourneEnLong = true },
        new OrientationBloc { Prefixe = "HMPLIM", MiroirFaceInterieure = true },
        new OrientationBloc { Prefixe = "HMPLINBO", MiroirFaceInterieure = true },
        new OrientationBloc { Prefixe = "HMDIA", MiroirFaceInterieure = true },
        new OrientationBloc { Prefixe = "HMESCALU17510088D", RotationX = -90 },
        new OrientationBloc { Prefixe = "HMESCALU25020088D", RotationX = -90 },
        new OrientationBloc { Prefixe = "HMESCALU30020088D", RotationX = -90 },
        new OrientationBloc { Prefixe = "HMESCALU300200", RotationZBloc = 90 },
    });

    /// <summary>
    /// Charge un fichier JSON (tableau d'<see cref="OrientationBloc"/>) qui complète les réglages par défaut :
    /// une entrée remplace le réglage par défaut de même préfixe, les autres réglages par défaut restent.
    /// Fichier absent = réglages par défaut. (Avant le 24/11/2026 le fichier remplaçait tout, et l'appli
    /// ne suivait plus les corrections faites dans <see cref="ParDefaut"/>.)
    /// </summary>
    public static TableOrientations Charger(string chemin)
    {
        if (!File.Exists(chemin))
        {
            return ParDefaut();
        }

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        var liste = JsonSerializer.Deserialize<List<OrientationBloc>>(File.ReadAllText(chemin), options) ?? new List<OrientationBloc>();
        return Fusionner(ParDefaut()._reglages, liste);
    }

    /// <summary>Réglages par défaut complétés ou remplacés (même préfixe) par <paramref name="surcharges"/>.</summary>
    public static TableOrientations Fusionner(IEnumerable<OrientationBloc> parDefaut, IEnumerable<OrientationBloc> surcharges)
    {
        var parPrefixe = new Dictionary<string, OrientationBloc>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in parDefaut.Concat(surcharges))
        {
            parPrefixe[r.Prefixe] = r;
        }

        return new TableOrientations(parPrefixe.Values);
    }

    public OrientationBloc? Reglage(string reference) =>
        _reglages.FirstOrDefault(r => reference.StartsWith(r.Prefixe, StringComparison.OrdinalIgnoreCase));

    /// <summary>Matrice 4x4 (convention colonne : p' = M · p) pour un élément.</summary>
    public double[,] Matrice(ElementPlace e)
    {
        var r = Reglage(e.Reference);

        // 1. Relever le bloc.
        var m = Matrices.RotationX(90);
        if (r != null && Math.Abs(r.RotationX) > 1e-9)
        {
            m = Matrices.Produit(Matrices.RotationX(r.RotationX), m);
        }

        if (r != null && Math.Abs(r.RotationZBloc) > 1e-9)
        {
            m = Matrices.Produit(Matrices.RotationZ(r.RotationZBloc), m);
        }

        // 2. Symétries (appliquées dans le repère relevé : l'axe Z local est devenu -Y monde).
        if (r?.MiroirZ == true)
        {
            m = Matrices.Produit(Matrices.Echelle(1, -1, 1), m);
        }

        if (r?.MiroirFaceInterieure == true && e.Face == Face.Interieure)
        {
            m = Matrices.Produit(Matrices.Echelle(1, -1, 1), m); // retourne la face (axe Y monde = ancien Z local)
        }

        if (r?.RetourneEnLong == true && e.Axe == Axe.X)
        {
            m = Matrices.Produit(Matrices.Echelle(1, -1, 1), m); // cadre court : profondeur vers l'extérieur de la structure
        }

        if (e.Inverse && e.Axe == Axe.X)
        {
            m = Matrices.Produit(Matrices.Echelle(-1, 1, 1), m); // origine à droite, part vers la gauche
        }

        if (e.Axe == Axe.Y && (e.Inverse ^ (r?.RetourneEnTravers == true)))
        {
            // About de l'extrémité gauche (x = 0) : miroir de l'about droit selon X monde
            // (les garde-corps d'about ont un sens, Laurent 23/11/2026). Sans réglage, la profondeur des cadres
            // HMGCSE part vers l'extérieur de la structure (relevé serveur et Laurent, 24/11/2026).
            m = Matrices.Produit(Matrices.Echelle(1, -1, 1), m);
        }

        // 2 bis. Inclinaison autour de l'axe de l'élément (tôles de pare-gravois), avant l'orientation dans le plan.
        if (Math.Abs(e.Inclinaison) > 1e-9)
        {
            m = Matrices.Produit(Matrices.RotationX(e.Inclinaison), m);
        }

        // 3. Orientation dans le plan.
        double angle = AngleStandard(e) + (r?.RotationZ ?? 0) + e.Rotation;
        m = Matrices.Produit(Matrices.RotationZ(angle), m);

        // 4. Rotation de la face dans la structure (S2, S3… en équerre) : l'origine est déjà tournée.
        if (Math.Abs(e.AngleFace) > 1e-9)
        {
            m = Matrices.Produit(Matrices.RotationZ(e.AngleFace), m);
        }

        // 5. Position.
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

/// <summary>
/// Paramètres d'un vdInsert équivalents à une matrice : point d'insertion (monde), vecteur d'extrusion,
/// rotation dans le plan du repère objet (radians) et facteur d'échelle Z (-1 pour une symétrie).
/// C'est la manière d'insérer de l'ancienne version (InsertionPoint + ExtrusionVector + Rotation),
/// avec le repère objet calculé par l'algorithme d'axe arbitraire DXF.
/// </summary>
public sealed class PoseInsert
{
    public PoseInsert(Point3 insertion, Point3 extrusion, double rotation, double echelleZ)
    {
        Insertion = insertion;
        Extrusion = extrusion;
        Rotation = rotation;
        EchelleZ = echelleZ;
    }

    /// <summary>Point d'insertion en coordonnées monde.</summary>
    public Point3 Insertion { get; }

    public Point3 Extrusion { get; }

    /// <summary>
    /// Point d'insertion exprimé dans le repère objet (OCS) défini par l'extrusion.
    /// Attention : VectorDraw attend le point en coordonnées MONDE (<see cref="Insertion"/>) ;
    /// mesuré sur le serveur le 23/11/2026, l'OCS place les blocs de travers. Gardé pour le calibrage.
    /// </summary>
    public Point3 InsertionOcs
    {
        get
        {
            var n = new[] { Extrusion.X, Extrusion.Y, Extrusion.Z };
            var ax = AxeArbitraire(n);
            var ay = Vectoriel(n, ax);
            var p = new[] { Insertion.X, Insertion.Y, Insertion.Z };
            return new Point3(Scalaire(p, ax), Scalaire(p, ay), Scalaire(p, n));
        }
    }

    /// <summary>Repasse un point du repère objet au repère monde (pour les tests).</summary>
    public Point3 OcsVersMonde(Point3 q)
    {
        var n = new[] { Extrusion.X, Extrusion.Y, Extrusion.Z };
        var ax = AxeArbitraire(n);
        var ay = Vectoriel(n, ax);
        return new Point3(
            q.X * ax[0] + q.Y * ay[0] + q.Z * n[0],
            q.X * ax[1] + q.Y * ay[1] + q.Z * n[1],
            q.X * ax[2] + q.Y * ay[2] + q.Z * n[2]);
    }

    public double Rotation { get; }

    public double EchelleZ { get; }

    /// <summary>Décompose la matrice (rotation, éventuellement avec une symétrie, + translation).</summary>
    public static PoseInsert Depuis(double[,] m)
    {
        var u = new[] { m[0, 0], m[1, 0], m[2, 0] };
        var v = new[] { m[0, 1], m[1, 1], m[2, 1] };
        var w = new[] { m[0, 2], m[1, 2], m[2, 2] };

        // Symétrie : on la porte sur l'axe Z local (Zscale = -1), le reste devient une rotation directe.
        double det = Scalaire(Vectoriel(u, v), w);
        double echelleZ = det < 0 ? -1 : 1;
        var n = det < 0 ? new[] { -w[0], -w[1], -w[2] } : w;

        var ax = AxeArbitraire(n);
        var ay = Vectoriel(n, ax);
        double rotation = Math.Atan2(Scalaire(u, ay), Scalaire(u, ax));
        return new PoseInsert(
            new Point3(m[0, 3], m[1, 3], m[2, 3]),
            new Point3(n[0], n[1], n[2]),
            Math.Abs(rotation) < 1e-12 ? 0 : rotation,
            echelleZ);
    }

    /// <summary>Recalcule la matrice (pour les tests) : M = T · [Ax Ay N] · Rz(rotation) · Sz(echelleZ).</summary>
    public double[,] VersMatrice()
    {
        var n = new[] { Extrusion.X, Extrusion.Y, Extrusion.Z };
        var ax = AxeArbitraire(n);
        var ay = Vectoriel(n, ax);
        var ocs = Matrices.Identite();
        for (int i = 0; i < 3; i++)
        {
            ocs[i, 0] = ax[i];
            ocs[i, 1] = ay[i];
            ocs[i, 2] = n[i];
        }

        var m = Matrices.Produit(Matrices.RotationZ(Rotation * 180 / Math.PI), Matrices.Echelle(1, 1, EchelleZ));
        m = Matrices.Produit(ocs, m);
        return Matrices.Produit(Matrices.Translation(Insertion.X, Insertion.Y, Insertion.Z), m);
    }

    /// <summary>Algorithme d'axe arbitraire (DXF / VectorDraw) : axe X du repère objet pour une normale donnée.</summary>
    public static double[] AxeArbitraire(double[] n)
    {
        const double seuil = 1.0 / 64.0;
        var a = Math.Abs(n[0]) < seuil && Math.Abs(n[1]) < seuil
            ? Vectoriel(new[] { 0.0, 1, 0 }, n)
            : Vectoriel(new[] { 0.0, 0, 1 }, n);
        double l = Math.Sqrt(Scalaire(a, a));
        return new[] { a[0] / l, a[1] / l, a[2] / l };
    }

    private static double[] Vectoriel(double[] a, double[] b) => new[]
    {
        a[1] * b[2] - a[2] * b[1],
        a[2] * b[0] - a[0] * b[2],
        a[0] * b[1] - a[1] * b[0],
    };

    private static double Scalaire(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
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
