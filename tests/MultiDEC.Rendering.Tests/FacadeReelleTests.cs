using System;
using System.Globalization;
using System.IO;
using System.Linq;
using MultiDEC.Core.Facade;
using MultiDEC.Rendering.VectorDraw;
using VectorDraw.Geometry;
using VectorDraw.Professional.Components;
using VectorDraw.Professional.vdObjects;
using VectorDraw.Professional.vdPrimaries;
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
        // Fichier ou dossier (on y cherche Blocs-EM-3D.dxf).
        var env = Environment.GetEnvironmentVariable("MULTIDEC_BIBLIOTHEQUE");
        if (!string.IsNullOrEmpty(env))
        {
            if (File.Exists(env))
            {
                return env;
            }

            var dansDossier = Path.Combine(env, "Blocs-EM-3D.dxf");
            if (File.Exists(dansDossier))
            {
                return dansDossier;
            }

            Console.WriteLine($"MULTIDEC_BIBLIOTHEQUE = {env} : dossier visible = {Directory.Exists(env)}, fichier introuvable.");
        }

        // Copie locale à côté du runner (le service tourne sous le compte Système, qui ne voit pas les lecteurs réseau).
        var ws = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        if (!string.IsNullOrEmpty(ws))
        {
            var locale = Path.GetFullPath(Path.Combine(ws, "..", "..", "..", "bibliotheque", "Blocs-EM-3D.dxf"));
            if (File.Exists(locale))
            {
                return locale;
            }

            Console.WriteLine("Copie locale absente : " + locale);
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

        return Extrait();
    }

    /// <summary>Extrait livré avec les tests (19 blocs de Blocs-EM-3D.dxf, même en-tête), décompressé dans %TEMP%.</summary>
    private static string? Extrait()
    {
        var gz = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Donnees", "blocs-facade-test.dxf.gz");
        if (!File.Exists(gz))
        {
            return null;
        }

        var dxf = Path.Combine(Path.GetTempPath(), "multidec-blocs-facade-test.dxf");
        using (var entree = new System.IO.Compression.GZipStream(File.OpenRead(gz), System.IO.Compression.CompressionMode.Decompress))
        using (var sortie = File.Create(dxf))
        {
            entree.CopyTo(sortie);
        }

        return dxf;
    }

    [Fact]
    public void Facade_standard_dans_son_volume()
    {
        var chemin = Bibliotheque();
        if (chemin == null)
        {
            var env = Environment.GetEnvironmentVariable("MULTIDEC_BIBLIOTHEQUE");
            _sortie.WriteLine($"Bibliothèque Blocs-EM-3D.dxf introuvable sur ce poste : test non exécuté. MULTIDEC_BIBLIOTHEQUE = '{env}', dossier visible = {(!string.IsNullOrEmpty(env) && Directory.Exists(env))}, compte = {Environment.UserName}.");
            if (!string.IsNullOrEmpty(env) && Directory.Exists(env))
            {
                foreach (var f in Directory.GetFiles(env, "*.dxf").Take(20))
                {
                    _sortie.WriteLine("   " + Path.GetFileName(f));
                }
            }

            return;
        }

        _sortie.WriteLine("Bibliothèque : " + chemin);
        var doc = new vdDocumentComponent().Document;
        var rendu = new RenduVectorDraw(doc);
        var avant = new vdDocumentComponent().Document;
        avant.Open(chemin);
        _sortie.WriteLine("--- Document juste ouvert (avant préparation) ---");
        DecrireRepere(avant);

        rendu.ChargerBibliotheque(chemin);
        _sortie.WriteLine("--- Après ChargerBibliotheque ---");
        DecrireRepere(doc);
        foreach (var ligne in rendu.Diagnostic)
        {
            _sortie.WriteLine(ligne);
        }

        DecrireBloc(doc, "HMPLA250298CL6");
        DecrireBloc(doc, "HMPOT20");
        DecrireBloc(doc, "HMGCSE250");

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

        // Vue iso : Z monde doit monter à l'écran, X monde doit aller vers la droite (SCU du DXF neutralisé).
        rendu.Vue(Vues.Iso);
        var m = doc.World2ViewMatrix;
        var o = m.Transform(new gPoint(0, 0, 0));
        var vz = m.Transform(new gPoint(0, 0, 1));
        var vx = m.Transform(new gPoint(1, 0, 0));
        _sortie.WriteLine(string.Format(CultureInfo.InvariantCulture, "Vue iso : X→({0:0.##};{1:0.##})  Z→({2:0.##};{3:0.##})", vx.x - o.x, vx.y - o.y, vz.x - o.x, vz.y - o.y));

        ExporterImages(doc, rendu);

        Assert.Equal(0, hors);
        Assert.True(vz.y - o.y > 0.5 && Math.Abs(vz.x - o.x) < 0.2, "Z monde n'est pas vertical à l'écran");
        Assert.True(vx.x - o.x > 0.5, "X monde ne va pas vers la droite à l'écran");
    }

    /// <summary>Propriétés de repère / vue du document et de l'espace objet, et projection des axes monde en vue iso.</summary>
    private void DecrireRepere(vdDocument d)
    {
        foreach (var cible in new object[] { d, d.Model })
        {
            foreach (var prop in cible.GetType().GetProperties())
            {
                var n = prop.Name;
                bool interessant = n.IndexOf("UCS", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("User", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("ViewDir", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("World2", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("RenderMode", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("ViewCenter", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!interessant || prop.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                string valeur;
                try
                {
                    var v = prop.GetValue(cible);
                    valeur = v is Matrix m ? Axes(m) : (v?.ToString() ?? "null");
                }
                catch (Exception ex)
                {
                    valeur = "!" + ex.GetBaseException().Message;
                }

                _sortie.WriteLine($"   {cible.GetType().Name}.{n} [{(prop.CanWrite ? "rw" : "r")}] = {valeur}");
            }
        }

        try
        {
            d.CommandAction.View3D("VISE");
            _sortie.WriteLine("   VISE : World2ViewMatrix " + Axes(d.World2ViewMatrix));
        }
        catch (Exception ex)
        {
            _sortie.WriteLine("   VISE : " + ex.GetBaseException().Message);
        }

        var ucs = d.CommandAction.GetType().GetMethods().Where(x => x.Name.IndexOf("UCS", StringComparison.OrdinalIgnoreCase) >= 0)
            .Select(x => x.Name + "(" + string.Join(", ", x.GetParameters().Select(pp => pp.ParameterType.Name)) + ")").Distinct();
        _sortie.WriteLine("   CommandAction UCS : " + string.Join(" | ", ucs));
    }

    private static string Axes(Matrix m)
    {
        var o = m.Transform(new gPoint(0, 0, 0));
        string A(double x, double y, double z)
        {
            var p = m.Transform(new gPoint(x, y, z));
            return string.Format(CultureInfo.InvariantCulture, "({0:0.##};{1:0.##};{2:0.##})", p.x - o.x, p.y - o.y, p.z - o.z);
        }

        return $"X→{A(1, 0, 0)} Y→{A(0, 1, 0)} Z→{A(0, 0, 1)}";
    }

    private void DecrireBloc(vdDocument doc, string nom)
    {
        var bloc = doc.Blocks.FindName(nom);
        if (bloc == null)
        {
            _sortie.WriteLine($"Bloc {nom} absent");
            return;
        }

        var types = new System.Collections.Generic.List<string>();
        foreach (vdFigure f in bloc.Entities)
        {
            types.Add($"{f.GetType().Name}@{f.Layer?.Name}");
        }

        _sortie.WriteLine($"Bloc {nom} : {string.Join(", ", types.GroupBy(t => t).Select(g => g.Count() + "×" + g.Key))}");
    }

    /// <summary>
    /// Essaie d'enregistrer le dessin en image (vue iso, rendu réaliste puis ombré) dans « rendu-serveur »,
    /// publié par le workflow : permet de voir le rendu sans lancer l'application.
    /// </summary>
    private void ExporterImages(vdDocument doc, RenduVectorDraw rendu)
    {
        var dossier = Path.Combine(Environment.GetEnvironmentVariable("GITHUB_WORKSPACE") ?? Path.GetTempPath(), "rendu-serveur");
        Directory.CreateDirectory(dossier);
        foreach (var (mode, nom) in new[] { (ModeRendu.Realiste, "realiste"), (ModeRendu.OmbreAretes, "ombre-aretes"), (ModeRendu.Filaire, "filaire") })
        {
            rendu.Rendu(mode);
            foreach (var ext in new[] { ".png", ".bmp", ".jpg" })
            {
                var f = Path.Combine(dossier, "iso-" + nom + ext);
                try
                {
                    bool ok = doc.SaveAs(f);
                    _sortie.WriteLine($"Image {Path.GetFileName(f)} : SaveAs={ok}, {(File.Exists(f) ? new FileInfo(f).Length + " octets" : "absente")}");
                    if (ok && File.Exists(f))
                    {
                        break;
                    }
                }
                catch (Exception ex)
                {
                    _sortie.WriteLine($"Image {Path.GetFileName(f)} : {ex.GetBaseException().Message}");
                }
            }
        }

        // Pistes pour un rendu hors écran si SaveAs ne sait pas faire d'image.
        foreach (var t in new[] { doc.GetType(), doc.Model.GetType() })
        {
            var noms = t.GetMethods().Where(x => x.Name.IndexOf("Render", StringComparison.OrdinalIgnoreCase) >= 0 || x.Name.IndexOf("Image", StringComparison.OrdinalIgnoreCase) >= 0 || x.Name.IndexOf("Bitmap", StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(x => x.Name + "(" + string.Join(", ", x.GetParameters().Select(pp => pp.ParameterType.Name)) + ")").Distinct();
            _sortie.WriteLine($"{t.Name} : {string.Join(" | ", noms)}");
        }
    }

    private static bool Dedans(global::VectorDraw.Geometry.Box b, double l, double largeur, double h) =>
        b.Min.x > -0.6 && b.Max.x < l + 0.6 && b.Min.y > -largeur - 0.6 && b.Max.y < 1.3 && b.Min.z > -0.6 && b.Max.z < h + 0.8;
}
