using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MultiDEC.App;
using MultiDEC.App.ViewModels;
using MultiDEC.Core.Facade;
using MultiDEC.Rendering.VectorDraw;
using Xunit;
using Xunit.Abstractions;

namespace MultiDEC.Rendering.Tests;

/// <summary>
/// Captures d'écran du manuel utilisateur (Laurent, 25/11/2026) : la fenêtre WPF est ouverte hors écran sur le serveur,
/// rendue par RenderTargetBitmap, et le dessin VectorDraw (contrôle WinForms, absent d'un rendu WPF) est incrusté à sa
/// place. Les images partent dans le log en base64 (IMAGE-DEBUT / IMG / IMAGE-FIN), comme les rendus 3D.
/// </summary>
public class CapturesManuelTests
{
    private readonly ITestOutputHelper _sortie;

    public CapturesManuelTests(ITestOutputHelper sortie) => _sortie = sortie;

    [Fact]
    public void Captures_du_manuel()
    {
        var dwg = Bibliotheque();
        if (dwg == null)
        {
            _sortie.WriteLine("Captures : bibliothèque « Blocs 3D - Echafaudage.dwg » introuvable, rien à capturer.");
            return;
        }

        Exception? erreur = null;
        var fil = new Thread(() =>
        {
            try
            {
                Capturer(dwg);
            }
            catch (Exception ex)
            {
                erreur = ex;
            }
        });
        fil.SetApartmentState(ApartmentState.STA);
        fil.IsBackground = true; // une boîte de dialogue inattendue ne bloque pas le job
        fil.Start();
        if (!fil.Join(TimeSpan.FromMinutes(8)))
        {
            _sortie.WriteLine("Captures : délai dépassé (boîte de dialogue ouverte ?)");
            return;
        }

        if (erreur != null)
        {
            _sortie.WriteLine("Captures : " + erreur);
        }
    }

    private static string? Bibliotheque()
    {
        var ws = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        if (string.IsNullOrEmpty(ws))
        {
            return null;
        }

        var runner = Path.GetFullPath(Path.Combine(ws, "..", "..", ".."));
        return Directory.GetDirectories(runner)
                        .Where(d => Path.GetFileName(d).StartsWith("biblioth", StringComparison.OrdinalIgnoreCase))
                        .SelectMany(d => Directory.GetFiles(d, "Blocs 3D*.dwg"))
                        .FirstOrDefault();
    }

    private void Capturer(string dwg)
    {
        Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");
        if (Application.Current == null)
        {
            // Pas l'App elle-même : son démarrage ouvrirait une seconde fenêtre (StartupUri). Mêmes styles que App.xaml.
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var groupe = new Style(typeof(GroupBox));
            groupe.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 6)));
            groupe.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(4)));
            app.Resources.Add(typeof(GroupBox), groupe);
            var caseACocher = new Style(typeof(CheckBox));
            caseACocher.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 2, 0, 2)));
            app.Resources.Add(typeof(CheckBox), caseACocher);
            FrameworkElement.LanguageProperty.OverrideMetadata(
                typeof(FrameworkElement), new FrameworkPropertyMetadata(System.Windows.Markup.XmlLanguage.GetLanguage("fr-FR")));
        }

        var w = new MainWindow { WindowState = WindowState.Normal, Width = 1600, Height = 960, Left = 0, Top = 0, ShowActivated = false };
        w.Show();
        Pomper();
        w.ChargerBibliothequePourCapture(dwg);
        var vm = w.ModeleVue;

        // 1. Façade droite par défaut.
        Valider(vm);
        w.DessinerPourCapture();
        Pomper();
        Fenetre(w, "IHM-principale");
        Element(w.Ruban, "IHM-ruban");
        Element(w.MenuPrincipal, "IHM-menu");
        Element(w.Onglets, "IHM-general");
        Onglet(w.Onglets, 1);
        Element(w.Onglets, "IHM-face");
        for (int i = 0; i < 4; i++)
        {
            Onglet(w.OngletsBas, i);
            Element(w.OngletsBas, "IHM-bas-" + i);
        }

        Onglet(w.OngletsBas, 0);
        Dessin(w, Vues.Iso, "3D-droite-iso");
        Dessin(w, Vues.Facade, "3D-droite-facade");

        // 2. Bouton « Valider la structure » rouge après un changement de dimension.
        vm.Parametres.LongueurTotaleSouhaitee = 15;
        Pomper();
        Element(w.Ruban, "IHM-ruban-a-valider");
        Element(w.Onglets, "IHM-face-a-valider");

        // 3. Consoles, pare-gravois et colonnes par niveau.
        vm.ProposerStructure();
        var n4 = vm.Niveaux.FirstOrDefault(n => Math.Abs(n.Hauteur - 4) < 0.01);
        if (n4 != null)
        {
            n4.Console = "HMCON700E";
            n4.PareGravois = true;
        }

        Pomper();
        w.DessinerPourCapture();
        Pomper();
        Element(w.OngletsBas, "IHM-grilles-consoles");
        Dessin(w, Vues.Iso, "3D-consoles-iso");

        // 4. Paramètres par défaut.
        var defauts = new ParametresParDefautWindow(vm.Defauts) { Width = 1100, Height = 820, ShowActivated = false, Left = 0, Top = 0 };
        defauts.Show();
        Pomper();
        Fenetre(defauts, "IHM-parametres-par-defaut");
        defauts.Close();

        // 5. Formes : en L, en U, fermée, inversée, volumique.
        foreach (var (forme, nom) in new[] { (FormeStructure.EnL, "L"), (FormeStructure.EnU, "U"), (FormeStructure.Fermee, "fermee") })
        {
            vm.Forme = forme;
            Pomper();
            Valider(vm);
            w.DessinerPourCapture();
            Pomper();
            Dessin(w, Vues.Iso, "3D-forme-" + nom);
            if (forme == FormeStructure.EnL)
            {
                Element(w.Onglets, "IHM-general-L");
                Fenetre(w, "IHM-forme-L");
                vm.Inversee = true;
                Pomper();
                Valider(vm);
                w.DessinerPourCapture();
                Pomper();
                Dessin(w, Vues.Iso, "3D-forme-L-inversee");
                vm.Inversee = false;
            }
        }

        vm.Forme = FormeStructure.Volumique;
        Pomper();
        vm.NombreModules = 3;
        Pomper();
        Valider(vm);
        w.DessinerPourCapture();
        Pomper();
        Dessin(w, Vues.Iso, "3D-volumique");
        Element(w.Onglets, "IHM-general-volumique");

        w.Close();
    }

    /// <summary>« Valider la structure » sur chaque face (grilles proposées depuis les dimensions).</summary>
    private static void Valider(FacadeViewModel vm)
    {
        for (int i = 0; i < vm.Faces.Count; i++)
        {
            vm.IndexFaceActive = i;
            vm.ProposerStructure();
        }

        vm.IndexFaceActive = 0;
        Pomper();
    }

    private static void Onglet(TabControl onglets, int index)
    {
        onglets.SelectedIndex = index;
        Pomper();
    }

    private static void Pomper()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    /// <summary>Dessin 3D seul, dans une vue donnée.</summary>
    private void Dessin(MainWindow w, Vues vue, string nom)
    {
        w.RenduCourant?.Vue(vue);
        using var bmp = w.ImageDessin(1400, 800);
        using var flux = new MemoryStream();
        bmp.Save(flux, System.Drawing.Imaging.ImageFormat.Png);
        Ecrire(nom, flux.ToArray());
    }

    /// <summary>Fenêtre entière ; pour la fenêtre principale, le dessin VectorDraw est incrusté à sa place.</summary>
    private void Fenetre(Window fenetre, string nom)
    {
        var contenu = (FrameworkElement)fenetre.Content;
        var visuel = new DrawingVisual();
        using (var dc = visuel.RenderOpen())
        {
            dc.DrawRectangle(new VisualBrush(contenu), null, new Rect(0, 0, contenu.ActualWidth, contenu.ActualHeight));
            if (fenetre is MainWindow w)
            {
                var hote = w.HoteVectorDraw;
                var p = hote.TransformToAncestor(contenu).Transform(new Point(0, 0));
                using var bmp = w.ImageDessin((int)hote.ActualWidth, (int)hote.ActualHeight);
                dc.DrawImage(VersWpf(bmp), new Rect(p.X, p.Y, hote.ActualWidth, hote.ActualHeight));
            }
        }

        Ecrire(nom, Png(visuel, contenu.ActualWidth, contenu.ActualHeight));
    }

    private void Element(FrameworkElement element, string nom)
    {
        var visuel = new DrawingVisual();
        using (var dc = visuel.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            dc.DrawRectangle(new VisualBrush(element), null, new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        }

        Ecrire(nom, Png(visuel, element.ActualWidth, element.ActualHeight));
    }

    private static byte[] Png(Visual visuel, double largeur, double hauteur)
    {
        var rtb = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(largeur)), Math.Max(1, (int)Math.Ceiling(hauteur)), 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visuel);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var flux = new MemoryStream();
        enc.Save(flux);
        return flux.ToArray();
    }

    private static BitmapSource VersWpf(System.Drawing.Bitmap bmp)
    {
        using var flux = new MemoryStream();
        bmp.Save(flux, System.Drawing.Imaging.ImageFormat.Png);
        flux.Position = 0;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = flux;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private void Ecrire(string nom, byte[] png)
    {
        var b64 = Convert.ToBase64String(png);
        _sortie.WriteLine($"IMAGE-DEBUT MANUEL-{nom} {b64.Length}");
        for (int i = 0; i < b64.Length; i += 4000)
        {
            _sortie.WriteLine("IMG " + b64.Substring(i, Math.Min(4000, b64.Length - i)));
        }

        _sortie.WriteLine("IMAGE-FIN MANUEL-" + nom);
    }
}
