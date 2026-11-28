using System;
using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using MultiDEC.App.Droits;
using MultiDEC.Core.Droits;

namespace MultiDEC.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Saisie des cotes avec la virgule française dans tous les champs liés.
        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)));
        Localisation.Installer(); // « . » et « , » acceptés dans les champs décimaux (remarque 64)
        base.OnStartup(e);

        // Erreur imprévue : message et journal plutôt que la fermeture de l'application par Windows.
        DispatcherUnhandledException += (_, args) =>
        {
            Journaliser(args.Exception);
            Boite.Show("Erreur imprévue : " + args.Exception.Message + "\n\nDétail enregistré dans " + CheminJournal,
                            "MultiDEC", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Journaliser(args.ExceptionObject as Exception);

        // Droits d'utilisation (Laurent, 27/11/2026) : liste des postes autorisés lue sur retotub.com pendant la
        // création de la fenêtre principale.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var controleDroits = System.Threading.Tasks.Task.Run(DroitsApplication.Verifier);

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
            Boite.Show("MultiDEC n'a pas pu démarrer : " + ex.GetBaseException().Message + "\n\nDétail enregistré dans " + CheminJournal,
                            "MultiDEC", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        ResultatDroits? droits = null;
        string? erreurDroits = null;
        try
        {
            droits = controleDroits.GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Journaliser(ex);
            erreurDroits = "Vérification des droits impossible : " + ex.GetBaseException().Message;
        }

        if (droits == null || !droits.Autorise)
        {
            demarrage.Close();
            droits = FenetreDroits.Bloquer(droits, DroitsApplication.DernierCode ?? CodeOuVide(), erreurDroits);
            if (droits == null)
            {
                fenetre.Close();
                Shutdown(2);
                return;
            }
        }

        MainWindow = fenetre;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        fenetre.Show();
        if (demarrage.IsVisible)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(demarrage.Close));
        }

        if (droits.Avertissement != null)
        {
            string avertissement = droits.Avertissement;
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                Boite.Show(fenetre, avertissement, "Droits d'utilisation de MultiDEC", MessageBoxButton.OK, MessageBoxImage.Warning)));
        }
    }

    private static string CodeOuVide()
    {
        try
        {
            return DroitsApplication.CodeDuPoste();
        }
        catch (Exception ex)
        {
            Journaliser(ex);
            return "indisponible";
        }
    }

    internal static string CheminJournal =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MultiDEC", "erreurs.log");

    internal static void Journaliser(Exception? ex) => Journaliser(ex?.ToString() ?? string.Empty);

    internal static void Journaliser(string message)
    {
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(CheminJournal)!);
            System.IO.File.AppendAllText(CheminJournal, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}\r\n\r\n");
        }
        catch
        {
            // Journal impossible : rien de plus à faire.
        }
    }
}
