using System;
using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;

namespace MultiDEC.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Saisie des cotes avec la virgule française dans tous les champs liés.
        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)));
        base.OnStartup(e);

        // Écran de démarrage peint avant la création de la fenêtre principale (VectorDraw, bibliothèque de blocs),
        // fermé quand l'application devient inactive, donc une fois la bibliothèque chargée.
        var demarrage = new EcranDemarrage();
        demarrage.Show();
        demarrage.Dispatcher.Invoke(DispatcherPriority.Render, new Action(() => { }));

        var fenetre = new MainWindow();
        MainWindow = fenetre;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        fenetre.Show();
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(demarrage.Close));
    }
}
