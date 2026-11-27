using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MultiDEC.Core.MiseEnPlan;

public enum Alignement
{
    BasGauche,
    BasCentre,
    Centre,
    GaucheMilieu,
    DroiteMilieu,
    BasDroite,
}

/// <summary>Couleurs des traits et textes de la feuille (le rendu les traduit).</summary>
public enum Encre
{
    Noir,
    Gris,
    Cote,
    Rouge,
    Bleu,
}

/// <summary>Élément dessiné sur la feuille, en mm (origine en bas à gauche).</summary>
public abstract class Primitive
{
    public Encre Encre { get; set; } = Encre.Noir;
}

public sealed class Trait : Primitive
{
    public Trait(Point2 a, Point2 b, double epaisseur = 0.25)
    {
        A = a;
        B = b;
        Epaisseur = epaisseur;
    }

    public Point2 A { get; }
    public Point2 B { get; }
    public double Epaisseur { get; }
    public bool Tirets { get; set; }
}

public sealed class Texte : Primitive
{
    public Texte(Point2 position, string contenu, double hauteur, Alignement alignement = Alignement.BasGauche, double angleDegres = 0)
    {
        Position = position;
        Contenu = contenu;
        Hauteur = hauteur;
        Alignement = alignement;
        Angle = angleDegres;
    }

    public Point2 Position { get; }
    public string Contenu { get; }
    public double Hauteur { get; }
    public Alignement Alignement { get; }
    public double Angle { get; }
    public bool Gras { get; set; }
}

public sealed class Cercle : Primitive
{
    public Cercle(Point2 centre, double rayon, bool plein = false)
    {
        Centre = centre;
        Rayon = rayon;
        Plein = plein;
    }

    public Point2 Centre { get; }
    public double Rayon { get; }
    public bool Plein { get; }
    public bool Tirets { get; set; }
}

public sealed class Polygone : Primitive
{
    public Polygone(IReadOnlyList<Point2> sommets, bool plein)
    {
        Sommets = sommets;
        Plein = plein;
    }

    public IReadOnlyList<Point2> Sommets { get; }
    public bool Plein { get; }
}

/// <summary>Image (logo) dans un cadre, proportions conservées.</summary>
public sealed class ImageFeuille : Primitive
{
    public ImageFeuille(string source, Rect2 cadre)
    {
        Source = source;
        Cadre = cadre;
    }

    /// <summary>Chemin d'un fichier, ou nom d'un logo intégré (« RETOTUB », « MultiRETO », « MultiSYSTEM »).</summary>
    public string Source { get; }
    public Rect2 Cadre { get; }
}

/// <summary>Une feuille prête à dessiner : les vues (rendues par VectorDraw) et tout le reste en primitives.</summary>
public sealed class FeuilleDessinee
{
    public FeuilleDessinee(PageDePlan page, FormatFeuille format)
    {
        Page = page;
        Format = format;
    }

    public PageDePlan Page { get; }
    public FormatFeuille Format { get; }
    public List<Primitive> Primitives { get; } = new List<Primitive>();

    public IEnumerable<Texte> Textes => Primitives.OfType<Texte>();
}

/// <summary>
/// Habillage des feuilles (cadre, cartouche, titres, cotes, repères) en primitives, indépendant de VectorDraw :
/// le rendu n'a plus qu'à tracer les primitives et à placer les vues dans leurs cadres.
/// </summary>
public static class MiseEnPage
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    public const double HauteurTexteCote = 2.2;
    public const double HauteurTitre = 3.5;
    private const double Oblique = 1.4; // demi-longueur des traits obliques des cotes
    private const double Depassement = 1.5;
    private const double Ecart = 1.0;

    public static List<FeuilleDessinee> Feuilles(PlanDeStructure plan)
    {
        var liste = new List<FeuilleDessinee>();
        foreach (var page in plan.Pages)
        {
            var f = new FeuilleDessinee(page, plan.Format);
            Cadre(f);
            CartoucheDe(f, plan.Cartouche, plan.Pages.Count);
            foreach (var vue in page.Vues)
            {
                TitreVue(f, vue);
                foreach (var c in vue.Cotes)
                {
                    CoteDe(f, vue, c);
                }

                foreach (var r in vue.Reperes)
                {
                    RepereDe(f, vue, r);
                }
            }

            liste.Add(f);
        }

        return liste;
    }

    private static void Rectangle(FeuilleDessinee f, Rect2 r, double epaisseur = 0.25, Encre encre = Encre.Noir)
    {
        var a = new Point2(r.UMin, r.VMin);
        var b = new Point2(r.UMax, r.VMin);
        var c = new Point2(r.UMax, r.VMax);
        var d = new Point2(r.UMin, r.VMax);
        foreach (var (p, q) in new[] { (a, b), (b, c), (c, d), (d, a) })
        {
            f.Primitives.Add(new Trait(p, q, epaisseur) { Encre = encre });
        }
    }

    private static void Cadre(FeuilleDessinee f)
    {
        var fmt = f.Format;
        Rectangle(f, new Rect2(fmt.Marge, fmt.Marge, fmt.Largeur - fmt.Marge, fmt.Hauteur - fmt.Marge), 0.5);
    }

    // ------------------------------------------------------------------ cartouche

    /// <summary>Colonnes du cartouche (mm depuis le bord gauche du cadre).</summary>
    internal static readonly double[] Colonnes = { 0, 62, 112, 192, 282, 352, 400 };

    private static void CartoucheDe(FeuilleDessinee f, Cartouche c, int nombrePages)
    {
        var fmt = f.Format;
        double x0 = fmt.Marge, y0 = fmt.Marge, h = fmt.HauteurCartouche, haut = y0 + h;
        f.Primitives.Add(new Trait(new Point2(x0, haut), new Point2(fmt.Largeur - fmt.Marge, haut), 0.5));
        for (int k = 1; k < Colonnes.Length - 1; k++)
        {
            f.Primitives.Add(new Trait(new Point2(x0 + Colonnes[k], y0), new Point2(x0 + Colonnes[k], haut), 0.35));
        }

        double X(int k) => x0 + Colonnes[k];

        // 1. MultiDEC : logos RETOTUB et du modèle, version.
        f.Primitives.Add(new ImageFeuille("RETOTUB", new Rect2(X(0) + 3, haut - 16, X(1) - 3, haut - 3)));
        string logoModele = c.Modele == Facade.ModeleEchafaudage.MultiSystem ? "MultiSYSTEM" : "MultiRETO";
        f.Primitives.Add(new ImageFeuille(logoModele, new Rect2(X(0) + 3, haut - 30, X(1) - 3, haut - 18)));
        f.Primitives.Add(new Texte(new Point2(X(0) + 3, y0 + 7), "Plan établi avec", 2.0));
        f.Primitives.Add(new Texte(new Point2(X(0) + 3, y0 + 3), c.Version, 2.2) { Gras = true });

        // 2. Logo client.
        if (!string.IsNullOrWhiteSpace(c.LogoClient))
        {
            f.Primitives.Add(new ImageFeuille(c.LogoClient!, new Rect2(X(1) + 3, y0 + 3, X(2) - 3, haut - 3)));
        }
        else
        {
            f.Primitives.Add(new Texte(new Point2((X(1) + X(2)) / 2, y0 + h / 2), "(logo client)", 2.0, Alignement.Centre) { Encre = Encre.Gris });
        }

        // 3. Projet.
        var projet = c.Projet.ToList();
        projet.Add(new KeyValuePair<string, string>("Établi par", c.EtabliPar));
        Rubrique(f, "PROJET", projet, X(2), X(3), y0, haut, 24);

        // 4. Structure.
        Rubrique(f, "STRUCTURE", c.Structure, X(3), X(4), y0, haut, 26);

        // 5. Faces, poids, mentions.
        double y = haut - 4.5;
        f.Primitives.Add(new Texte(new Point2(X(4) + 2, y), "FACES", 2.2) { Gras = true });
        double[] largeurs = { 8, 11, 0, 0, 11, 10 }; // face, longueur, (largeur, dern. plancher non repris faute de place), hauteur, amarrages
        var colonnes = new[] { 0, 1, 4, 5 };
        string[] entetes = { "Face", "Long.", "Haut.", "Amarr." };
        int lignes = Math.Min(c.Faces.Count - 1, 5);
        y -= 4;
        for (int l = -1; l < lignes; l++)
        {
            double x = X(4) + 2;
            for (int k = 0; k < colonnes.Length; k++)
            {
                string t = l < 0 ? entetes[k] : (c.Faces[l + 1].Length > colonnes[k] ? c.Faces[l + 1][colonnes[k]] : string.Empty);
                f.Primitives.Add(new Texte(new Point2(x, y), t, 1.9) { Gras = l < 0 });
                x += largeurs[colonnes[k]] + (k == 0 ? 2 : 6);
            }

            y -= 3.4;
        }

        if (c.Faces.Count - 1 > lignes)
        {
            f.Primitives.Add(new Texte(new Point2(X(4) + 2, y), "… " + (c.Faces.Count - 1 - lignes).ToString(Fr) + " autres faces", 1.9));
            y -= 3.4;
        }

        if (!string.IsNullOrEmpty(c.Poids))
        {
            f.Primitives.Add(new Texte(new Point2(X(4) + 2, y0 + 3 + 3.6 * c.Mentions.Count), "Poids : " + c.Poids, 2.2) { Gras = true });
        }

        for (int k = 0; k < c.Mentions.Count; k++)
        {
            f.Primitives.Add(new Texte(new Point2(X(4) + 2, y0 + 3 + 3.6 * (c.Mentions.Count - 1 - k)), c.Mentions[k], 1.9) { Encre = Encre.Rouge });
        }

        // 6. Titre de la feuille, échelles, folio.
        var page = f.Page;
        f.Primitives.Add(new Texte(new Point2(X(5) + 3, haut - 7), page.Titre, 3.5) { Gras = true });
        var echelles = page.Vues.Where(v => v.Echelle > 0).Select(v => v.LibelleEchelle).Distinct().ToList();
        f.Primitives.Add(new Texte(new Point2(X(5) + 3, haut - 13), "Échelle : " + (echelles.Count == 0 ? "sans" : echelles.Count == 1 ? echelles[0] : "voir les vues"), 2.2));
        f.Primitives.Add(new Texte(new Point2(X(5) + 3, haut - 18), "Format " + f.Format.Nom + " – cotes en mètres", 2.2));
        f.Primitives.Add(new Texte(new Point2(X(5) + 3, haut - 23), "Indice " + c.Indice, 2.2));
        f.Primitives.Add(new Texte(new Point2(X(6) - 3, y0 + 3), "Page " + page.Numero.ToString(Fr) + " / " + nombrePages.ToString(Fr), 3.0, Alignement.BasDroite) { Gras = true });
    }

    private static void Rubrique(FeuilleDessinee f, string titre, IReadOnlyList<KeyValuePair<string, string>> lignes, double x0, double x1, double y0, double haut, double largeurCle)
    {
        double y = haut - 4.5;
        f.Primitives.Add(new Texte(new Point2(x0 + 2, y), titre, 2.2) { Gras = true });
        double pas = Math.Min(4.4, (haut - y0 - 8) / Math.Max(1, lignes.Count));
        y -= pas + 0.5;
        foreach (var kv in lignes)
        {
            f.Primitives.Add(new Texte(new Point2(x0 + 2, y), kv.Key, 2.0) { Encre = Encre.Gris });
            f.Primitives.Add(new Texte(new Point2(x0 + 2 + largeurCle, y), Tronquer(kv.Value, (x1 - x0 - largeurCle - 4) / 1.7), 2.0));
            y -= pas;
        }
    }

    /// <summary>Coupe un texte trop long pour sa case (largeur moyenne d'un caractère ≈ 0,85 × hauteur).</summary>
    internal static string Tronquer(string texte, double caracteres)
    {
        int n = Math.Max(4, (int)caracteres);
        return texte.Length <= n ? texte : texte.Substring(0, n - 1) + "…";
    }

    // ------------------------------------------------------------------ vues

    private static void TitreVue(FeuilleDessinee f, VueDePlan vue)
    {
        var cadre = vue.Cadre;
        double y = cadre.VMin - (vue.Cotes.Count > 0 ? 23.5 : 3) - HauteurTitre;
        string titre = (vue.Lettre != null ? "Détail " + vue.Lettre + " : " : string.Empty) + vue.Titre;
        string echelle = vue.Echelle > 0 ? "Éch. " + vue.LibelleEchelle : "sans échelle";
        var centre = new Point2(cadre.Centre.U, y);
        f.Primitives.Add(new Texte(centre, titre, HauteurTitre, Alignement.BasCentre) { Gras = true });
        f.Primitives.Add(new Texte(new Point2(centre.U, y - 4), echelle, 2.2, Alignement.BasCentre) { Encre = Encre.Gris });
        double l = titre.Length * HauteurTitre * 0.62;
        f.Primitives.Add(new Trait(new Point2(centre.U - l / 2, y - 0.8), new Point2(centre.U + l / 2, y - 0.8), 0.35));
    }

    /// <summary>Cote : lignes d'attache, ligne de cote décalée, traits obliques aux extrémités, valeur au-dessus.</summary>
    internal static void CoteDe(FeuilleDessinee f, VueDePlan vue, Cote c)
    {
        var a = vue.VersFeuille(c.A);
        var b = vue.VersFeuille(c.B);
        var d = c.Direction;
        var n = d.Normale;
        double na = n.U * a.U + n.V * a.V, nb = n.U * b.U + n.V * b.V;
        double s = (c.DecalageMm >= 0 ? Math.Max(na, nb) : Math.Min(na, nb)) + c.DecalageMm;
        var la = a + n * (s - na);
        var lb = b + n * (s - nb);
        double signe = Math.Sign(c.DecalageMm == 0 ? 1 : c.DecalageMm);

        // Lignes d'attache (avec un petit jour près de l'objet).
        foreach (var (p, l) in new[] { (a, la), (b, lb) })
        {
            var debut = p + n * (signe * Ecart);
            var fin = l + n * (signe * Depassement);
            if ((fin - debut).Longueur > 0.1)
            {
                f.Primitives.Add(new Trait(debut, fin, 0.18) { Encre = Encre.Cote });
            }
        }

        f.Primitives.Add(new Trait(la - d * Depassement, lb + d * Depassement, 0.18) { Encre = Encre.Cote });
        var oblique = (d + n).Norme() * Oblique;
        foreach (var p in new[] { la, lb })
        {
            f.Primitives.Add(new Trait(p - oblique, p + oblique, 0.35) { Encre = Encre.Cote });
        }

        // Texte lisible : de gauche à droite ou de bas en haut, posé du côté extérieur de la ligne.
        double angle = Math.Atan2(d.V, d.U) * 180 / Math.PI;
        var haut = n;
        if (angle > 90.001 || angle <= -89.999)
        {
            angle += angle > 0 ? -180 : 180;
            haut = n * -1;
        }

        var milieu = (la + lb) * 0.5;
        double longueur = (lb - la).Longueur;
        double largeurTexte = c.Texte.Length * HauteurTexteCote * 0.75;
        var position = milieu + haut * 0.8;
        if (largeurTexte > longueur - 1)
        {
            // Trop court : texte à l'extérieur, dans le prolongement de la ligne de cote.
            var lecture = new Point2(Math.Cos(angle * Math.PI / 180), Math.Sin(angle * Math.PI / 180));
            bool coteB = c.PositionTexte switch
            {
                PositionTexte.Avant => false,
                PositionTexte.Apres => true,
                _ => (lb - vue.Cadre.Centre).Longueur >= (la - vue.Cadre.Centre).Longueur,
            };
            var bout = coteB ? lb : la;
            var autre = coteB ? la : lb;
            var sens = (bout - autre).Norme();
            bool memeSens = sens.U * lecture.U + sens.V * lecture.V > 0;
            position = bout + lecture * ((memeSens ? 1 : -1) * (Depassement + 1 + largeurTexte / 2)) + haut * 0.8;
        }

        f.Primitives.Add(new Texte(position, c.Texte, HauteurTexteCote, Alignement.BasCentre, angle) { Encre = Encre.Cote });
    }

    private static void RepereDe(FeuilleDessinee f, VueDePlan vue, Repere r)
    {
        var p = vue.VersFeuille(r.Position);
        switch (r.Type)
        {
            case TypeRepere.Amarrage:
                // Triangle plein pointé vers le mur, au droit de l'amarrage.
                f.Primitives.Add(new Polygone(new[] { p + new Point2(-1.3, -1.1), p + new Point2(1.3, -1.1), p + new Point2(0, 1.2) }, true) { Encre = Encre.Bleu });
                break;
            case TypeRepere.Niveau:
                if (vue.Type == TypeVue.Dessus)
                {
                    f.Primitives.Add(new Texte(p, r.Texte, 3.0, Alignement.Centre) { Gras = true, Encre = Encre.Bleu });
                    break;
                }

                // Repère de niveau à droite de la vue : triangle pointe en bas sur le trait, cote d'altitude au-dessus.
                var q = new Point2(vue.Cadre.UMax + 3, p.V);
                f.Primitives.Add(new Trait(new Point2(vue.Cadre.UMax + 0.5, p.V), q + new Point2(9, 0), 0.18) { Encre = Encre.Cote });
                f.Primitives.Add(new Polygone(new[] { q, q + new Point2(-1.2, 1.8), q + new Point2(1.2, 1.8) }, false) { Encre = Encre.Cote });
                f.Primitives.Add(new Texte(q + new Point2(2, 0.6), r.Texte, HauteurTexteCote) { Encre = Encre.Cote });
                break;
            case TypeRepere.Detail:
                var zone = r.Zone ?? new Rect2(r.Position.U - 0.5, r.Position.V - 0.5, r.Position.U + 0.5, r.Position.V + 0.5);
                var z0 = vue.VersFeuille(new Point2(zone.UMin, zone.VMin));
                var z1 = vue.VersFeuille(new Point2(zone.UMax, zone.VMax));
                var cadre = new Rect2(z0.U, z0.V, z1.U, z1.V).Agrandi(1.5);
                foreach (var (a, b) in new[]
                {
                    (new Point2(cadre.UMin, cadre.VMin), new Point2(cadre.UMax, cadre.VMin)),
                    (new Point2(cadre.UMax, cadre.VMin), new Point2(cadre.UMax, cadre.VMax)),
                    (new Point2(cadre.UMax, cadre.VMax), new Point2(cadre.UMin, cadre.VMax)),
                    (new Point2(cadre.UMin, cadre.VMax), new Point2(cadre.UMin, cadre.VMin)),
                })
                {
                    f.Primitives.Add(new Trait(a, b, 0.3) { Tirets = true, Encre = Encre.Rouge });
                }

                var bulle = new Point2(cadre.UMax, cadre.VMax) + new Point2(2.4, 2.4);
                f.Primitives.Add(new Cercle(bulle, 3.2) { Encre = Encre.Rouge });
                f.Primitives.Add(new Texte(bulle, r.Texte, 3.2, Alignement.Centre) { Gras = true, Encre = Encre.Rouge });
                break;
        }
    }
}
