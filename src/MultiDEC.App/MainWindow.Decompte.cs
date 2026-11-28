using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MultiDEC.Core.Decompte;

namespace MultiDEC.App;

/// <summary>
/// Décompte modifiable (remarque 61, Laurent 27/11/2026) : quantités modifiées et références ajoutées à la main,
/// enregistrées dans le projet ; les totaux suivent.
/// </summary>
public partial class MainWindow
{
    private void GrilleDecompte_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit || e.Column != ColonneQuantite || e.Row.Item is not LigneDecompte ligne)
        {
            return;
        }

        string texte = (e.EditingElement as TextBox)?.Text?.Trim() ?? string.Empty;
        bool valide = int.TryParse(texte, NumberStyles.Integer, CultureInfo.CurrentCulture, out int quantite) && quantite >= 0;
        string reference = ligne.Reference;

        // Recalcul après la fin de l'édition (la grille ne doit pas être en cours de transaction quand on la remplit).
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            GrilleDecompte.CancelEdit(DataGridEditingUnit.Row);
            if (!valide)
            {
                Boite.Show(this, $"« {texte} » n'est pas une quantité valide (nombre entier, 0 ou plus).", "Décompte",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Vm.ModifierQuantite(reference, quantite);
            Selectionner(reference);
        }));
    }

    private void SaisieDecompte_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            AjouterReference_Click(sender, e);
        }
    }

    private void AjouterReference_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.Structure == null)
        {
            Boite.Show(this, "Générez d'abord la structure : le décompte est calculé à partir du dessin.", "Décompte",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string saisie = SaisieReference.Text.Trim();
        if (saisie.Length == 0)
        {
            SaisieReference.Focus();
            return;
        }

        if (!int.TryParse(SaisieQuantite.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out int quantite) || quantite <= 0)
        {
            Boite.Show(this, "Indiquez une quantité entière supérieure à 0.", "Décompte", MessageBoxButton.OK, MessageBoxImage.Warning);
            SaisieQuantite.Focus();
            SaisieQuantite.SelectAll();
            return;
        }

        string? reference = Vm.ReferenceSaisie(saisie);
        if (reference == null)
        {
            var reponse = Boite.Show(this,
                $"« {saisie} » est introuvable dans multidec.bib et dans les références client." + Environment.NewLine
                + "L'ajouter quand même, sans désignation ni poids ?",
                "Décompte", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (reponse != MessageBoxResult.Yes)
            {
                return;
            }

            reference = saisie.ToUpperInvariant();
        }

        GrilleDecompte.CommitEdit(DataGridEditingUnit.Row, true);
        bool dejaPresente = Vm.Decompte.Any(l => l.Quantite > 0 && string.Equals(l.Reference, reference, StringComparison.OrdinalIgnoreCase));
        var ligne = Vm.AjouterAuDecompte(reference, quantite);
        if (ligne != null)
        {
            Selectionner(ligne.Reference);
            if (dejaPresente)
            {
                Boite.Show(this, $"{ligne.ReferenceAffichee} était déjà dans le décompte : sa quantité passe à {ligne.Quantite}.", "Décompte",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        SaisieReference.Clear();
        SaisieQuantite.Text = "1";
        SaisieReference.Focus();
    }

    private void RetablirLigne_Click(object sender, RoutedEventArgs e)
    {
        if (GrilleDecompte.SelectedItem is not LigneDecompte ligne)
        {
            Boite.Show(this, "Sélectionnez la ligne à rétablir.", "Décompte", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!ligne.Ajoutee && !ligne.Modifiee)
        {
            return;
        }

        GrilleDecompte.CancelEdit(DataGridEditingUnit.Row);
        Vm.RetablirLigne(ligne.Reference);
        Selectionner(ligne.Reference);
    }

    private void RetablirDecompte_Click(object sender, RoutedEventArgs e)
    {
        if (Boite.Show(this, "Retirer toutes les quantités modifiées et les références ajoutées à la main ?", "Décompte",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes)
        {
            GrilleDecompte.CancelEdit(DataGridEditingUnit.Row);
            Vm.RetablirDecompte();
        }
    }

    /// <summary>Sélectionne et montre la ligne d'une référence (après un recalcul du décompte).</summary>
    private void Selectionner(string reference)
    {
        var ligne = Vm.Decompte.FirstOrDefault(l => string.Equals(l.Reference, reference, StringComparison.OrdinalIgnoreCase));
        GrilleDecompte.SelectedItem = ligne;
        if (ligne != null)
        {
            GrilleDecompte.ScrollIntoView(ligne);
        }
    }
}
