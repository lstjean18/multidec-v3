using System;
using MultiDEC.Core.Geometrie;

namespace MultiDEC.Core.Facade;

/// <summary>Famille d'un composant : sert au décompte, aux calques et à l'orientation des blocs.</summary>
public enum Famille
{
    Calage,
    Socle,
    Embase,
    Poteau,
    Goupille,
    Lisse,
    Traverse,
    DiagonaleVerticale,
    DiagonaleHorizontale,
    GardeCorps,
    Plinthe,
    Plateau,
    Trappe,
    Echelle,
    Console,
    Amarrage,

    /// <summary>Pare-gravois : consoles HMPARGRA, tôles, crochets et lisses inclinées.</summary>
    PareGravois,

    /// <summary>Poutres de franchissement (HMPOUT…) et leurs tubes de contreventement.</summary>
    Poutre,

    /// <summary>Volées d'escalier (HMESCALU…) et leurs garde-corps (HMGCESCALU…).</summary>
    Escalier,

    /// <summary>Référence ajoutée à la main dans le décompte, absente de multidec.bib (remarque 61).</summary>
    Divers,
}

/// <summary>Direction principale d'un élément.</summary>
public enum Axe
{
    /// <summary>Vertical (poteaux, socles...).</summary>
    Z,

    /// <summary>Le long de la façade (lisses, garde-corps de façade, plateaux, diagonales verticales).</summary>
    X,

    /// <summary>En travers de la façade (traverses, abouts, consoles, amarrages).</summary>
    Y,
}

/// <summary>
/// Un composant positionné dans l'espace. C'est la sortie du moteur de génération,
/// indépendante de VectorDraw : le module de rendu transforme chaque élément en vdInsert
/// (bloc = <see cref="Reference"/>) en appliquant l'orientation propre à chaque bloc du DXF.
/// </summary>
public sealed class ElementPlace
{
    public ElementPlace(string reference, Famille famille, Point3 origine, Axe axe, Face face = Face.Aucune, int numeroMaille = 0, bool inverse = false, double inclinaison = 0, double rotation = 0)
    {
        Inclinaison = inclinaison;
        Rotation = rotation;
        Reference = reference;
        Famille = famille;
        Origine = origine;
        Axe = axe;
        Face = face;
        NumeroMaille = numeroMaille;
        Inverse = inverse;
    }

    /// <summary>Référence produit = nom du bloc dans Blocs-EM-3D.dxf (ex. HMPOT20).</summary>
    public string Reference { get; }

    public Famille Famille { get; }

    /// <summary>Point d'insertion (point de base du bloc), en mètres.</summary>
    public Point3 Origine { get; }

    public Axe Axe { get; }

    /// <summary>Côté de la maille : l'élément est tourné vers ce côté (garde-corps, plinthes, diagonales...).</summary>
    public Face Face { get; }

    /// <summary>Numéro de maille (1..n), 0 si l'élément appartient à une file. Sert d'étiquette (Label) comme dans l'ancienne version.</summary>
    public int NumeroMaille { get; }

    /// <summary>Sens inversé (diagonale montant vers la gauche, trappe tournée...).</summary>
    public bool Inverse { get; }

    /// <summary>
    /// Rotation de l'élément autour de son propre axe, en degrés, avant son orientation dans le plan
    /// (tôles de pare-gravois inclinées). Positif : l'extrémité côté façade monte.
    /// </summary>
    public double Inclinaison { get; }

    /// <summary>
    /// Rotation supplémentaire dans le plan, autour de Z, en degrés (sens trigonométrique), après l'orientation
    /// standard de l'axe : tubes en diagonale horizontale sous une poutre de franchissement.
    /// </summary>
    public double Rotation { get; }

    /// <summary>Face de la structure (S1…S4) qui a produit l'élément.</summary>
    public string NomFace { get; private set; } = "S1";

    /// <summary>
    /// Rotation de la face autour de Z, en degrés (0 pour S1, +90 pour S2…). <see cref="Origine"/> est déjà
    /// en coordonnées de la structure ; le rendu applique cette rotation au bloc après son orientation propre.
    /// </summary>
    public double AngleFace { get; private set; }

    /// <summary>About (bout de face et niveau) auquel appartient l'élément : menu « Remplacer l'about par ». Null sinon.</summary>
    public RepereAbout? About { get; internal set; }

    /// <summary>Côté et palier d'une tour escalier auxquels appartient l'élément : menu « Ouvrir / Fermer ». Null sinon.</summary>
    public TourEscalier.RepereCoteTour? CoteTour { get; internal set; }

    /// <summary>
    /// Copie de l'élément placée dans la structure : rotation de <paramref name="angle"/> degrés autour de Z
    /// puis translation de <paramref name="origineFace"/> (point de la structure où tombe l'origine de la face).
    /// </summary>
    public ElementPlace DansLaStructure(string nomFace, double angle, Point3 origineFace)
    {
        double a = angle * Math.PI / 180;
        double c = Math.Round(Math.Cos(a), 12);
        double s = Math.Round(Math.Sin(a), 12);
        var o = new Point3(
            origineFace.X + Origine.X * c - Origine.Y * s,
            origineFace.Y + Origine.X * s + Origine.Y * c,
            Origine.Z);
        return new ElementPlace(Reference, Famille, o, Axe, Face, NumeroMaille, Inverse, Inclinaison, Rotation)
        {
            NomFace = nomFace,
            AngleFace = angle,
            About = About,
            CoteTour = CoteTour,
        };
    }

    public override string ToString() => $"{NomFace} {Reference} {Origine} {Axe} {Face}{(Inverse ? " inv" : string.Empty)} m{NumeroMaille}";
}
