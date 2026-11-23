using System;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using MultiDEC.App.ViewModels;
using MultiDEC.Core.Persistance;
using MultiDEC.Rendering.VectorDraw;

namespace MultiDEC.App;

/// <summary>
/// Fenêtre principale : héberge le contrôle VectorDraw (WinForms) et relie le ViewModel au rendu.
/// </summary>
public partial class MainWindow : Window
{
    private const string FiltreProjet = "Projet MultiDEC v3 (*" + FichierProjet.Extension + ")|*" + FichierProjet.Extension;

    private readonly vdScrollableControl.vdScrollableControl _vd = new vdScrollableControl.vdScrollableControl();
    private readonly TableOrientations _orientations;
    private RenduVectorDraw? _rendu;

    public MainWindow()
    {
        InitializeComponent();
        HoteVectorDraw.Child = _vd;
        _orientations = TableOrientations.Charger(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "orientations-blocs.json"));
        Loaded += (_, _) => ChargerBibliotheque(Reglages.CheminBibliotheque);
    }

    private FacadeViewModel Vm => (FacadeViewModel)DataContext;

    private void ChargerBibliotheque(string? chemin)
    {
        if (string.IsNullOrEmpty(chemin) || !File.Exists(chemin))
        {
            TexteBibliotheque.Text = "Bibliothèque de blocs non choisie (Fichier > Bibliothèque de blocs…)";
            return;
        }

        try
        {
            _vd.BaseControl.EnsureDocument();
            _rendu = new RenduVectorDraw(_vd.BaseControl.ActiveDocument);
            _rendu.ChargerBibliotheque(chemin!);
            Reglages.CheminBibliotheque = chemin;
            TexteBibliotheque.Text = "Bibliothèque : " + Path.GetFileName(chemin);
        }
        catch (Exception ex)
        {
            _rendu = null;
            MessageBox.Show(this, ex.Message, "Bibliothèque de blocs", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Dessiner_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var structure = Vm.Generer();
            if (_rendu == null)
            {
                MessageBox.Show(this, "Choisissez d'abord la bibliothèque de blocs (Blocs-EM-3D.dxf). Le décompte est calculé.", "Dessin", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var rapport = _rendu.Dessiner(structure, _orientations);
            if (rapport.BlocsManquants.Count > 0)
            {
                Vm.Alertes.Add("Blocs absents de la bibliothèque : " + string.Join(", ", rapport.BlocsManquants));
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Dessin de la façade", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Gamme_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => Vm.GammeChangee();

    private void Nouveau_Click(object sender, RoutedEventArgs e) => Vm.Nouveau();

    private void Ouvrir_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = FiltreProjet };
        if (dlg.ShowDialog(this) == true)
        {
            try
            {
                Vm.Ouvrir(dlg.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Ouvrir", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void Enregistrer_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.CheminFichier == null)
        {
            EnregistrerSous_Click(sender, e);
            return;
        }

        Vm.Enregistrer(Vm.CheminFichier);
    }

    private void EnregistrerSous_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { Filter = FiltreProjet, FileName = string.IsNullOrEmpty(Vm.Projet.ReferenceEtude) ? "facade" : Vm.Projet.ReferenceEtude };
        if (dlg.ShowDialog(this) == true)
        {
            Vm.Enregistrer(dlg.FileName);
        }
    }

    private void Bibliotheque_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Bibliothèque de blocs (*.dxf;*.dwg)|*.dxf;*.dwg" };
        if (dlg.ShowDialog(this) == true)
        {
            ChargerBibliotheque(dlg.FileName);
        }
    }
}

/// <summary>Réglages du poste, dans %APPDATA%\MultiDEC\v3\ (un fichier texte par réglage).</summary>
internal static class Reglages
{
    private static readonly string Dossier = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MultiDEC", "v3");

    public static string? CheminBibliotheque
    {
        get => Lire("bibliotheque.txt");
        set => Ecrire("bibliotheque.txt", value);
    }

    private static string? Lire(string nom)
    {
        string f = Path.Combine(Dossier, nom);
        return File.Exists(f) ? File.ReadAllLines(f).FirstOrDefault() : null;
    }

    private static void Ecrire(string nom, string? valeur)
    {
        Directory.CreateDirectory(Dossier);
        File.WriteAllText(Path.Combine(Dossier, nom), valeur ?? string.Empty);
    }
}
