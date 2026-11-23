using System;
using System.Globalization;
using System.IO;
using System.Linq;
using MultiDEC.Core.Facade;
using MultiDEC.Rendering.VectorDraw;
using VectorDraw.Professional.Components;
using Xunit;
using Xunit.Abstractions;

namespace MultiDEC.Rendering.Tests;

/// <summary>
/// Dessine une façade avec la vraie bibliothèque Blocs-EM-3D.dxf (si elle est trouvée sur le poste)
/// et vérifie que chaque famille de pièces tombe dans le volume de l'échafaudage.
/// </summary>
public class FacadeReelleTests
{
    private readonly ITestOutputHelper _sortie;

    public FacadeReelleTests(ITestOutputHelper sortie) => _sortie = sortie;

    /// <summary>MULTIDEC_BIBLIOTHEQUE, sinon le choix fait dans l'application par un utilisateur du poste.</summary>
    private static string? Bibliotheque()
    {
        var env = Environment.GetEnvironmentVariable("MULTIDEC_BIBLIOTHEQUE");
        if (!string.IsNullOrEmpty(env) && File.Exists(env))
        {
            return env;
        }

        try
        {
            foreach (var profil in Directory.GetDirectories(@"C:\Users"))
            {
                var f = Path.Combine(profil, @"AppData\Roaming\MultiDEC\v3\bibliotheque.txt");
                if (File.Exists(f))
                {
                    var chemin = File.ReadAllLines(f).FirstOrDefault()?.Trim();
                    if (!string.IsNullOrEmpty(chemin) && File.Exists(chemin))
                    {
                        return chemin;
                    }
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
        }

        return null;
    }

    [Fact]
    public void Facade_standard_dans_son_volume()
    {
        var chemin = Bibliotheque();
        if (chemin == null)
        {
            _sortie.WriteLine("Bibliothèque Blocs-EM-3D.dxf introuvable sur ce poste : test non exécuté.");
            return;
        }

        _sortie.WriteLine("Bibliothèque : " + chemin);
        var doc = new vdDocumentComponent().Document;
        var rendu = new RenduVectorDraw(doc);
        rendu.ChargerBibliotheque(chemin);

        var p = new ParametresFacade { LongueurTotaleSouhaitee = 10, LongueurMaillePreferee = 2.5, NombreAcces = 1, HauteurDernierPlancher = 6 };
        var projet = ProposeurStructure.NouveauProjet(p);
        projet.Mailles[0].Console = "HMCON700E";
        var structure = GenerateurFacade.Generer(projet);
        var rapport = rendu.Dessiner(structure, TableOrientations.ParDefaut());
        _sortie.WriteLine($"{rapport.Inseres} insérés, manquants : {string.Join(", ", rapport.BlocsManquants)}");

        double l = projet.LongueurObtenue, h = projet.HauteurTotale;
        int hors = 0;
        foreach (var g in rapport.Inserts.GroupBy(x => x.Element.Famille))
        {
            var boites = g.Select(x => x.Insert.BoundingBox).ToList();
            double x0 = boites.Min(b => b.Min.x), y0 = boites.Min(b => b.Min.y), z0 = boites.Min(b => b.Min.z);
            double x1 = boites.Max(b => b.Max.x), y1 = boites.Max(b => b.Max.y), z1 = boites.Max(b => b.Max.z);
            bool dedans = x0 > -0.6 && x1 < l + 0.6 && y0 > -p.LargeurMaille - 0.6 && y1 < 1.3 && z0 > -0.6 && z1 < h + 0.8;
            if (!dedans)
            {
                hors++;
            }

            _sortie.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "{0,-22} {1,4}  X {2:0.00}..{3:0.00}  Y {4:0.00}..{5:0.00}  Z {6:0.00}..{7:0.00}  {8}",
                g.Key, g.Count(), x0, x1, y0, y1, z0, z1, dedans ? "OK" : "HORS VOLUME"));

            foreach (var x in g.Where(x => !Dedans(x.Insert.BoundingBox, l, p.LargeurMaille, h)).Take(3))
            {
                var b = x.Insert.BoundingBox;
                _sortie.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "      ex. {0} origine {1} → X {2:0.00}..{3:0.00} Y {4:0.00}..{5:0.00} Z {6:0.00}..{7:0.00}",
                    x.Element.Reference, x.Element.Origine, b.Min.x, b.Max.x, b.Min.y, b.Max.y, b.Min.z, b.Max.z));
            }
        }

        Assert.Equal(0, hors);
    }

    private static bool Dedans(VectorDraw.Geometry.Box b, double l, double largeur, double h) =>
        b.Min.x > -0.6 && b.Max.x < l + 0.6 && b.Min.y > -largeur - 0.6 && b.Max.y < 1.3 && b.Min.z > -0.6 && b.Max.z < h + 0.8;
}
