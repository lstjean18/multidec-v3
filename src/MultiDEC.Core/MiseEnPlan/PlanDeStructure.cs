using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MultiDEC.Core.MiseEnPlan;

public enum TypeVue
{
    Dessus,
    Elevation,
    Profil,
    Iso,
    Detail,
}

/// <summary>Cote d'une vue : de A à B (mètres, coordonnées de la vue), ligne de cote décalée sur la feuille.</summary>
public sealed class Cote
{
    public Cote(Point2 a, Point2 b, Point2 direction, double decalageMm, string? texte = null)
    {
        A = a;
        B = b;
        Direction = direction.Norme();
        DecalageMm = decalageMm;
        Texte = texte ?? Format(Math.Abs((b - a).U * Direction.U + (b - a).V * Direction.V));
    }

    public Point2 A { get; }
    public Point2 B { get; }

    /// <summary>Direction mesurée (unitaire).</summary>
    public Point2 Direction { get; }

    /// <summary>Décalage de la ligne de cote sur la feuille, en mm, le long de la normale à gauche de <see cref="Direction"/>.</summary>
    public double DecalageMm { get; }

    public string Texte { get; }

    /// <summary>Où mettre le texte si la cote est trop courte.</summary>
    public PositionTexte PositionTexte { get; internal set; }

    /// <summary>Longueur mesurée, en mètres.</summary>
    public double Valeur => Math.Abs((B - A).U * Direction.U + (B - A).V * Direction.V);

    public static string Format(double metres) => metres.ToString("0.00", CultureInfo.GetCultureInfo("fr-FR"));
}

/// <summary>Place du texte d'une cote trop courte pour le contenir.</summary>
public enum PositionTexte
{
    /// <summary>Du côté de l'extrémité la plus éloignée du centre de la vue.</summary>
    Auto,

    /// <summary>Avant le point A (première cote d'une chaîne).</summary>
    Avant,

    /// <summary>Après le point B (dernière cote d'une chaîne).</summary>
    Apres,
}

public enum TypeRepere
{
    /// <summary>Amarrage (symbole).</summary>
    Amarrage,

    /// <summary>Niveau de plancher (« +6,00 »).</summary>
    Niveau,

    /// <summary>Zone renvoyée vers une vue de détail (« A »).</summary>
    Detail,
}

public sealed class Repere
{
    public Repere(TypeRepere type, Point2 position, string texte, Rect2? zone = null)
    {
        Type = type;
        Position = position;
        Texte = texte;
        Zone = zone;
    }

    public TypeRepere Type { get; }

    /// <summary>Position (mètres, coordonnées de la vue).</summary>
    public Point2 Position { get; }

    public string Texte { get; }

    /// <summary>Zone entourée (détail).</summary>
    public Rect2? Zone { get; }
}

/// <summary>Une vue de la mise en plan : projection, partie de la structure montrée, échelle et place sur la feuille.</summary>
public sealed class VueDePlan
{
    public VueDePlan(string titre, TypeVue type, Projection projection, Rect2 emprise)
    {
        Titre = titre;
        Type = type;
        Projection = projection;
        Emprise = emprise;
    }

    public string Titre { get; }
    public TypeVue Type { get; }
    public Projection Projection { get; }

    /// <summary>Partie montrée, en mètres dans les coordonnées de la vue.</summary>
    public Rect2 Emprise { get; internal set; }

    /// <summary>Dénominateur de l'échelle (100 pour 1/100) ; 0 si la vue est ajustée sans échelle (iso).</summary>
    public int Echelle { get; internal set; }

    /// <summary>Cadre de la vue sur la feuille, en mm (origine en bas à gauche).</summary>
    public Rect2 Cadre { get; internal set; }

    /// <summary>Millimètres de feuille par mètre de structure.</summary>
    public double MmParMetre { get; internal set; }

    public List<Cote> Cotes { get; } = new List<Cote>();
    public List<Repere> Reperes { get; } = new List<Repere>();

    /// <summary>Lettre du détail (vues de détail).</summary>
    public string? Lettre { get; internal set; }

    /// <summary>Faces dont la vue montre les éléments (null : toutes).</summary>
    public IReadOnlyCollection<string>? Faces { get; internal set; }

    /// <summary>Vue isométrique (ni plan ni élévation) : ajustée à sa place, sans échelle.</summary>
    public bool EstAxonometrique => Math.Abs(Projection.Vers.Z) > 1e-9 && Math.Abs(Math.Abs(Projection.Vers.Z) - 1) > 1e-9;

    public string LibelleEchelle => Echelle > 0 ? "1/" + Echelle.ToString(CultureInfo.InvariantCulture) : "sans échelle";

    /// <summary>Point de la vue (mètres) → point de la feuille (mm).</summary>
    public Point2 VersFeuille(Point2 p) => Cadre.Centre + (p - Emprise.Centre) * MmParMetre;
}

public sealed class PageDePlan
{
    public PageDePlan(string titre)
    {
        Titre = titre;
    }

    public string Titre { get; }
    public List<VueDePlan> Vues { get; } = new List<VueDePlan>();
    public int Numero { get; internal set; }
}

/// <summary>Feuille A3 paysage (Laurent, 27/11/2026) : cadre à 10 mm, cartouche en bas.</summary>
public sealed class FormatFeuille
{
    public static readonly FormatFeuille A3Paysage = new FormatFeuille("A3", 420, 297);

    /// <summary>Liste du matériel (Laurent, 27/11/2026).</summary>
    public static readonly FormatFeuille A4Portrait = new FormatFeuille("A4", 210, 297);

    public FormatFeuille(string nom, double largeur, double hauteur)
    {
        Nom = nom;
        Largeur = largeur;
        Hauteur = hauteur;
    }

    public string Nom { get; }
    public double Largeur { get; }
    public double Hauteur { get; }
    public double Marge => 10;
    public double HauteurCartouche => 44;

    /// <summary>Zone des vues : au-dessus du cartouche, dans le cadre.</summary>
    public Rect2 ZoneDessin => new Rect2(Marge + 4, Marge + HauteurCartouche + 4, Largeur - Marge - 4, Hauteur - Marge - 4);
}

public sealed class PlanDeStructure
{
    public FormatFeuille Format { get; set; } = FormatFeuille.A3Paysage;
    public List<PageDePlan> Pages { get; } = new List<PageDePlan>();
    public Cartouche Cartouche { get; set; } = new Cartouche();
    public IEnumerable<VueDePlan> Vues => Pages.SelectMany(p => p.Vues);
}
