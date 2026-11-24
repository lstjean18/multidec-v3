using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace MultiDEC.Core.Decompte;

/// <summary>Article du fichier multidec.bib : désignation, poids, famille et ordre d'affichage d'une référence.</summary>
public sealed class Article
{
    public Article(string reference, string designation, double? poidsUnitaire, string famille, double? classement, string? refClient = null)
    {
        Reference = reference;
        Designation = designation;
        PoidsUnitaire = poidsUnitaire;
        Famille = famille;
        Classement = classement;
        RefClient = refClient;
    }

    public string Reference { get; }

    public string Designation { get; }

    /// <summary>Poids unitaire en kg (absent si non renseigné).</summary>
    public double? PoidsUnitaire { get; }

    public string Famille { get; }

    /// <summary>Ordre d'affichage dans le décompte (ex. 2.06).</summary>
    public double? Classement { get; }

    public string? RefClient { get; }
}

/// <summary>
/// Catalogue des articles lu dans multidec.bib (DataSet .NET sérialisé en XML, table « Tarif »).
/// Les prix de ce fichier (tarif 06/2016) sont obsolètes et volontairement ignorés.
/// </summary>
public sealed class CatalogueArticles
{
    /// <summary>Nom du fichier attendu à côté de la bibliothèque de blocs.</summary>
    public const string NomFichier = "multidec.bib";

    private readonly Dictionary<string, Article> _articles;

    public CatalogueArticles(IEnumerable<Article> articles, string? version = null)
    {
        _articles = new Dictionary<string, Article>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in articles)
        {
            _articles[a.Reference] = a;
        }

        Version = version;
    }

    public static CatalogueArticles Vide { get; } = new CatalogueArticles(Array.Empty<Article>());

    public int Nombre => _articles.Count;

    /// <summary>Version du fichier (Famille de la ligne d'en-tête « TARIF », ex. 2.2.0).</summary>
    public string? Version { get; }

    public IEnumerable<Article> Articles => _articles.Values;

    public bool TryGet(string reference, out Article article) => _articles.TryGetValue(reference, out article!);

    public static CatalogueArticles Charger(string chemin)
    {
        using var flux = File.OpenRead(chemin);
        return Lire(flux);
    }

    public static CatalogueArticles Lire(Stream flux) => Lire(XDocument.Load(flux));

    public static CatalogueArticles LireTexte(string xml) => Lire(XDocument.Parse(xml));

    private static CatalogueArticles Lire(XDocument doc)
    {
        var articles = new List<Article>();
        string? version = null;
        foreach (var t in doc.Root!.Elements().Where(e => e.Name.LocalName == "Tarif"))
        {
            string reference = Texte(t, "Référence")?.Trim() ?? string.Empty;
            if (reference.Length == 0)
            {
                continue;
            }

            if (reference == "TARIF")
            {
                version = Texte(t, "Famille");
                continue;
            }

            articles.Add(new Article(
                reference,
                Texte(t, "Désignation")?.Trim() ?? string.Empty,
                Decimal(t, "Poids_x0020_U"),
                Texte(t, "Famille")?.Trim() ?? string.Empty,
                Decimal(t, "Classement"),
                Texte(t, "RefClient")?.Trim() is { Length: > 0 } rc ? rc : null));
        }

        return new CatalogueArticles(articles, version);
    }

    private static string? Texte(XElement parent, string nom) =>
        parent.Elements().FirstOrDefault(e => e.Name.LocalName == nom)?.Value;

    private static double? Decimal(XElement parent, string nom) =>
        double.TryParse(Texte(parent, nom), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : (double?)null;
}
