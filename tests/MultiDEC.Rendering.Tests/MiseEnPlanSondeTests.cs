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

    [Fact]
    public void Sonde_presentation_A3_et_pdf()
    {
        Membres(typeof(vdViewport), "View|Paper|Model|World|Clip|Freeze|Layer|Render|Hide|Scale|Center|Size");
        Membres(typeof(vdLayout), "View|Paper|Print|Render|Zoom|Space|Clip");
        Membres(typeof(vdPrint), "Print|Paper|Window|Scale|Margin|Resolution|Land|Name|Center|Export|Pdf|PDF");
        Membres(typeof(vdDocument), "PDF|Pdf|Print");
        Membres(typeof(Matrix), "^A[0-3][0-3]$|View|Transform|Invert|Rotate");

        var doc = new vdDocumentComponent().Document;
        var rendu = new RenduVectorDraw(doc);
        rendu.ChargerBibliotheque(Bibliotheque());
        var projet = ProjetStructure.Nouveau(face: new ParametresFacade { LongueurTotaleSouhaitee = 10, LongueurMaillePreferee = 2.5, HauteurDernierPlancher = 6, NombreAcces = 1 });
        rendu.Dessiner(GenerateurStructure.Generer(projet), TableOrientations.ParDefaut());

        var lay = new vdLayout(doc, "Plan-sonde");
        lay.setDocumentDefaults();
        if (doc.LayOuts.FindName("Plan-sonde") == null)
        {
            doc.LayOuts.AddItem(lay);
        }

        lay.BkColorEx = Color.White;
        var papiers = lay.Printer.GetPapers();
        _sortie.WriteLine("PAPIERS : " + string.Join(" | ", papiers.Cast<object>().Take(40)));
        var a3 = papiers.Cast<object>().Select(o => o.ToString()).FirstOrDefault(p => p.ToUpperInvariant().Contains("A3"));
        if (a3 != null)
        {
            lay.Printer.SelectPaper(a3);
        }

        lay.Printer.LandScape = true;
        lay.Printer.PrintWindow = new Box(new gPoint(0, 0), new gPoint(420, 297));
        lay.Entities.AddItem(new vdRect(doc, new gPoint(10, 10), 400, 277, 0));

        // Élévation côté extérieur : U = X, V = Z, W = U x V = -Y (vers l'observateur, côté extérieur).
        var m = new Matrix();
        m.A00 = 1; m.A01 = 0; m.A02 = 0;
        m.A10 = 0; m.A11 = 0; m.A12 = 1;
        m.A20 = 0; m.A21 = -1; m.A22 = 0;
        var vp = new vdViewport(doc);
        vp.setDocumentDefaults();
        vp.Width = 200;
        vp.Height = 120;
        vp.Center = new gPoint(120, 180);
        vp.World2ViewMatrix = m;
        vp.ViewCenter = new gPoint(5, 3.5, 0);
        vp.ViewSize = 12; // 120 mm au 1/100 = 12 m
        vp.RenderMode = global::VectorDraw.Render.vdRender.Mode.ShadeOn;
        lay.Entities.AddItem(vp);
        vp.Update();
        _sortie.WriteLine("VP apres ajout : ViewCenter " + vp.ViewCenter + " ViewSize " + vp.ViewSize.ToString(CultureInfo.InvariantCulture) + " W2V " + Mat(vp.World2ViewMatrix));
        foreach (var p in new[] { new gPoint(0, 0, 0), new gPoint(10, -0.7, 6), new gPoint(5, 0, 3.5) })
        {
            _sortie.WriteLine($"PROJ {p} -> {vp.World2ViewMatrix.Transform(p)}");
        }

        // Repères calculés : papier = centre + (uv - ViewCenter) * 10 mm/m.
        foreach (var (u, v) in new[] { (0.0, 0.0), (10.0, 6.0), (10.0, 0.0) })
        {
            var c = new vdCircle(doc, new gPoint(120 + (u - 5) * 10, 180 + (v - 3.5) * 10), 2);
            c.PenColor = new vdColor(Color.Red);
            lay.Entities.AddItem(c);
        }

        var cote = new vdDimension(doc);
        cote.dimType = global::VectorDraw.Professional.Constants.VdConstDimType.dim_Aligned;
        cote.DefPoint1 = new gPoint(120 + (0 - 5) * 10, 180 + (0 - 3.5) * 10);
        cote.DefPoint2 = new gPoint(120 + (10 - 5) * 10, 180 + (0 - 3.5) * 10);
        cote.LinePosition = new gPoint(120, 180 + (0 - 3.5) * 10 - 10);
        cote.TextHeight = 2.5;
        cote.ArrowSize = 2;
        cote.Label = "10,00";
        lay.Entities.AddItem(cote);
        var t = new vdText(doc) { TextString = "Élévation S1 – échelle 1/100", Height = 4, InsertionPoint = new gPoint(20, 250) };
        lay.Entities.AddItem(t);

        // Iso en bas à droite.
        var iso = new vdViewport(doc);
        iso.setDocumentDefaults();
        iso.Width = 150; iso.Height = 100; iso.Center = new gPoint(320, 80);
        lay.Entities.AddItem(iso);
        doc.ActiveLayOut = lay;
        doc.CommandAction.View3D("VISE");
        iso.World2ViewMatrix = doc.Model.World2ViewMatrix;
        iso.ZoomExtents();
        iso.RenderMode = global::VectorDraw.Render.vdRender.Mode.ShadeOn;
        lay.Update();

        using (var bmp = new Bitmap(1680, 1188))
        {
            lay.RenderToBitmap(bmp);
            Image(bmp, "plan-sonde");
        }

        string pdf = Path.Combine(Path.GetTempPath(), "plan-sonde.pdf");
        try
        {
            File.Delete(pdf);
            lay.Printer.PrinterName = pdf;
            lay.Printer.DocumentName = pdf;
            lay.Printer.Resolution = 200;
            lay.Printer.PrintExtents();
            lay.Printer.PrintScale.Numerator = 1;
            lay.Printer.PrintScale.Denumerator = 1;
            lay.Printer.CenterDrawingToPaper();
            lay.Printer.PrintOut();
            _sortie.WriteLine("PDF : " + (File.Exists(pdf) ? new FileInfo(pdf).Length + " octets" : "absent"));
        }
        catch (Exception ex)
        {
            _sortie.WriteLine("PDF : " + ex.GetBaseException());
        }
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
