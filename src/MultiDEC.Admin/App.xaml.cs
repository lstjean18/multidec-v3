using System;
using System.IO;
using System.Windows;

namespace MultiDEC.Admin;

public partial class App : Application
{
    internal static string CheminJournal =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MultiDEC-Admin", "erreurs.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            Journaliser(args.Exception.ToString());
            MessageBox.Show("Erreur imprévue : " + args.Exception.GetBaseException().Message + "\n\nDétail enregistré dans " + CheminJournal,
                            "MultiDEC Admin", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
    }

    internal static void Journaliser(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CheminJournal)!);
            File.AppendAllText(CheminJournal, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}\r\n\r\n");
        }
        catch (IOException)
        {
        }
    }
}
