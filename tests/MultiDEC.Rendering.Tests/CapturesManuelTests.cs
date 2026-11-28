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
using MultiDEC.Core.Persistance;
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

        // Écran de démarrage (version et build en bas à droite).
        try
        {
            var ecran = new EcranDemarrage { ShowActivated = false, Topmost = false };
            ecran.Show();
            Pomper();
            Fenetre(ecran, "IHM-splash");
            ecran.Close();
        }
        catch (Exception ex)
        {
            _sortie.WriteLine("Écran de démarrage non capturé : " + ex.Message);
        }

        _sortie.WriteLine("Ressources de MultiDEC.exe : " + string.Join(", ", typeof(MainWindow).Assembly.GetManifestResourceNames()));
        var w = new MainWindow { WindowState = WindowState.Normal, Width = 1600, Height = 960, Left = 0, Top = 0, ShowActivated = false };
        w.Show();
        Pomper();
        w.ChargerBibliothequePourCapture(dwg);
        var vm = w.ModeleVue;
        ChangerProtections(w);

        // 1. Façade droite par défaut.
        Valider(vm);
        w.DessinerPourCapture();
        Pomper();
        Fenetre(w, "IHM-principale");
        Disposer(w);
        Element(w.Ruban, "IHM-ruban");
        Element(w.Onglets, "IHM-general");
        Defilant(w.Onglets, "IHM-general-complet");
        Onglet(w.Onglets, 1);
        Disposer(w);
        Element(w.Onglets, "IHM-face");
        Defilant(w.Onglets, "IHM-face-complet");
        for (int i = 0; i < 3; i++)
        {
            Onglet(w.OngletsBas, i);
            Disposer(w);
            Element(w.OngletsBas, "IHM-bas-" + i);
        }

        Onglet(w.OngletsBas, 0);
        Dessin(w, Vues.Iso, "3D-droite-iso");
        Dessin(w, Vues.Facade, "3D-droite-facade");

        // 2. Bouton « Valider la structure » rouge après un changement de dimension.
        vm.LongueurFacade = 15;
        Pomper();
        Disposer(w);
        Element(w.Ruban, "IHM-ruban-a-valider");
        Defilant(w.Onglets, "IHM-face-a-valider");

        // 3. Consoles, pare-gravois et colonnes par niveau.
        vm.ProposerStructure();
        var n4 = vm.Niveaux.FirstOrDefault(n => Math.Abs(n.Hauteur - 4) < 0.01);
        if (n4 != null)
        {
            n4.Console = "HMCON700E";
            n4.PareGravois = true;
        }

        // Colonnes particulières de la grille des mailles (départ, stabilisation, poutre refusée sur la 1re maille).
        if (vm.Mailles.Count > 5)
        {
            vm.Mailles[4].HauteurDepart = 0.5;
            vm.Mailles[5].Stabilisation = 1.5;
            vm.Mailles[5].Lest = true; // colonne « Lest » (point 49)
            vm.Mailles[0].Poutre = 4;
        }

        Pomper();
        w.DessinerPourCapture();
        Pomper();
        Onglet(w.OngletsBas, 0);
        Disposer(w);
        Element(w.OngletsBas, "IHM-grilles-consoles");
        Element(w.GrilleMailles, "IHM-grille-mailles");
        Onglet(w.OngletsBas, 3);
        Disposer(w);
        Element(w.OngletsBas, "IHM-alertes");
        Onglet(w.OngletsBas, 1);
        Disposer(w);
        Element(w.OngletsBas, "IHM-decompte-consoles");

        // Décompte en références client LV Tec.
        vm.ReferencesAffichees = MultiDEC.Core.Decompte.ReferencesClient.CodeLvTec;
        Pomper();
        Disposer(w);
        Element(w.OngletsBas, "IHM-decompte-lvtec");
        vm.ReferencesAffichees = MultiDEC.Core.Decompte.ReferencesClient.Retotub;
        Pomper();

        // Décompte modifié à la main (remarque 61) : une quantité changée, une référence ajoutée.
        if (vm.Decompte.Count > 2)
        {
            var ligne = vm.Decompte[1];
            vm.ModifierQuantite(ligne.Reference, ligne.Quantite + 4);
            // Référence de petit classement : la ligne ajoutée apparaît en haut du tableau.
            var ajout = vm.Articles.Articles.Where(a => a.PoidsUnitaire != null && a.Classement != null && a.Reference.StartsWith("HM", StringComparison.Ordinal)
                                                        && !vm.Decompte.Any(l => l.Reference == a.Reference))
                                            .OrderBy(a => a.Classement).FirstOrDefault();
            if (ajout != null)
            {
                vm.AjouterAuDecompte(ajout.Reference, 6);
            }

            Pomper();
            Disposer(w);
            Element(w.OngletsBas, "IHM-decompte-modifie");
            vm.RetablirDecompte();
            Pomper();
        }

        Onglet(w.OngletsBas, 0);
        Dessin(w, Vues.Iso, "3D-consoles-iso");
        Dessin(w, Vues.Facade, "3D-consoles-facade");

        // 4. Paramètres par défaut.
        // Chemins tels qu'après une installation (bibliothèque et multidec.bib livrés, remarque 62).
        var exemple = vm.Defauts.Copie();
        string installe = @"C:\Users\utilisateur\AppData\Local\Programs\RETOTUB\MultiDEC v3\Bibliotheque\";
        exemple.Bibliotheque ??= installe + ParametresParDefaut.NomBibliothequeInstallee;
        exemple.Articles ??= installe + "multidec.bib";
        var defauts = new ParametresParDefautWindow(exemple) { Width = 1100, Height = 820, ShowActivated = false, Left = 0, Top = 0 };
        defauts.Show();
        Pomper();
        Fenetre(defauts, "IHM-parametres-par-defaut");
        var ongletsDefauts = Descendants<TabControl>(defauts).FirstOrDefault();
        if (ongletsDefauts != null)
        {
            for (int i = 0; i < ongletsDefauts.Items.Count; i++)
            {
                Onglet(ongletsDefauts, i);
                Defilant(ongletsDefauts, "IHM-parametres-onglet-" + i);
            }
        }

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
                Disposer(w);
        Defilant(w.Onglets, "IHM-general-L");
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

        Disposer(w);
        Defilant(w.Onglets, "IHM-general-fermee");
        vm.Forme = FormeStructure.Volumique;
        Pomper();
        vm.NombreModules = 3;
        Pomper();
        Valider(vm);
        w.DessinerPourCapture();
        Pomper();
        Dessin(w, Vues.Iso, "3D-volumique");
        Disposer(w);
        Defilant(w.Onglets, "IHM-general-volumique");

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

    /// <summary>
    /// Mise en page à 1600 × 960 : le bureau du service (session 0) est plus petit et Windows y réduit la fenêtre.
    /// À appeler juste avant une capture (la fenêtre reprend sa taille à la mise en page suivante).
    /// </summary>
    private static void Disposer(Window w)
    {
        var contenu = (FrameworkElement)w.Content;
        contenu.Measure(new Size(1600, 940));
        contenu.Arrange(new Rect(0, 0, 1600, 940)); // pas d'UpdateLayout : la fenêtre reprendrait sa taille
    }

    /// <summary>Contenu entier (sans la barre de défilement) du panneau défilant de l'onglet sélectionné.</summary>
    private void Defilant(TabControl onglets, string nom)
    {
        var defileur = Descendants<ScrollViewer>(onglets).Where(v => v.IsVisible && v.Content is Panel)
                                                         .OrderByDescending(v => v.ActualWidth * v.ActualHeight).FirstOrDefault();
        if (defileur?.Content is FrameworkElement contenu)
        {
            Element(contenu, nom);
        }
        else
        {
            Element(onglets, nom);
        }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject racine) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(racine); i++)
        {
            var enfant = VisualTreeHelper.GetChild(racine, i);
            if (enfant is T t)
            {
                yield return t;
            }

            foreach (var d in Descendants<T>(enfant))
            {
                yield return d;
            }
        }
    }

    private static void Onglet(TabControl onglets, int index)
    {
        onglets.SelectedIndex = index;
        Pomper();
    }

    /// <summary>
    /// Plantage signalé par Laurent (25/11/2026, 23h03) en changeant garde-corps ou plinthe intérieurs : on change chaque liste
    /// de l'onglet « Général » comme un utilisateur (SelectedItem du ComboBox), puis on vérifie la valeur dans le modèle.
    /// </summary>
    private void ChangerProtections(MainWindow w)
    {
        Onglet(w.Onglets, 0);
        Pomper();
        var listes = Descendants<ComboBox>(w)
            .Select(c => (c, chemin: System.Windows.Data.BindingOperations.GetBinding(c, System.Windows.Controls.Primitives.Selector.SelectedItemProperty)?.Path?.Path))
            .Where(x => x.chemin is "SecuriteExterieure" or "SecuriteInterieure" or "SecuriteAbouts" or "PlintheExterieure" or "PlintheInterieure" or "PlintheAbouts")
            .ToList();
        Console.WriteLine($"Protections : {listes.Count} listes trouvées");
        foreach (var (liste, chemin) in listes)
        {
            foreach (var valeur in liste.Items.Cast<object>().ToList())
            {
                Console.WriteLine($"Protections : {chemin} = {valeur}");
                liste.SelectedItem = valeur;
                Pomper();
                var lu = typeof(FacadeViewModel).GetProperty(chemin!)!.GetValue(w.ModeleVue);
                if (!Equals(valeur, lu))
                {
                    _sortie.WriteLine($"Protections : {chemin} choisi {valeur}, modèle {lu}");
                }
            }
        }

        _sortie.WriteLine($"Protections : {listes.Count} listes changées sans plantage");
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
        if (fenetre is MainWindow)
        {
            Disposer(fenetre);
        }

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
