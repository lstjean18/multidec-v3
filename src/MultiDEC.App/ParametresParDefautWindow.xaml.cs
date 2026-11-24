using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using MultiDEC.App.ViewModels;
using MultiDEC.Core.Persistance;

namespace MultiDEC.App;

/// <summary>Écran « Paramètres par défaut » (menu Outils) : rend les paramètres modifiés par <see cref="Resultat"/>.</summary>
public partial class ParametresParDefautWindow : Window
{
    private const string FiltreExport = "Paramètres MultiDEC (*.mdcpar)|*.mdcpar|JSON (*.json)|*.json";

    private readonly ParametresParDefautViewModel _vm;

    public ParametresParDefautWindow(ParametresParDefaut actuels)
    {
        InitializeComponent();
        _vm = new ParametresParDefautViewModel(actuels);
        DataContext = _vm;
    }

    public ParametresParDefaut Resultat => _vm.Resultat;

    private void Enregistrer_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Importer_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = FiltreExport + "|Tous les fichiers|*.*" };
        if (dlg.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            _vm.Remplacer(ParametresParDefaut.DepuisJson(File.ReadAllText(dlg.FileName)));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Import impossible : " + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Exporter_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { Filter = FiltreExport, FileName = "parametres-multidec" };
        if (dlg.ShowDialog(this) == true)
        {
            _vm.Resultat.Enregistrer(dlg.FileName);
        }
    }

    private void Usine_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Revenir aux paramètres d'usine ? Les fichiers de référence sont aussi effacés.", Title,
                            MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK)
        {
            _vm.Remplacer(ParametresParDefaut.Usine());
        }
    }

    private void ChoisirFichier_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not FichierParDefaut fichier)
        {
            return;
        }

        var dlg = new OpenFileDialog { Filter = fichier.Filtre };
        if (!string.IsNullOrEmpty(fichier.Chemin) && File.Exists(fichier.Chemin))
        {
            dlg.InitialDirectory = Path.GetDirectoryName(fichier.Chemin);
        }

        if (dlg.ShowDialog(this) == true)
        {
            fichier.Chemin = dlg.FileName;
        }
    }

    private void EffacerFichier_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is FichierParDefaut fichier)
        {
            fichier.Chemin = null;
        }
    }
}
