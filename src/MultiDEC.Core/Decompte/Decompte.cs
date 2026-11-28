using System;
using System.Collections.Generic;
using System.Linq;
using MultiDEC.Core.Facade;

namespace MultiDEC.Core.Decompte;

/// <summary>Une ligne du décompte matériel.</summary>
public sealed class LigneDecompte
{
    public LigneDecompte(string reference, Famille famille, int quantite, Article? article = null, string? referenceClient = null,
                         int? quantiteCalculee = null)
    {
        Reference = reference;
        Famille = famille;
        Quantite = quantite;
        Article = article;
        ReferenceClient = referenceClient;
        QuantiteCalculee = quantiteCalculee ?? quantite;
    }

    /// <summary>Référence RETOTUB.</summary>
    public string Reference { get; }

    /// <summary>Référence du client quand on travaille en références client (null sinon, ou sans correspondance).</summary>
    public string? ReferenceClient { get; }

    /// <summary>Référence affichée : celle du client si elle est connue, sinon la référence RETOTUB.</summary>
    public string ReferenceAffichee => ReferenceClient ?? Reference;

    /// <summary>Famille interne (type de pièce dans le générateur).</summary>
    public Famille Famille { get; }

    /// <summary>
    /// Quantité retenue (modifiable dans l'onglet « Décompte », remarque 61 : l'écran enregistre la nouvelle valeur
    /// dans les corrections du projet puis recalcule le décompte).
    /// </summary>
    public int Quantite { get; set; }

    /// <summary>Quantité calculée à partir du modèle (0 pour une référence ajoutée à la main).</summary>
    public int QuantiteCalculee { get; }

    /// <summary>Référence ajoutée à la main (absente du modèle) : repérée par un astérisque dans la liste du matériel.</summary>
    public bool Ajoutee => QuantiteCalculee == 0;

    /// <summary>Quantité modifiée à la main (différente de la quantité calculée).</summary>
    public bool Modifiee => !Ajoutee && Quantite != QuantiteCalculee;

    /// <summary>Repère de la ligne dans l'onglet « Décompte » : « * » ajoutée, « M » quantité modifiée.</summary>
    public string Repere => Ajoutee ? "*" : Modifiee ? "M" : string.Empty;

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

/// <summary>Quantité imposée à la main pour une référence du décompte (modifiée ou ajoutée, remarque 61).</summary>
public sealed class CorrectionDecompte
{
    /// <summary>Référence RETOTUB.</summary>
    public string Reference { get; set; } = string.Empty;

    /// <summary>Quantité retenue (remplace la quantité calculée).</summary>
    public int Quantite { get; set; }
}

/// <summary>
/// Décompte calculé à partir du modèle (et non plus en relisant les blocs du dessin comme en v2).
/// Désignations, poids et ordre d'affichage viennent de multidec.bib quand il est chargé (pas les prix, obsolètes).
/// </summary>
public static class CalculDecompte
{
    /// <param name="client">Travail en références client : correspondance RETOTUB → client (null = références RETOTUB).</param>
    /// <param name="corrections">
    /// Quantités imposées à la main (remarque 61) : elles remplacent la quantité calculée ; une référence absente du modèle
    /// est ajoutée (quantité 0 : correction ignorée).
    /// </param>
    public static IReadOnlyList<LigneDecompte> Calculer(IEnumerable<ElementPlace> elements, CatalogueArticles? catalogue = null,
                                                        ReferencesClient? client = null, IEnumerable<CorrectionDecompte>? corrections = null)
    {
        catalogue ??= CatalogueArticles.Vide;
        var imposees = new Dictionary<string, CorrectionDecompte>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in corrections ?? Enumerable.Empty<CorrectionDecompte>())
        {
            if (!string.IsNullOrWhiteSpace(c.Reference))
            {
                imposees[c.Reference.Trim()] = c; // la dernière l'emporte
            }
        }

        Article? ArticleDe(string reference) => catalogue.TryGet(reference, out var a) ? a : null;
        string? ClientDe(string reference) => client != null && client.TryGet(reference, out var rc) ? rc : null;

        var lignes = elements.GroupBy(e => e.Reference, StringComparer.OrdinalIgnoreCase)
                             .Select(g =>
                             {
                                 int calculee = g.Count();
                                 int quantite = imposees.TryGetValue(g.Key, out var c) ? Math.Max(0, c.Quantite) : calculee;
                                 return new LigneDecompte(g.First().Reference, g.First().Famille, quantite, ArticleDe(g.Key), ClientDe(g.Key), calculee);
                             })
                             .ToList();

        var presentes = new HashSet<string>(lignes.Select(l => l.Reference), StringComparer.OrdinalIgnoreCase);
        foreach (var c in imposees.Values.Where(c => c.Quantite > 0 && !presentes.Contains(c.Reference.Trim())))
        {
            string reference = c.Reference.Trim();
            lignes.Add(new LigneDecompte(reference, Famille.Divers, c.Quantite, ArticleDe(reference), ClientDe(reference), 0));
        }

        // Ordre du fichier (Classement) ; les références absentes du fichier à la fin.
        return lignes.OrderBy(l => l.Article?.Classement ?? double.MaxValue)
                     .ThenBy(l => l.Famille)
                     .ThenBy(l => l.Reference, StringComparer.Ordinal)
                     .ToList();
    }

    /// <summary>
    /// Référence RETOTUB correspondant à une saisie (remarque 61) : référence RETOTUB de multidec.bib, sinon référence du
    /// client affiché, sinon référence client indiquée dans multidec.bib. Null si la saisie n'est reconnue nulle part.
    /// </summary>
    public static string? ReferenceRetotub(string saisie, CatalogueArticles? catalogue, ReferencesClient? client)
    {
        string texte = (saisie ?? string.Empty).Trim();
        if (texte.Length == 0)
        {
            return null;
        }

        catalogue ??= CatalogueArticles.Vide;
        if (catalogue.TryGet(texte, out var article))
        {
            return article.Reference;
        }

        if (client != null && client.TryGetRetotub(texte, out var retotub))
        {
            return catalogue.TryGet(retotub, out var a) ? a.Reference : retotub;
        }

        var parRefClient = catalogue.Articles.FirstOrDefault(a => string.Equals(a.RefClient?.Trim(), texte, StringComparison.OrdinalIgnoreCase));
        return parRefClient?.Reference;
    }

    /// <summary>
    /// Enregistre une quantité imposée : remplace la correction existante de la référence ; une quantité égale à la
    /// quantité calculée (ou 0 pour une référence ajoutée) supprime la correction.
    /// </summary>
    public static void Imposer(List<CorrectionDecompte> corrections, string reference, int quantite, int quantiteCalculee)
    {
        corrections.RemoveAll(c => string.Equals(c.Reference?.Trim(), reference, StringComparison.OrdinalIgnoreCase));
        quantite = Math.Max(0, quantite);
        if (quantite != quantiteCalculee)
        {
            corrections.Add(new CorrectionDecompte { Reference = reference, Quantite = quantite });
        }
    }

    /// <summary>Poids total du décompte en kg (lignes de poids connu).</summary>
    public static double PoidsTotal(IEnumerable<LigneDecompte> lignes) => lignes.Sum(l => l.PoidsTotal ?? 0);

    /// <summary>Références RETOTUB du décompte sans correspondance chez le client.</summary>
    public static IEnumerable<string> SansReferenceClient(IEnumerable<LigneDecompte> lignes) =>
        lignes.Where(l => l.ReferenceClient == null).Select(l => l.Reference);

    /// <summary>Références du décompte absentes de multidec.bib ou sans poids.</summary>
    public static IEnumerable<string> SansPoids(IEnumerable<LigneDecompte> lignes) =>
        lignes.Where(l => l.PoidsUnitaire == null).Select(l => l.Reference);
}
