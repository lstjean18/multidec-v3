using System;
using System.Windows;

namespace MultiDEC.App;

/// <summary>Fenêtre « À propos » : logo, version, emplacements et licences des composants tiers.</summary>
public partial class AProposWindow : Window
{
    /// <summary>Mentions des composants tiers livrés avec MultiDEC (ou utilisés pour le publier).</summary>
    internal const string Licences =
        "VectorDraw Developer Framework 9.9003 – © VectorDraw. Licence commerciale de RETOTUB, licence d'exécution intégrée.\n\n"
        + "PdfiumViewer 2.13 – © Pieter van Ginkel. Licence Apache 2.0.\n\n"
        + "PDFium (moteur PDF, paquet PdfiumViewer.Native) – © Google et contributeurs PDFium. Licences BSD 3 clauses et Apache 2.0.\n\n"
        + "Inno Setup (installateur) – © Jordan Russell et Martijn Laan. Licence Inno Setup, utilisation commerciale gratuite.\n\n"
        + ".NET Framework 4.8 et WPF – © Microsoft.";

    public AProposWindow()
    {
        InitializeComponent();
        TexteTitre.Text = $"MultiDEC {VersionApplication.Libelle}";
        TexteVersion.Text = $"{VersionApplication.Texte} ({MiseAJourApplication.VersionInstallee})";
        TexteDossier.Text = AppDomain.CurrentDomain.BaseDirectory;
        TexteMisesAJour.Text = MiseAJourApplication.Adresse ?? "adresse non définie";
        TexteJournal.Text = App.CheminJournal;
        TexteLicences.Text = Licences;
    }
}
