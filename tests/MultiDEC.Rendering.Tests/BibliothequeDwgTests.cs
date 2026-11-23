using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using MultiDEC.Core.Facade;
using MultiDEC.Rendering.VectorDraw;
using VectorDraw.Professional.Components;
using VectorDraw.Professional.vdObjects;
using VectorDraw.Professional.vdPrimaries;
using Xunit;
using Xunit.Abstractions;

namespace MultiDEC.Rendering.Tests;

/// <summary>
/// Bibliothèque DWG fournie par Laurent (Bloc3D.dwg, 23/11/2026) : VectorDraw la lit par vdxFcnv,
/// qui convertit les solides 3D. Le test décrit ce qui est chargé, dessine la façade de test
/// et écrit une image du rendu dans le journal (base64) pour contrôle visuel.
/// </summary>
public class BibliothequeDwgTests
{
    private readonly ITestOutputHelper _sortie;

    public BibliothequeDwgTests(ITestOutputHelper sortie) => _sortie = sortie;

    /// <summary>Bloc3D.dwg livré avec les tests, puis les DWG déposés dans actions-runner\bibliotheque (ou bibliothèque).</summary>
    private static List<string> Fichiers()
    {
        var liste = new List<string> { Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Donnees", "Bloc3D.dwg") };
        var ws = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        if (!string.IsNullOrEmpty(ws))
        {
            var runner = Path.GetFullPath(Path.Combine(ws, "..", "..", ".."));
            foreach (var d in Directory.GetDirectories(runner).Where(d => Path.GetFileName(d).StartsWith("biblioth", StringComparison.OrdinalIgnoreCase)))
            {
                liste.AddRange(Directory.GetFiles(d, "*.dwg"));
            }
        }

        return liste;
    }

    [Fact]
    public void Facades_avec_les_dwg()
    {
        foreach (var f in Fichiers())
        {
            _sortie.WriteLine("===== " + f + " (" + new FileInfo(f).Length + " octets)");
            try
            {
                Facade_avec_le_dwg(f);
            }
            catch (Exception ex)
            {
                _sortie.WriteLine("ERREUR : " + ex.GetBaseException());
            }
        }
    }

    private void Facade_avec_le_dwg(string dwg)
    {
        var fichier = Path.GetFileName(dwg);
        var doc = new vdDocumentComponent().Document;
        var rendu = new RenduVectorDraw(doc);
        rendu.ChargerBibliotheque(dwg);
        foreach (var l in rendu.Diagnostic)
        {
            _sortie.WriteLine(l);
        }

        var types = new Dictionary<string, int>();
        int blocs = 0;
        foreach (vdBlock b in doc.Blocks)
        {
            blocs++;
            foreach (vdFigure f in b.Entities)
            {
                var k = f.GetType().Name + "@" + f.Layer?.Name;
                types[k] = types.TryGetValue(k, out var c) ? c + 1 : 1;
            }
        }

        _sortie.WriteLine($"{blocs} blocs ; entités : {string.Join(", ", types.OrderByDescending(x => x.Value).Take(20).Select(x => x.Value + "×" + x.Key))}");
        _sortie.WriteLine("Blocs : " + string.Join(" ", doc.Blocks.Cast<vdBlock>().Select(b => b.Name).Where(n => !n.StartsWith("*")).Take(600)));

        foreach (var nom in new[] { "HMPLA250298CL6", "HMPOT20", "HMGCSE250", "HMSOCREG" })
        {
            var b = doc.Blocks.FindName(nom);
            _sortie.WriteLine(b == null ? $"{nom} : absent" : $"{nom} : {string.Join(", ", b.Entities.Cast<vdFigure>().GroupBy(f => f.GetType().Name + "@" + f.Layer?.Name).Select(g => g.Count() + "×" + g.Key))}");
        }

        var p = new ParametresFacade { LongueurTotaleSouhaitee = 10, LongueurMaillePreferee = 2.5, NombreAcces = 1, HauteurDernierPlancher = 6 };
        var projet = ProposeurStructure.NouveauProjet(p);
        projet.Mailles[0].Console = "HMCON700E";
        var rapport = rendu.Dessiner(GenerateurFacade.Generer(projet), TableOrientations.ParDefaut());
        _sortie.WriteLine($"{rapport.Inseres} insérés, manquants : {string.Join(", ", rapport.BlocsManquants)}");
        foreach (var g in rapport.Inserts.GroupBy(x => x.Element.Famille))
        {
            var bx = g.Select(x => x.Insert.BoundingBox).ToList();
            _sortie.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0,-20} {1,3}  X {2:0.00}..{3:0.00}  Y {4:0.00}..{5:0.00}  Z {6:0.00}..{7:0.00}",
                g.Key, g.Count(), bx.Min(b => b.Min.x), bx.Max(b => b.Max.x), bx.Min(b => b.Min.y), bx.Max(b => b.Max.y), bx.Min(b => b.Min.z), bx.Max(b => b.Max.z)));
        }

        foreach (var mode in new[] { ModeRendu.OmbreAretes, ModeRendu.Realiste })
        {
            rendu.Rendu(mode);
            rendu.Vue(Vues.Iso);
            Image(doc, Path.GetFileNameWithoutExtension(fichier) + "-" + mode);
        }
    }

    private void Image(vdDocument doc, string nom)
    {
        try
        {
            using var bmp = new Bitmap(1400, 800);
            doc.Model.RenderToBitmap(bmp);
            using var petit = new MemoryStream();
            bmp.Save(petit, ImageFormat.Png);
            var b64 = Convert.ToBase64String(petit.ToArray());
            _sortie.WriteLine($"IMAGE-DEBUT {nom} {b64.Length}");
            for (int i = 0; i < b64.Length; i += 4000)
            {
                _sortie.WriteLine("IMG " + b64.Substring(i, Math.Min(4000, b64.Length - i)));
            }

            _sortie.WriteLine("IMAGE-FIN " + nom);
        }
        catch (Exception ex)
        {
            _sortie.WriteLine($"Image {nom} : {ex.GetBaseException().GetType().Name} : {ex.GetBaseException().Message}");
        }
    }
}
