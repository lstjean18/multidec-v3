using System.Windows;
using MultiDEC.App.Droits;

namespace MultiDEC.App;

/// <summary>Menu Aide : droits d'utilisation (code du poste, état, demande d'accès).</summary>
public partial class MainWindow
{
    private void DroitsUtilisation_Click(object sender, RoutedEventArgs e) => FenetreDroits.Consulter(this);
}
