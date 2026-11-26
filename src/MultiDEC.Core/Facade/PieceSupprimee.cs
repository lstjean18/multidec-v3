using System;
using System.Collections.Generic;
using System.Linq;
using MultiDEC.Core.Geometrie;

namespace MultiDEC.Core.Facade;

/// <summary>
/// Pièce supprimée à la main dans le dessin (clic droit « Supprimer la sélection »), enregistrée dans le projet .mdc3
/// (point 54, Laurent 26/11/2026) : à chaque génération elle est retirée de la structure, si bien que le modèle 3D et
/// le décompte rouverts sont ceux que l'utilisateur avait modifiés. Repérée par sa face, sa référence, sa position et son axe.
/// </summary>
public sealed class PieceSupprimee
{
    public string NomFace { get; set; } = string.Empty;

    public string Reference { get; set; } = string.Empty;

    public double X { get; set; }

    public double Y { get; set; }

    public double Z { get; set; }

    public Axe Axe { get; set; }

    public bool Inverse { get; set; }

    public static PieceSupprimee De(ElementPlace e) => new PieceSupprimee
    {
        NomFace = e.NomFace,
        Reference = e.Reference,
        X = Math.Round(e.Origine.X, 4),
        Y = Math.Round(e.Origine.Y, 4),
        Z = Math.Round(e.Origine.Z, 4),
        Axe = e.Axe,
        Inverse = e.Inverse,
    };

    /// <summary>Même pièce (au millimètre près).</summary>
    public bool Designe(ElementPlace e) =>
        e.NomFace == NomFace && e.Reference == Reference && e.Axe == Axe && e.Inverse == Inverse
        && Math.Abs(e.Origine.X - X) < 0.001 && Math.Abs(e.Origine.Y - Y) < 0.001 && Math.Abs(e.Origine.Z - Z) < 0.001;

    /// <summary>
    /// Retire les pièces supprimées d'une liste d'éléments ; renvoie le nombre de pièces qui ne correspondent plus
    /// à rien (structure modifiée depuis la suppression).
    /// </summary>
    public static int Retirer(List<ElementPlace> elements, IReadOnlyCollection<PieceSupprimee> pieces)
    {
        int orphelines = 0;
        foreach (var p in pieces)
        {
            int i = elements.FindIndex(p.Designe);
            if (i < 0)
            {
                orphelines++;
                continue;
            }

            elements.RemoveAt(i);
        }

        return orphelines;
    }
}
