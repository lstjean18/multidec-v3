using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MultiDEC.Core.Facade;
using MultiDEC.Core.Geometrie;
using MultiDEC.Rendering.VectorDraw;
using VectorDraw.Geometry;
using VectorDraw.Professional.Components;
using VectorDraw.Professional.vdFigures;
using VectorDraw.Professional.vdObjects;
using VectorDraw.Professional.vdPrimaries;
using Xunit;
using Xunit.Abstractions;

namespace MultiDEC.Rendering.Tests;

/// <summary>
/// Mesure où VectorDraw place réellement un bloc selon la manière de l'insérer,
/// pour les orientations utilisées par le moteur façade. Le bloc de calibrage est un trièdre :
/// X long de 1 m, Y long de 2 m, Z long de 0,5 m.
/// </summary>
public class CalibrageInsertionTests
{
    private readonly ITestOutputHelper _sortie;

    public CalibrageInsertionTests(ITestOutputHelper sortie) => _sortie = sortie;

    public static IEnumerable<ElementPlace> Echantillon() => new[]
    {
        new ElementPlace("POTEAU", Famille.Poteau, new Point3(2.5, -0.7, 4), Axe.Z),
        new ElementPlace("LISSE", Famille.Lisse, new Point3(5, 0, 4), Axe.X),
        new ElementPlace("TRAVERSE", Famille.Traverse, new Point3(5, 0, 4), Axe.Y),
        new ElementPlace("CONSOLE", Famille.Console, new Point3(5, 0, 4), Axe.Y, Face.Interieure),
        new ElementPlace("HMDIA-INV", Famille.DiagonaleVerticale, new Point3(2.5, -0.75, 2), Axe.X, Face.Exterieure, 1, inverse: true),
        new ElementPlace("HMGCSE-INT", Famille.GardeCorps, new Point3(1, 0, 2), Axe.X, Face.Interieure),
    };

    [Fact]
    public void Calibrage()
    {
        var comp = new vdDocumentComponent();
        var doc = comp.Document;
        var bloc = new vdBlock();
        bloc.SetUnRegisterDocument(doc);
        bloc.setDocumentDefaults();
        bloc.Name = "CALIBRAGE";
        bloc.Entities.AddItem(Ligne(doc, 1, 0, 0));
        bloc.Entities.AddItem(Ligne(doc, 0, 2, 0));
        bloc.Entities.AddItem(Ligne(doc, 0, 0, 0.5));
        doc.Blocks.AddItem(bloc);

        var table = TableOrientations.ParDefaut();
        var methodes = new (string Nom, Action<vdInsert, double[,]> Appliquer)[]
        {
            ("A pose+OCS", (ins, m) => { var p = PoseInsert.Depuis(m); var o = p.InsertionOcs; ins.InsertionPoint = new gPoint(o.X, o.Y, o.Z); Pose(ins, p); }),
            ("B pose+WCS", (ins, m) => { var p = PoseInsert.Depuis(m); ins.InsertionPoint = new gPoint(p.Insertion.X, p.Insertion.Y, p.Insertion.Z); Pose(ins, p); }),
            ("C Transformby", (ins, m) => { ins.InsertionPoint = new gPoint(0, 0, 0); ins.Transformby(VersMatrix(m, false)); }),
            ("D Transformby T", (ins, m) => { ins.InsertionPoint = new gPoint(0, 0, 0); ins.Transformby(VersMatrix(m, true)); }),
        };

        var ok = new Dictionary<string, int>();
        foreach (var e in Echantillon())
        {
            var m = table.Matrice(e);
            var attendu = BoiteAttendue(m);
            _sortie.WriteLine($"== {e.Reference}  attendu {Texte(attendu.Min)} .. {Texte(attendu.Max)}");
            foreach (var (nom, appliquer) in methodes)
            {
                var ins = new vdInsert();
                ins.SetUnRegisterDocument(doc);
                ins.setDocumentDefaults();
                ins.Block = bloc;
                try
                {
                    appliquer(ins, m);
                    ins.Update();
                    var b = ins.BoundingBox;
                    bool juste = Proche(b.Min, attendu.Min) && Proche(b.Max, attendu.Max);
                    if (juste)
                    {
                        ok[nom] = (ok.TryGetValue(nom, out int n) ? n : 0) + 1;
                    }

                    _sortie.WriteLine($"   {nom,-16} {(juste ? "OK " : "KO ")} {Texte(b.Min)} .. {Texte(b.Max)}  ext={Texte(ins.ExtrusionVector)} rot={ins.Rotation:0.###} ins={Texte(ins.InsertionPoint)} sc=({ins.Xscale};{ins.Yscale};{ins.Zscale})");
                }
                catch (Exception ex)
                {
                    _sortie.WriteLine($"   {nom,-16} ERREUR {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        _sortie.WriteLine("BILAN : " + string.Join(" | ", methodes.Select(x => $"{x.Nom} {(ok.TryGetValue(x.Nom, out int n) ? n : 0)}/{Echantillon().Count()}")));

        // La méthode utilisée par RenduVectorDraw (pose + point monde) doit placer juste toutes les orientations.
        Assert.Equal(Echantillon().Count(), ok.TryGetValue("B pose+WCS", out int retenue) ? retenue : 0);
    }

    private static void Pose(vdInsert ins, PoseInsert p)
    {
        ins.ExtrusionVector = new Vector(p.Extrusion.X, p.Extrusion.Y, p.Extrusion.Z);
        ins.Rotation = p.Rotation;
        if (p.EchelleZ < 0)
        {
            ins.Zscale = -1;
        }
    }

    private static (Point3 Min, Point3 Max) BoiteAttendue(double[,] m)
    {
        var pts = new[] { (0.0, 0.0, 0.0), (1.0, 0.0, 0.0), (0.0, 2.0, 0.0), (0.0, 0.0, 0.5) }
            .Select(p => Matrices.Appliquer(m, p.Item1, p.Item2, p.Item3)).ToList();
        return (new Point3(pts.Min(p => p.X), pts.Min(p => p.Y), pts.Min(p => p.Z)),
                new Point3(pts.Max(p => p.X), pts.Max(p => p.Y), pts.Max(p => p.Z)));
    }

    private static bool Proche(gPoint a, Point3 b) =>
        Math.Abs(a.x - b.X) < 0.01 && Math.Abs(a.y - b.Y) < 0.01 && Math.Abs(a.z - b.Z) < 0.01;

    private static string Texte(gPoint p) => string.Format(CultureInfo.InvariantCulture, "({0:0.##};{1:0.##};{2:0.##})", p.x, p.y, p.z);

    private static string Texte(Vector p) => string.Format(CultureInfo.InvariantCulture, "({0:0.##};{1:0.##};{2:0.##})", p.x, p.y, p.z);

    private static string Texte(Point3 p) => string.Format(CultureInfo.InvariantCulture, "({0:0.##};{1:0.##};{2:0.##})", p.X, p.Y, p.Z);

    private static vdLine Ligne(vdDocument doc, double x, double y, double z)
    {
        var l = new vdLine();
        l.SetUnRegisterDocument(doc);
        l.setDocumentDefaults();
        l.StartPoint = new gPoint(0, 0, 0);
        l.EndPoint = new gPoint(x, y, z);
        return l;
    }

    private static Matrix VersMatrix(double[,] m, bool transposee)
    {
        double A(int i, int j) => transposee ? m[j, i] : m[i, j];
        var r = new Matrix();
        r.A00 = A(0, 0); r.A01 = A(0, 1); r.A02 = A(0, 2); r.A03 = A(0, 3);
        r.A10 = A(1, 0); r.A11 = A(1, 1); r.A12 = A(1, 2); r.A13 = A(1, 3);
        r.A20 = A(2, 0); r.A21 = A(2, 1); r.A22 = A(2, 2); r.A23 = A(2, 3);
        r.A30 = A(3, 0); r.A31 = A(3, 1); r.A32 = A(3, 2); r.A33 = A(3, 3);
        return r;
    }
}
