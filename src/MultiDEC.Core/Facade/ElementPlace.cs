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
    public ElementPlace(string reference, Famille famille, Point3 origine, Axe axe, Face face = Face.Aucune, int numeroMaille = 0, bool inverse = false)
    {
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

    public override string ToString() => $"{Reference} {Origine} {Axe} {Face}{(Inverse ? " inv" : string.Empty)} m{NumeroMaille}";
}
