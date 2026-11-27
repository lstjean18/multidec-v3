using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using MultiDEC.App.ViewModels;
using MultiDEC.Core.Decompte;
using MultiDEC.Core.MiseEnPlan;
using MultiDEC.Rendering.VectorDraw;

namespace MultiDEC.App;

/// <summary>
/// Mise en plan (Laurent, 27/11/2026) : feuilles A3 (dessus, élévations, profils, isos, détails) cotées, avec
/// cartouche, exportées en PDF et affichées dans le viewer intégré.
/// </summary>
public partial class MainWindow
{
    private void MiseEnPlan_Click(object sender, RoutedEventArgs e)
    {
        if (_rendu == null)
        {
            MessageBox.Show(this, "La mise en plan a besoin de la bibliothèque de blocs (Blocs 3D - Echafaudage.dwg) : choisissez-la dans Fichier.",
                "Mise en plan", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        int aValider = Vm.PremiereFaceAValider();
        if (aValider >= 0)
        {
            Onglets.SelectedIndex = aValider + 1;
            MessageBox.Show(this, $"Les paramètres de {Vm.Faces[aValider].Nom} ont changé : cliquez sur « Valider la structure » (bouton rouge) avant la mise en plan.",
                "Mise en plan", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            // Le plan montre la structure telle qu'elle est dessinée : on la redessine d'abord.
            var structure = Vm.Generer();
            _rendu.Dessiner(structure, _orientations);
            _rendu.DessinerFacade(Vm.MursDuBatiment(), Vm.DessinerFacade);
            MettreEnEvidence();

            var projet = Vm.Projet;
            var cartouche = Cartouche.De(projet, structure, $"MultiDEC {VersionApplication.Libelle} – build {VersionApplication.Build}",
                CalculDecompte.PoidsTotal(Vm.Decompte), Vm.Defauts.LogoClient, Libelles.De);
            cartouche.EtabliPar = string.IsNullOrWhiteSpace(Vm.Defauts.EtabliPar) ? Environment.UserName : Vm.Defauts.EtabliPar!;
            var plan = GenerateurPlan.Generer(projet, structure, cartouche);
            var feuilles = MiseEnPage.Feuilles(plan);

            var mep = new MiseEnPlanVectorDraw(_rendu.Document);
            var pages = mep.Composer(feuilles);
            string dossier = Path.Combine(Path.GetTempPath(), "MultiDEC", "Plans");
            Directory.CreateDirectory(dossier);
            string nom = NomDuPlan(projet.Chantier, projet.Client);
            string chemin = Path.Combine(dossier, $"{nom}-{DateTime.Now:yyyyMMdd-HHmmss}.pdf");
            mep.ExporterPdf(pages, chemin);
            foreach (var d in mep.Diagnostic)
            {
                App.Journaliser("Mise en plan : " + d);
            }

            Mouse.OverrideCursor = null;
            new PlanPdfWindow(chemin, nom + ".pdf", $"{pages.Count} feuilles A3 – {chemin}") { Owner = this }.Show();
        }
        catch (Exception ex)
        {
            Mouse.OverrideCursor = null;
            App.Journaliser("Mise en plan : " + ex);
            MessageBox.Show(this, "La mise en plan a échoué : " + ex.GetBaseException().Message, "Mise en plan", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    /// <summary>Guide utilisateur livré avec l'application, ouvert dans le viewer intégré.</summary>
    private void Guide_Click(object sender, RoutedEventArgs e)
    {
        string chemin = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MultiDEC-v3-Guide-utilisateur.pdf");
        if (!File.Exists(chemin))
        {
            MessageBox.Show(this, "Le guide utilisateur est absent du dossier de MultiDEC : " + chemin, "Guide utilisateur", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            new PlanPdfWindow(chemin, "MultiDEC-v3-Guide-utilisateur.pdf", "Guide utilisateur MultiDEC v3", "Guide utilisateur") { Owner = this }.Show();
        }
        catch (Exception ex)
        {
            App.Journaliser(ex);
            MessageBox.Show(this, "Le guide n'a pas pu s'ouvrir : " + ex.GetBaseException().Message, "Guide utilisateur", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Nom de fichier du plan : « Plan - chantier » (caractères interdits remplacés).</summary>
    internal static string NomDuPlan(string? chantier, string? client)
    {
        string libelle = !string.IsNullOrWhiteSpace(chantier) ? chantier! : !string.IsNullOrWhiteSpace(client) ? client! : "structure";
        var interdits = Path.GetInvalidFileNameChars();
        return "Plan - " + new string(libelle.Trim().Select(c => interdits.Contains(c) ? '_' : c).ToArray());
    }
}
