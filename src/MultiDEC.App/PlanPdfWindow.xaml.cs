using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using PdfiumViewer;

namespace MultiDEC.App;

/// <summary>Viewer intégré des plans PDF (PDFium).</summary>
public partial class PlanPdfWindow : Window
{
    private readonly string _chemin;
    private readonly string _nomPropose;
    private readonly PdfDocument _document;

    public PlanPdfWindow(string cheminPdf, string nomPropose, string info, string titre = "Mise en plan")
    {
        InitializeComponent();
        Title = titre;
        _chemin = cheminPdf;
        _nomPropose = nomPropose;

        // Chargé depuis la mémoire : le fichier reste libre (enregistrement, ouverture externe).
        _document = PdfDocument.Load(new MemoryStream(File.ReadAllBytes(cheminPdf)));
        var viewer = new PdfViewer { Document = _document, ShowToolbar = true, ZoomMode = PdfViewerZoomMode.FitBest };
        Hote.Child = viewer;
        TexteInfo.Text = info;
        Closed += (s, e) => _document.Dispose();
    }

    private void EnregistrerSous_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { Filter = "Plan PDF (*.pdf)|*.pdf", FileName = _nomPropose, DefaultExt = ".pdf" };
        if (dlg.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            File.Copy(_chemin, dlg.FileName, overwrite: true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Enregistrer le plan", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Imprimer_Click(object sender, RoutedEventArgs e)
    {
        using var impression = _document.CreatePrintDocument();
        using var dlg = new System.Windows.Forms.PrintDialog { Document = impression, UseEXDialog = true, AllowSomePages = true };
        impression.DefaultPageSettings.Landscape = true;
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            impression.Print();
        }
    }

    private void OuvrirExterne_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(_chemin) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Aucun lecteur PDF n'a pu ouvrir le plan : " + ex.Message, "Mise en plan", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
