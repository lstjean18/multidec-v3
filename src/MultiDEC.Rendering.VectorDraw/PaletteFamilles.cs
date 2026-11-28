using System.Drawing;
using MultiDEC.Core.Facade;

namespace MultiDEC.Rendering.VectorDraw;

/// <summary>
/// Couleurs du rendu réaliste, reprises de la v2 : tubes acier gris, plinthes vertes, diagonales rouges,
/// socles orange, échelles et trappes couleur bois. Les blocs sont préparés « couleur DuBloc »
/// (voir <see cref="RenduVectorDraw.ChargerBibliotheque"/>) : la couleur de l'insertion s'applique à toute la pièce.
/// </summary>
public static class PaletteFamilles
{
    public static readonly Color Acier = Color.FromArgb(150, 152, 158);
    public static readonly Color Plancher = Color.FromArgb(105, 108, 112);
    public static readonly Color Vert = Color.FromArgb(40, 200, 70);
    public static readonly Color Rouge = Color.FromArgb(225, 50, 25);
    public static readonly Color Orange = Color.FromArgb(245, 130, 30);
    public static readonly Color Bois = Color.FromArgb(215, 165, 105);

    /// <summary>Volées d'escalier et leurs garde-corps (Laurent, 24/11/2026 : lisibilité).</summary>
    public static readonly Color Bleu = Color.FromArgb(55, 125, 190);

    /// <summary>Montants de garde-corps sur lisse HMMONTGC / HMMONTGCE (Laurent, 24/11/2026).</summary>
    public static readonly Color Violet = Color.FromArgb(140, 60, 200);

    /// <summary>Couleur d'un élément : par famille, sauf pièces repérées par leur référence.</summary>
    public static Color Couleur(ElementPlace e) =>
        e.Reference.StartsWith("HMMONTGC", System.StringComparison.OrdinalIgnoreCase) ? Violet : Couleur(e.Famille);

    public static Color Couleur(Famille famille)
    {
        switch (famille)
        {
            case Famille.Socle:
            case Famille.Embase:
            case Famille.Calage:
                return Orange;
            case Famille.DiagonaleVerticale:
            case Famille.Amarrage:
                return Rouge;
            case Famille.Plinthe:
                return Vert;
            case Famille.Plateau:
                return Plancher;
            case Famille.Trappe:
            case Famille.Echelle:
                return Bois;
            case Famille.Escalier:
                return Bleu;
            default:
                return Acier;
        }
    }
}
