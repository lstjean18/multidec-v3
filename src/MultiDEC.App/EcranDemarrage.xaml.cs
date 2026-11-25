using System.Windows;

namespace MultiDEC.App;

/// <summary>Écran de démarrage affiché pendant la création de la fenêtre principale et le chargement de la bibliothèque.</summary>
public partial class EcranDemarrage : Window
{
    public EcranDemarrage()
    {
        InitializeComponent();
        TexteVersion.Text = VersionApplication.Texte;
    }
}
