using System;

namespace MultiDEC.Core.Facade;

/// <summary>
/// About remplacé par clic droit dans le dessin (Laurent, 25/11/2026) : à un bout de la face et à un niveau,
/// un autre garde-corps d'about que celui des paramètres, ou un déport en console. Enregistré dans le projet
/// et reproduit à chaque « Dessiner ».
/// </summary>
public sealed class RemplacementAbout
{
    /// <summary>Bout de la face : false = about gauche (x = 0), true = about droit (fin de face).</summary>
    public bool Droit { get; set; }

    /// <summary>Hauteur du plancher concerné.</summary>
    public double Hauteur { get; set; }

    /// <summary>Garde-corps d'about choisi (null si déport en console).</summary>
    public TypeSecurite? Securite { get; set; }

    /// <summary>Console du déport (null si garde-corps d'about).</summary>
    public string? Console { get; set; }

    public RemplacementAbout Copie() => new RemplacementAbout { Droit = Droit, Hauteur = Hauteur, Securite = Securite, Console = Console };

    public override string ToString() =>
        FormattableString.Invariant($"About {(Droit ? "droit" : "gauche")} {Hauteur:0.00} m : {(Console != null ? "déport " + Console : Securite?.ToString())}");
}

/// <summary>Repère d'un about dans le dessin (bout de la face et niveau), pour le menu « Remplacer l'about par ».</summary>
public sealed class RepereAbout
{
    public RepereAbout(bool droit, double hauteur)
    {
        Droit = droit;
        Hauteur = hauteur;
    }

    public bool Droit { get; }

    public double Hauteur { get; }
}
