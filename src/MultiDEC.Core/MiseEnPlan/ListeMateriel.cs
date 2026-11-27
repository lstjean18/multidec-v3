using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MultiDEC.Core.Decompte;

namespace MultiDEC.Core.MiseEnPlan;

/// <summary>En-tête et pied de la liste du matériel.</summary>
public sealed class EnTeteListe
{
    public string Chantier { get; set; } = string.Empty;
    public string Client { get; set; } = string.Empty;
    public string ReferenceEtude { get; set; } = string.Empty;
    public DateTime Date { get; set; } = DateTime.Today;

    /// <summary>Chemin du logo du client (Paramètres par défaut), null si aucun.</summary>
    public string? LogoClient { get; set; }

    /// <summary>Nom des références client affichées (ex. « LV Tec »), null en références RETOTUB.</summary>
    public string? NomReferencesClient { get; set; }

    public string Titre { get; set; } = "LISTE DU MATÉRIEL";

    public string Pied { get; set; } = "Décompte réalisé avec MultiDEC v3";
}

/// <summary>
/// Liste du matériel en PDF A4 portrait (Laurent, 27/11/2026) : en-tête (logo client, chantier, client, référence étude,
/// date), tableau du décompte par famille (mêmes informations que l'onglet Décompte), totaux, alertes en bas du tableau,
/// pied « Décompte réalisé avec MultiDEC v3 ». Mise en page en primitives (mm), tracée par le rendu comme les plans.
/// </summary>
public static class ListeMateriel
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>Format des nombres : virgule décimale, espace ordinaire comme séparateur de milliers (police du PDF).</summary>
    private static readonly NumberFormatInfo Nombres = Format();

    public const double Gauche = 12;
    public const double Droite = 198;
    public const double HauteurTexte = 2.1;
    private const double Interligne = 3.4;
    private const double MargeCellule = 1.5;
    private const double HautZone = 245;
    private const double BasZone = 20;

    private sealed class Colonne
    {
        public Colonne(string titre, double largeur, bool aDroite, Func<LigneDecompte, string> valeur)
        {
            Titre = titre;
            Largeur = largeur;
            ADroite = aDroite;
            Valeur = valeur;
        }

        public string Titre { get; }
        public double Largeur { get; set; }
        public bool ADroite { get; }
        public Func<LigneDecompte, string> Valeur { get; }
        public double X0 { get; set; }
        public double X1 => X0 + Largeur;
    }

    /// <summary>Feuilles A4 de la liste (au moins une, même sans matériel).</summary>
    public static List<FeuilleDessinee> Feuilles(EnTeteListe entete, IReadOnlyList<LigneDecompte> lignes, IEnumerable<string> alertes)
    {
        var colonnes = Colonnes(entete);
        var feuilles = new List<FeuilleDessinee>();
        FeuilleDessinee f = null!;
        double y = 0;

        void NouvellePage(bool enteteTableau)
        {
            f = new FeuilleDessinee(new PageDePlan("Liste du matériel") { Numero = feuilles.Count + 1 }, FormatFeuille.A4Portrait);
            feuilles.Add(f);
            EnTete(f, entete);
            y = HautZone;
            if (enteteTableau)
            {
                y = LigneEnTete(f, colonnes, y);
            }
        }

        NouvellePage(true);

        // Lignes du tableau, regroupées par famille (ordre du décompte).
        string? famille = null;
        int rang = 0;
        foreach (var l in lignes)
        {
            var cellules = colonnes.Select(c => Couper(c.Valeur(l), c.Largeur - 2 * MargeCellule, HauteurTexte)).ToList();
            double h = HauteurLigne(cellules.Max(c => c.Count));
            bool nouvelleFamille = !string.Equals(l.FamilleAffichee, famille, StringComparison.Ordinal);
            double hFamille = HauteurLigne(1);
            if (y - h - (nouvelleFamille ? hFamille : 0) < BasZone)
            {
                NouvellePage(true);
                if (!nouvelleFamille)
                {
                    y = LigneFamille(f, famille + " (suite)", y);
                    rang = 0;
                }
            }

            if (nouvelleFamille)
            {
                famille = l.FamilleAffichee;
                y = LigneFamille(f, famille, y);
                rang = 0;
            }

            if (rang++ % 2 == 1)
            {
                Fond(f, new Rect2(Gauche, y - h, Droite, y), Encre.FondClair);
            }

            for (int i = 0; i < colonnes.Count; i++)
            {
                Cellule(f, colonnes[i], cellules[i], y, HauteurTexte, false, Encre.Noir);
            }

            y -= h;
        }

        // Totaux.
        double hTotal = HauteurLigne(1) + 1;
        if (y - hTotal < BasZone)
        {
            NouvellePage(true);
        }

        f.Primitives.Add(new Trait(new Point2(Gauche, y), new Point2(Droite, y), 0.35));
        double ligneBase = y - MargeCellule - HauteurTexte - 0.5;
        int pieces = lignes.Sum(l => l.Quantite);
        f.Primitives.Add(new Texte(new Point2(Gauche + MargeCellule, ligneBase),
            $"Total : {lignes.Count} référence{(lignes.Count > 1 ? "s" : "")}, {pieces.ToString("N0", Nombres)} pièce{(pieces > 1 ? "s" : "")}", HauteurTexte) { Gras = true });
        var qte = colonnes[colonnes.Count - 3];
        var poids = colonnes[colonnes.Count - 1];
        f.Primitives.Add(new Texte(new Point2(qte.X1 - MargeCellule, ligneBase), pieces.ToString("N0", Nombres), HauteurTexte, Alignement.BasDroite) { Gras = true });
        f.Primitives.Add(new Texte(new Point2(poids.X1 - MargeCellule, ligneBase), CalculDecompte.PoidsTotal(lignes).ToString("N1", Nombres) + " kg", HauteurTexte, Alignement.BasDroite) { Gras = true });
        y -= hTotal;
        f.Primitives.Add(new Trait(new Point2(Gauche, y), new Point2(Droite, y), 0.35));

        // Alertes en bas du tableau.
        var liste = alertes.Where(a => !string.IsNullOrWhiteSpace(a)).ToList();
        y -= 7;
        if (y - 8 < BasZone)
        {
            NouvellePage(false);
        }

        f.Primitives.Add(new Texte(new Point2(Gauche, y - 2.6), liste.Count == 0 ? "Alertes" : $"Alertes ({liste.Count})", 2.6)
        {
            Gras = true,
            Encre = liste.Count == 0 ? Encre.Noir : Encre.Rouge,
        });
        y -= 5.5;
        if (liste.Count == 0)
        {
            f.Primitives.Add(new Texte(new Point2(Gauche + 4, y - HauteurTexte), "Aucune alerte.", HauteurTexte) { Encre = Encre.Gris });
        }

        foreach (var a in liste)
        {
            var morceaux = Couper(a, Droite - Gauche - 4, HauteurTexte);
            double h = HauteurTexte + (morceaux.Count - 1) * Interligne + 1.4;
            if (y - h < BasZone)
            {
                NouvellePage(false);
            }

            f.Primitives.Add(new Texte(new Point2(Gauche + 0.8, y - HauteurTexte), "•", HauteurTexte) { Encre = Encre.Rouge });
            for (int k = 0; k < morceaux.Count; k++)
            {
                f.Primitives.Add(new Texte(new Point2(Gauche + 4, y - HauteurTexte - k * Interligne), morceaux[k], HauteurTexte));
            }

            y -= h;
        }

        foreach (var feuille in feuilles)
        {
            Pied(feuille, entete, feuilles.Count);
        }

        return feuilles;
    }

    /// <summary>Alertes propres au décompte : références sans poids, sans correspondance client.</summary>
    public static List<string> AlertesDecompte(IReadOnlyList<LigneDecompte> lignes, string? nomReferencesClient)
    {
        var alertes = new List<string>();
        var sansPoids = CalculDecompte.SansPoids(lignes).ToList();
        if (sansPoids.Count > 0)
        {
            alertes.Add("Poids inconnu (non compté dans le total) : " + string.Join(", ", sansPoids) + ".");
        }

        if (nomReferencesClient != null)
        {
            var sansRef = CalculDecompte.SansReferenceClient(lignes).ToList();
            if (sansRef.Count > 0)
            {
                alertes.Add($"Sans référence {nomReferencesClient} (référence RETOTUB indiquée) : " + string.Join(", ", sansRef) + ".");
            }
        }

        return alertes;
    }

    // ------------------------------------------------------------------ colonnes

    private static List<Colonne> Colonnes(EnTeteListe entete)
    {
        var colonnes = new List<Colonne>();
        if (entete.NomReferencesClient != null)
        {
            colonnes.Add(new Colonne("Réf. " + entete.NomReferencesClient, 30, false, l => l.ReferenceAffichee));
            colonnes.Add(new Colonne("Réf. RETOTUB", 32, false, l => l.Reference));
        }
        else
        {
            colonnes.Add(new Colonne("Référence", 36, false, l => l.Reference));
        }

        colonnes.Add(new Colonne("Désignation", 0, false, l => l.Designation));
        colonnes.Add(new Colonne("Qté", 14, true, l => l.Quantite.ToString("N0", Nombres)));
        colonnes.Add(new Colonne("Poids U (kg)", 19, true, l => l.PoidsUnitaire?.ToString("N2", Nombres) ?? "–"));
        colonnes.Add(new Colonne("Poids total (kg)", 22, true, l => l.PoidsTotal?.ToString("N1", Nombres) ?? "–"));

        var designation = colonnes.First(c => c.Titre == "Désignation");
        designation.Largeur = Droite - Gauche - colonnes.Sum(c => c.Largeur);
        double x = Gauche;
        foreach (var c in colonnes)
        {
            c.X0 = x;
            x += c.Largeur;
        }

        return colonnes;
    }

    private static double HauteurLigne(int nombreLignesTexte) => 2 * MargeCellule + HauteurTexte + (nombreLignesTexte - 1) * Interligne;

    private static double LigneEnTete(FeuilleDessinee f, List<Colonne> colonnes, double y)
    {
        var cellules = colonnes.Select(c => Couper(c.Titre, c.Largeur - 2 * MargeCellule, HauteurTexte * 1.1)).ToList();
        double h = HauteurLigne(cellules.Max(c => c.Count)) + 0.6;
        Fond(f, new Rect2(Gauche, y - h, Droite, y), Encre.FondFonce);
        for (int i = 0; i < colonnes.Count; i++)
        {
            Cellule(f, colonnes[i], cellules[i], y - 0.3, HauteurTexte, true, Encre.Blanc);
        }

        return y - h;
    }

    private static double LigneFamille(FeuilleDessinee f, string famille, double y)
    {
        double h = HauteurLigne(1);
        Fond(f, new Rect2(Gauche, y - h, Droite, y), Encre.FondFamille);
        f.Primitives.Add(new Texte(new Point2(Gauche + MargeCellule, y - MargeCellule - HauteurTexte), famille, HauteurTexte) { Gras = true });
        return y - h;
    }

    private static void Cellule(FeuilleDessinee f, Colonne c, List<string> morceaux, double yHaut, double hauteur, bool gras, Encre encre)
    {
        for (int k = 0; k < morceaux.Count; k++)
        {
            double v = yHaut - MargeCellule - hauteur - k * Interligne;
            var p = c.ADroite ? new Point2(c.X1 - MargeCellule, v) : new Point2(c.X0 + MargeCellule, v);
            f.Primitives.Add(new Texte(p, morceaux[k], hauteur, c.ADroite ? Alignement.BasDroite : Alignement.BasGauche) { Gras = gras, Encre = encre });
        }
    }

    private static void Fond(FeuilleDessinee f, Rect2 r, Encre encre)
    {
        f.Primitives.Add(new Polygone(new[]
        {
            new Point2(r.UMin, r.VMin), new Point2(r.UMax, r.VMin), new Point2(r.UMax, r.VMax), new Point2(r.UMin, r.VMax),
        }, true) { Encre = encre });
    }

    // ------------------------------------------------------------------ en-tête et pied

    private static void EnTete(FeuilleDessinee f, EnTeteListe e)
    {
        // Sans logo client dans les Paramètres par défaut : logo RETOTUB 2017 (Laurent, 27/11/2026, remarque 57).
        string logo = string.IsNullOrWhiteSpace(e.LogoClient) ? "RETOTUB" : e.LogoClient!;
        f.Primitives.Add(new ImageFeuille(logo, new Rect2(Gauche, 253, 72, 285)));

        f.Primitives.Add(new Texte(new Point2(Droite, 279), e.Titre, 4.5, Alignement.BasDroite) { Gras = true });
        var infos = new[]
        {
            ("Chantier", e.Chantier),
            ("Client", e.Client),
            ("Référence étude", e.ReferenceEtude),
            ("Date", e.Date.ToString("dd/MM/yyyy", Fr)),
        };
        double y = 271;
        const double xLibelle = 104, xValeur = 134;
        foreach (var (libelle, valeur) in infos)
        {
            f.Primitives.Add(new Texte(new Point2(xLibelle, y), libelle, 2.3) { Encre = Encre.Gris });
            string v = string.IsNullOrWhiteSpace(valeur) ? "–" : Abreger(valeur.Trim(), Droite - xValeur, 2.5);
            f.Primitives.Add(new Texte(new Point2(xValeur, y), v, 2.5) { Gras = true });
            y -= 5;
        }

        f.Primitives.Add(new Trait(new Point2(Gauche, 249), new Point2(Droite, 249), 0.5));
    }

    private static void Pied(FeuilleDessinee f, EnTeteListe e, int nombrePages)
    {
        f.Primitives.Add(new Trait(new Point2(Gauche, 16), new Point2(Droite, 16), 0.2) { Encre = Encre.Gris });
        f.Primitives.Add(new Texte(new Point2(Gauche, 11), e.Pied, 2.2) { Encre = Encre.Gris });
        f.Primitives.Add(new Texte(new Point2(Droite, 11), $"Page {f.Page.Numero} / {nombrePages}", 2.2, Alignement.BasDroite) { Encre = Encre.Gris });
    }

    // ------------------------------------------------------------------ texte

    /// <summary>Largeur estimée d'un texte Arial (mm) pour une hauteur de majuscule <paramref name="hauteur"/>.</summary>
    public static double Largeur(string texte, double hauteur)
    {
        double em = 0;
        foreach (char c in texte)
        {
            em += c switch
            {
                ' ' => 0.278,
                'i' or 'l' or 'j' or 'I' or '.' or ',' or ':' or ';' or '\'' or '|' or '!' => 0.25,
                'f' or 't' or 'r' or '(' or ')' or '-' or '/' => 0.34,
                'm' or 'w' => 0.833,
                'M' or 'W' => 0.9,
                '–' or '—' => 0.6,
                _ when char.IsDigit(c) => 0.556,
                _ when char.IsUpper(c) => 0.70,
                _ => 0.54,
            };
        }

        return em * hauteur / 0.716; // hauteur des majuscules Arial = 0,716 em
    }

    /// <summary>Coupe un texte en lignes (aux espaces, sinon en plein mot) pour tenir dans <paramref name="largeurMax"/>.</summary>
    public static List<string> Couper(string texte, double largeurMax, double hauteur)
    {
        var lignes = new List<string>();
        string courante = string.Empty;
        foreach (var mot in (texte ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string essai = courante.Length == 0 ? mot : courante + " " + mot;
            if (Largeur(essai, hauteur) <= largeurMax)
            {
                courante = essai;
                continue;
            }

            if (courante.Length > 0)
            {
                lignes.Add(courante);
            }

            // Mot trop long à lui seul : coupé où il faut.
            courante = mot;
            while (Largeur(courante, hauteur) > largeurMax && courante.Length > 1)
            {
                int n = courante.Length - 1;
                while (n > 1 && Largeur(courante.Substring(0, n), hauteur) > largeurMax)
                {
                    n--;
                }

                lignes.Add(courante.Substring(0, n));
                courante = courante.Substring(n);
            }
        }

        if (courante.Length > 0 || lignes.Count == 0)
        {
            lignes.Add(courante);
        }

        return lignes;
    }

    private static string Abreger(string texte, double largeurMax, double hauteur)
    {
        if (Largeur(texte, hauteur) <= largeurMax)
        {
            return texte;
        }

        while (texte.Length > 1 && Largeur(texte + "…", hauteur) > largeurMax)
        {
            texte = texte.Substring(0, texte.Length - 1);
        }

        return texte.TrimEnd() + "…";
    }

    private static NumberFormatInfo Format()
    {
        var n = (NumberFormatInfo)Fr.NumberFormat.Clone();
        n.NumberGroupSeparator = " ";
        return n;
    }
}
