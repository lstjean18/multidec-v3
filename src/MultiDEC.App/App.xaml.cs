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

        // Erreur imprévue : message et journal plutôt que la fermeture de l'application par Windows.
        DispatcherUnhandledException += (_, args) =>
        {
            Journaliser(args.Exception);
            MessageBox.Show("Erreur imprévue : " + args.Exception.Message + "\n\nDétail enregistré dans " + CheminJournal,
                            "MultiDEC", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Journaliser(args.ExceptionObject as Exception);

        // Écran de démarrage peint avant la création de la fenêtre principale (VectorDraw, bibliothèque de blocs),
        // fermé quand l'application devient inactive, donc une fois la bibliothèque chargée.
        var demarrage = new EcranDemarrage();
        demarrage.Show();
        demarrage.Dispatcher.Invoke(DispatcherPriority.Render, new Action(() => { }));

        MainWindow fenetre;
        try
        {
            fenetre = new MainWindow();
        }
        catch (Exception ex)
        {
            Journaliser(ex);
            demarrage.Close();
            MessageBox.Show("MultiDEC n'a pas pu démarrer : " + ex.GetBaseException().Message + "\n\nDétail enregistré dans " + CheminJournal,
                            "MultiDEC", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        MainWindow = fenetre;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        fenetre.Show();
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(demarrage.Close));
    }

    internal static string CheminJournal =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MultiDEC", "erreurs.log");

    internal static void Journaliser(Exception? ex)
    {
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(CheminJournal)!);
            System.IO.File.AppendAllText(CheminJournal, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {ex}\r\n\r\n");
        }
        catch
        {
            // Journal impossible : rien de plus à faire.
        }
    }
}
