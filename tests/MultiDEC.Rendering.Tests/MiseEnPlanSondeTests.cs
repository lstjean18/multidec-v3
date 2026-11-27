using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using MultiDEC.Core.Facade;
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
/// Sonde de la mise en plan (27/11/2026) : présentation A3, fenêtre de vue à matrice imposée, cotes et textes en espace papier,
/// image de la page et PDF. Sert à caler la projection du cœur sur celle de VectorDraw.
/// </summary>
public class MiseEnPlanSondeTests
{
    private readonly ITestOutputHelper _sortie;

    public MiseEnPlanSondeTests(ITestOutputHelper sortie) => _sortie = sortie;

    private static string Bibliotheque()
    {
        var ws = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        if (!string.IsNullOrEmpty(ws))
        {
            var runner = Path.GetFullPath(Path.Combine(ws, "..", "..", ".."));
            var f = Directory.GetDirectories(runner).Where(d => Path.GetFileName(d).StartsWith("biblioth", StringComparison.OrdinalIgnoreCase))
                .SelectMany(d => Directory.GetFiles(d, "Blocs 3D - Echafaudage.dwg")).FirstOrDefault();
            if (f != null)
            {
                return f;
            }
        }

        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Donnees", "Bloc3D.dwg");
    }

    private void Membres(Type t, string filtre)
    {
        var noms = t.GetMembers().Where(m => System.Text.RegularExpressions.Regex.IsMatch(m.Name, filtre))
            .Select(m => m is System.Reflection.MethodInfo mi ? mi.Name + "(" + string.Join(", ", mi.GetParameters().Select(p => p.ParameterType.Name)) + ")" : m.Name)
            .Distinct().OrderBy(n => n);
        _sortie.WriteLine("MEMBRES " + t.Name + " : " + string.Join(" | ", noms));
    }

    private static Matrix Vue(double[] u, double[] v, double[] w)
    {
        var m = new Matrix();
        m.A00 = u[0]; m.A01 = u[1]; m.A02 = u[2];
        m.A10 = v[0]; m.A11 = v[1]; m.A12 = v[2];
        m.A20 = w[0]; m.A21 = w[1]; m.A22 = w[2];
        return m;
    }

    private vdViewport Fenetre(vdDocument doc, vdLayout lay, string nom, gPoint centre, double l, double h, Matrix m, gPoint viewCenter, double viewSize,
        global::VectorDraw.Render.vdRender.Mode mode, bool add)
    {
        var vp = new vdViewport(doc);
        vp.setDocumentDefaults();
        vp.Width = l;
        vp.Height = h;
        vp.Center = centre;
        vp.World2ViewMatrix = m;
        vp.ViewCenter = viewCenter;
        vp.ViewSize = viewSize;
        vp.RenderMode = mode;
        if (add)
        {
            lay.Entities.Add(vp);
        }
        else
        {
            lay.Entities.AddItem(vp);
        }

        vp.Update();
        _sortie.WriteLine($"VP {nom} : add={add} mode={mode} ViewCenter {vp.ViewCenter} ViewSize {vp.ViewSize.ToString(CultureInfo.InvariantCulture)} W2V {Mat(vp.World2ViewMatrix)}");
        var t = new vdText(doc) { TextString = nom, Height = 3, InsertionPoint = new gPoint(centre.x - l / 2, centre.y - h / 2 - 5) };
        lay.Entities.AddItem(t);
        return vp;
    }

    [Fact]
    public void Sonde_presentation_A3_et_pdf()
    {
        Membres(typeof(vdLayout).GetProperty("Entities").PropertyType, "^Add");
        Membres(typeof(vdPrint), "Paper|Margin|Name");

        var doc = new vdDocumentComponent().Document;
        var rendu = new RenduVectorDraw(doc);
        rendu.ChargerBibliotheque(Bibliotheque());
        var projet = ProjetStructure.Nouveau(face: new ParametresFacade { LongueurTotaleSouhaitee = 10, LongueurMaillePreferee = 2.5, HauteurDernierPlancher = 6, NombreAcces = 1 });
        rendu.Dessiner(GenerateurStructure.Generer(projet), TableOrientations.ParDefaut());
        _sortie.WriteLine("Modèle : " + doc.Model.Entities.Count + " entités");

        var lay = new vdLayout(doc, "Plan-sonde");
        lay.setDocumentDefaults();
        doc.LayOuts.AddItem(lay);
        lay.World2UserMatrix = new Matrix();
        lay.World2ViewMatrix = new Matrix();
        lay.BkColorEx = Color.White;
        var a3 = lay.Printer.GetPapers().Cast<object>().Select(o => o.ToString()).FirstOrDefault(p => p == "A3");
        if (a3 != null)
        {
            lay.Printer.SelectPaper(a3);
        }

        lay.Printer.LandScape = true;
        lay.Printer.PrintWindow = new Box(new gPoint(0, 0), new gPoint(420, 297));
        _sortie.WriteLine("Papier : " + lay.Printer.GetPaper() + " zone imprimable " + lay.Printer.PrintableAreaMM);
        lay.Entities.AddItem(new vdRect(doc, new gPoint(10, 10), 400, 277, 0));

        // Élévation côté extérieur : U = X, V = Z, W = -Y.
        var elev = Vue(new double[] { 1, 0, 0 }, new double[] { 0, 0, 1 }, new double[] { 0, -1, 0 });
        var mode = global::VectorDraw.Render.vdRender.Mode.ShadeOn;
        Fenetre(doc, lay, "A AddItem ShadeOn", new gPoint(70, 230), 120, 80, elev, new gPoint(5, 3.5, 0), 8, mode, false);
        Fenetre(doc, lay, "B Add ShadeOn", new gPoint(210, 230), 120, 80, elev, new gPoint(5, 3.5, 0), 8, mode, true);
        Fenetre(doc, lay, "C Add Hide", new gPoint(350, 230), 120, 80, elev, new gPoint(5, 3.5, 0), 8, global::VectorDraw.Render.vdRender.Mode.Hide, true);
        Fenetre(doc, lay, "D Add Wire2d", new gPoint(70, 130), 120, 80, elev, new gPoint(5, 3.5, 0), 8, global::VectorDraw.Render.vdRender.Mode.Wire2d, true);
        var e = Fenetre(doc, lay, "E Add ShadeOn ZoomExtents", new gPoint(210, 130), 120, 80, elev, new gPoint(5, 3.5, 0), 8, mode, true);
        e.ZoomExtents();
        e.Update();
        _sortie.WriteLine($"VP E après ZoomExtents : ViewCenter {e.ViewCenter} ViewSize {e.ViewSize.ToString(CultureInfo.InvariantCulture)}");

        // Iso calculée (côté extérieur) : W vers l'observateur (1, -1, 1)/√3.
        double r2 = Math.Sqrt(2), r6 = Math.Sqrt(6), r3 = Math.Sqrt(3);
        var iso = Vue(new double[] { 1 / r2, 1 / r2, 0 }, new double[] { -1 / r6, 1 / r6, 2 / r6 }, new double[] { 1 / r3, -1 / r3, 1 / r3 });
        Fenetre(doc, lay, "F Iso ShadeOn", new gPoint(350, 130), 120, 80, iso, new gPoint(3.5, 2, 0), 10, mode, true);

        // Repères papier attendus pour A (centre (70,230), 80 mm pour 8 m → 10 mm/m) : (0,0), (10,0), (10,6).
        foreach (var (u, v) in new[] { (0.0, 0.0), (10.0, 0.0), (10.0, 6.0) })
        {
            var c = new vdCircle(doc, new gPoint(70 + (u - 5) * 10, 230 + (v - 3.5) * 10), 1.5) { PenColor = new vdColor(Color.Red) };
            lay.Entities.AddItem(c);
        }

        var cote = new vdDimension(doc);
        cote.setDocumentDefaults();
        cote.dimType = global::VectorDraw.Professional.Constants.VdConstDimType.dim_Aligned;
        cote.DefPoint1 = new gPoint(70 - 50, 230 - 35);
        cote.DefPoint2 = new gPoint(70 + 50, 230 - 35);
        cote.LinePosition = new gPoint(70, 230 - 35 - 6);
        cote.TextHeight = 2.5;
        cote.ArrowSize = 2;
        cote.Label = "10,00";
        lay.Entities.AddItem(cote);
        doc.ActiveLayOut = lay;
        lay.ZoomExtents();
        lay.Update();

        using (var bmp = new Bitmap(1680, 1188))
        {
            lay.RenderToBitmap(bmp);
            Image(bmp, "plan-sonde");
        }

        // Repli : espace objet rendu avec la même matrice.
        doc.ActiveLayOut = doc.Model;
        doc.Model.World2ViewMatrix = elev;
        doc.Model.ViewCenter = new gPoint(5, 3.5, 0);
        doc.Model.ViewSize = 8;
        doc.Model.RenderMode = mode;
        using (var bmp = new Bitmap(1200, 800))
        {
            doc.Model.RenderToBitmap(bmp);
            using (var g = Graphics.FromImage(bmp))
            {
                // 800 px pour 8 m : (u - 5) * 100 + 600, 400 - (v - 3.5) * 100
                foreach (var (u, v) in new[] { (0.0, 0.0), (10.0, 0.0), (10.0, 6.0) })
                {
                    g.DrawEllipse(Pens.Red, (float)((u - 5) * 100 + 600 - 6), (float)(400 - (v - 3.5) * 100 - 6), 12, 12);
                }
            }

            Image(bmp, "plan-sonde-modele");
        }

        doc.ActiveLayOut = lay;
        foreach (var (nom, action) in new (string, Action<string>)[]
        {
            ("PrintOut", f => { lay.Printer.PrinterName = f; lay.Printer.DocumentName = f; lay.Printer.Resolution = 300; lay.Printer.PrintScale.Numerator = 1; lay.Printer.PrintScale.Denumerator = 1; lay.Printer.PrintOut(); }),
            ("ExportMultiPagePDF", f =>
            {
                var type = typeof(vdPrint).Assembly.GetTypes().First(t => t.Name == "vdPrintArray");
                _sortie.WriteLine("vdPrintArray = " + type.FullName);
                object a = Activator.CreateInstance(type);
                var ajout = type.GetMethods().First(mi => mi.Name == "AddItem" && mi.GetParameters().Length == 1);
                ajout.Invoke(a, new object[] { lay.Printer });
                ajout.Invoke(a, new object[] { lay.Printer });
                typeof(vdDocument).GetMethod("ExportMultiPagePDF").Invoke(doc, new object[] { a, f });
            }),
        })
        {
            string pdf = Path.Combine(Path.GetTempPath(), "plan-sonde-" + nom + ".pdf");
            try
            {
                File.Delete(pdf);
                action(pdf);
                _sortie.WriteLine("PDF " + nom + " : " + (File.Exists(pdf) ? new FileInfo(pdf).Length + " octets" : "absent"));
                if (File.Exists(pdf))
                {
                    Fichier(File.ReadAllBytes(pdf), "plan-sonde-" + nom + ".pdf");
                }
            }
            catch (Exception ex)
            {
                _sortie.WriteLine("PDF " + nom + " : " + ex.GetBaseException());
            }
        }
    }

    private void Fichier(byte[] octets, string nom)
    {
        var b64 = Convert.ToBase64String(octets);
        _sortie.WriteLine($"IMAGE-DEBUT {nom} {b64.Length}");
        for (int i = 0; i < b64.Length; i += 4000)
        {
            _sortie.WriteLine("IMG " + b64.Substring(i, Math.Min(4000, b64.Length - i)));
        }

        _sortie.WriteLine("IMAGE-FIN " + nom);
    }

    private static string Mat(Matrix m) => string.Format(CultureInfo.InvariantCulture, "[{0:0.###} {1:0.###} {2:0.###} | {3:0.###} {4:0.###} {5:0.###} | {6:0.###} {7:0.###} {8:0.###} | t {9:0.###} {10:0.###} {11:0.###}]",
        m.A00, m.A01, m.A02, m.A10, m.A11, m.A12, m.A20, m.A21, m.A22, m.A03, m.A13, m.A23);

    private void Image(Bitmap bmp, string nom)
    {
        using var flux = new MemoryStream();
        bmp.Save(flux, ImageFormat.Png);
        var b64 = Convert.ToBase64String(flux.ToArray());
        _sortie.WriteLine($"IMAGE-DEBUT {nom} {b64.Length}");
        for (int i = 0; i < b64.Length; i += 4000)
        {
            _sortie.WriteLine("IMG " + b64.Substring(i, Math.Min(4000, b64.Length - i)));
        }

        _sortie.WriteLine("IMAGE-FIN " + nom);
    }
}
