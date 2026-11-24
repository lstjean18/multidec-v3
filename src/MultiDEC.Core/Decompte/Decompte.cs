using System;
using System.Collections.Generic;
using System.Linq;
using MultiDEC.Core.Facade;

namespace MultiDEC.Core.Decompte;

/// <summary>Une ligne du décompte matériel.</summary>
public sealed class LigneDecompte
{
    public LigneDecompte(string reference, Famille famille, int quantite, Article? article = null)
    {
        Reference = reference;
        Famille = famille;
        Quantite = quantite;
        Article = article;
    }

    public string Reference { get; }

    /// <summary>Famille interne (type de pièce dans le générateur).</summary>
    public Famille Famille { get; }

    public int Quantite { get; }

    /// <summary>Article de multidec.bib (null si la référence n'y figure pas).</summary>
    public Article? Article { get; }

    /// <summary>Famille affichée : celle de multidec.bib, sinon la famille interne.</summary>
    public string FamilleAffichee => string.IsNullOrEmpty(Article?.Famille) ? Famille.ToString() : Article!.Famille;

    public string Designation => Article?.Designation ?? string.Empty;

    /// <summary>Poids unitaire en kg (null si inconnu).</summary>
    public double? PoidsUnitaire => Article?.PoidsUnitaire;

    /// <summary>Poids total de la ligne en kg (null si le poids unitaire est inconnu).</summary>
    public double? PoidsTotal => PoidsUnitaire * Quantite;

    public override string ToString() => $"{Reference} x{Quantite}";
}

/// <summary>
/// Décompte calculé à partir du modèle (et non plus en relisant les blocs du dessin comme en v2).
/// Désignations, poids et ordre d'affichage viennent de multidec.bib quand il est chargé (pas les prix, obsolètes).
/// </summary>
public static class CalculDecompte
{
    public static IReadOnlyList<LigneDecompte> Calculer(IEnumerable<ElementPlace> elements, CatalogueArticles? catalogue = null)
    {
        catalogue ??= CatalogueArticles.Vide;
        return elements.GroupBy(e => e.Reference, StringComparer.OrdinalIgnoreCase)
                       .Select(g => new LigneDecompte(g.First().Reference, g.First().Famille, g.Count(),
                                                      catalogue.TryGet(g.Key, out var a) ? a : null))
                       // Ordre du fichier (Classement) ; les références absentes du fichier à la fin.
                       .OrderBy(l => l.Article?.Classement ?? double.MaxValue)
                       .ThenBy(l => l.Famille)
                       .ThenBy(l => l.Reference, StringComparer.Ordinal)
                       .ToList();
    }

    /// <summary>Poids total du décompte en kg (lignes de poids connu).</summary>
    public static double PoidsTotal(IEnumerable<LigneDecompte> lignes) => lignes.Sum(l => l.PoidsTotal ?? 0);

    /// <summary>Références du décompte absentes de multidec.bib ou sans poids.</summary>
    public static IEnumerable<string> SansPoids(IEnumerable<LigneDecompte> lignes) =>
        lignes.Where(l => l.PoidsUnitaire == null).Select(l => l.Reference);
}
