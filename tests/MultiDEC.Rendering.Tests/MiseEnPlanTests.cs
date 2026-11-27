using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using MultiDEC.Core.Facade;
using MultiDEC.Core.MiseEnPlan;
using MultiDEC.Rendering.VectorDraw;
using VectorDraw.Professional.Components;
using Xunit;
using Xunit.Abstractions;

namespace MultiDEC.Rendering.Tests;

/// <summary>
/// Mise en plan sur le serveur (27/11/2026) : présentations A3, PDF multipage. Les feuilles sont écrites dans le log
/// (images base64) pour être contrôlées depuis le conteneur, ainsi que le PDF de la façade droite.
/// </summary>
public class MiseEnPlanTests
{
    private readonly ITestOutputHelper _sortie;

    public MiseEnPlanTests(ITestOutputHelper sortie) => _sortie = sortie;

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

    [Theory]
    [InlineData("droite", false)]
    [InlineData("L", true)]
    public void Plan_pdf_A3(string nom, bool enL)
    {
        var doc = new vdDocumentComponent().Document;
        var rendu = new RenduVectorDraw(doc);
        rendu.ChargerBibliotheque(Bibliotheque());
        var projet = ProjetStructure.Nouveau(face: new ParametresFacade { LongueurTotaleSouhaitee = 15, LongueurMaillePreferee = 2.5, HauteurDernierPlancher = 6, NombreAcces = 1 });
        projet.Client = "Client essai";
        projet.Chantier = "Chantier essai";
        if (enL)
        {
            projet.Forme = FormeStructure.EnL;
            projet.AjusterFaces();
        }

        var structure = GenerateurStructure.Generer(projet);
        rendu.Dessiner(structure, TableOrientations.ParDefaut());
        var cartouche = Cartouche.De(projet, structure, "MultiDEC v3.0 bêta 1 – essai serveur", 1234, null, o => o.ToString());
        cartouche.EtabliPar = "Essai";
        var plan = GenerateurPlan.Generer(projet, structure, cartouche);
        var feuilles = MiseEnPage.Feuilles(plan);

        var mep = new MiseEnPlanVectorDraw(doc) { Resolution = 150 };
        var pages = mep.Composer(feuilles);
        Assert.Equal(plan.Pages.Count, pages.Count);
        foreach (var lay in pages)
        {
            doc.ActiveLayOut = lay;
            lay.ZoomExtents();
            using var bmp = new Bitmap(1680, 1188);
            lay.RenderToBitmap(bmp);
            Fichier(Png(bmp), $"plan-{nom}-{lay.Name.Replace(' ', '-')}.png");
        }

        string pdf = Path.Combine(Path.GetTempPath(), $"plan-{nom}.pdf");
        mep.ExporterPdf(pages, pdf);
        foreach (var d in mep.Diagnostic)
        {
            _sortie.WriteLine("DIAG " + d);
        }

        long taille = new FileInfo(pdf).Length;
        _sortie.WriteLine($"PDF {nom} : {taille} octets, {pages.Count} feuilles");
        if (taille < 4_000_000)
        {
            Fichier(File.ReadAllBytes(pdf), $"plan-{nom}.pdf");
        }

        Assert.True(taille > 10_000);
    }

    /// <summary>
    /// Remarque 59 (Laurent, 27/11/2026, « génération des plans relativement lente ») : temps de chaque étape de la
    /// composition, variantes (sans mise à jour des fenêtres, sans zoom) et export PDF ; feuille 2 en image pour
    /// contrôler que la variante dessine toujours les vues.
    /// </summary>
    [Theory]
    [InlineData("droite", false)]
    [InlineData("L", true)]
    public void Chronometre_mise_en_plan(string nom, bool enL)
    {
        var chrono = System.Diagnostics.Stopwatch.StartNew();
        void Etape(string etape)
        {
            _sortie.WriteLine($"CHRONO {nom} {etape} : {chrono.ElapsedMilliseconds} ms");
            chrono.Restart();
        }

        var doc = new vdDocumentComponent().Document;
        var rendu = new RenduVectorDraw(doc);
        rendu.ChargerBibliotheque(Bibliotheque());
        Etape("chargement bibliothèque");
        var projet = ProjetStructure.Nouveau(face: new ParametresFacade { LongueurTotaleSouhaitee = 15, LongueurMaillePreferee = 2.5, HauteurDernierPlancher = 6, NombreAcces = 1 });
        if (enL)
        {
            projet.Forme = FormeStructure.EnL;
            projet.AjusterFaces();
        }

        var structure = GenerateurStructure.Generer(projet);
        rendu.Dessiner(structure, TableOrientations.ParDefaut());
        Etape("dessin 3D");
        var cartouche = Cartouche.De(projet, structure, "MultiDEC v3 – chrono", 1234, null, o => o.ToString());
        var feuilles = MiseEnPage.Feuilles(GenerateurPlan.Generer(projet, structure, cartouche));
        Etape($"plan ({feuilles.Count} feuilles, {feuilles.Sum(f => f.Page.Vues.Count)} vues)");

        foreach (var (variante, majFenetres, zoom) in new[] { ("actuel", true, true), ("sans maj fenêtres", false, true), ("sans maj ni zoom", false, false), ("actuel bis", true, true) })
        {
            var mep = new MiseEnPlanVectorDraw(doc) { MettreAJourFenetres = majFenetres, ZoomPresentations = zoom, Resolution = 200 };
            var pages = mep.Composer(feuilles);
            Etape($"composition {variante} [{string.Join(", ", mep.Temps.Select(kv => kv.Key + " " + kv.Value))}]");
            string pdf = Path.Combine(Path.GetTempPath(), $"chrono-{nom}.pdf");
            mep.ExporterPdf(pages, pdf);
            Etape($"export PDF 200 dpi {variante} : {new FileInfo(pdf).Length / 1024} Ko");
            if (variante != "actuel bis")
            {
                doc.ActiveLayOut = pages[1];
                pages[1].ZoomExtents();
                using var bmp = new Bitmap(840, 594);
                pages[1].RenderToBitmap(bmp);
                Fichier(Png(bmp), $"chrono-{nom}-{variante.Replace(' ', '-')}.png");
                doc.ActiveLayOut = doc.Model;
                Etape($"image {variante}");
            }
        }
    }

    /// <summary>Liste du matériel A4 (27/11/2026) : page 1 en image et PDF dans le log.</summary>
    [Fact]
    public void Liste_materiel_pdf_A4()
    {
        var projet = ProjetStructure.Nouveau(face: new ParametresFacade { LongueurTotaleSouhaitee = 15, LongueurMaillePreferee = 2.5, HauteurDernierPlancher = 6, NombreAcces = 1 });
        var structure = GenerateurStructure.Generer(projet);
        var bib = Path.Combine(Path.GetDirectoryName(Bibliotheque())!, "multidec.bib");
        var catalogue = File.Exists(bib) ? MultiDEC.Core.Decompte.CatalogueArticles.Charger(bib) : MultiDEC.Core.Decompte.CatalogueArticles.Vide;
        var lignes = MultiDEC.Core.Decompte.CalculDecompte.Calculer(structure.Elements, catalogue);
        var entete = new EnTeteListe
        {
            Chantier = "Chantier essai – façade nord",
            Client = "Client essai",
            ReferenceEtude = "ET-2026-001",
        };
        var alertes = structure.Alertes.Concat(ListeMateriel.AlertesDecompte(lignes, null)).ToList();
        var feuilles = ListeMateriel.Feuilles(entete, lignes, alertes);

        var doc = new vdDocumentComponent().Document;
        var mep = new MiseEnPlanVectorDraw(doc);
        var pages = mep.Composer(feuilles);
        doc.ActiveLayOut = pages[0];
        pages[0].ZoomExtents();
        using (var bmp = new Bitmap(840, 1188))
        {
            pages[0].RenderToBitmap(bmp);
            Fichier(Png(bmp), "liste-materiel-1.png");
        }

        string pdf = Path.Combine(Path.GetTempPath(), "liste-materiel.pdf");
        foreach (var d in MiseEnPlanVectorDraw.ExporterFeuillesPdf(feuilles, pdf))
        {
            _sortie.WriteLine("DIAG " + d);
        }

        long taille = new FileInfo(pdf).Length;
        _sortie.WriteLine($"LISTE : {lignes.Count} lignes (catalogue {catalogue.Nombre} articles), {feuilles.Count} pages, PDF {taille} octets");
        Fichier(File.ReadAllBytes(pdf), "liste-materiel.pdf");
        Assert.True(taille > 2_000);
    }

    private static byte[] Png(Bitmap bmp)
    {
        using var flux = new MemoryStream();
        bmp.Save(flux, ImageFormat.Png);
        return flux.ToArray();
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
}
