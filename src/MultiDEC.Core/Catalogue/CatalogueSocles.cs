using System.Collections.Generic;
using System.Linq;

namespace MultiDEC.Core.Catalogue;

/// <summary>Un pied de poteau au choix (onglet « Général », liste « Socles ») et ses cotes de pose.</summary>
public sealed class SocleInfo
{
    public SocleInfo(string reference, string libelle, double zSocle, double? zCale)
    {
        Reference = reference;
        Libelle = libelle;
        ZSocle = zSocle;
        ZCale = zCale;
    }

    /// <summary>Référence = nom du bloc.</summary>
    public string Reference { get; }

    public string Libelle { get; }

    /// <summary>Cote du point de base du bloc (0 = départ des poteaux).</summary>
    public double ZSocle { get; }

    /// <summary>Cote de la cale bois, ou null (socles à roue : pas de cale).</summary>
    public double? ZCale { get; }

    public bool AvecRoues => ZCale == null;

    public override string ToString() => Libelle;
}

/// <summary>
/// Socles et vérins proposés (liste de la v2, version2024). Cotes relevées dans la bibliothèque
/// ECHAFAUDAGE-FILAIRE-3D-MultiDEC.dwg (test de rendu « BOITE … », 24/11/2026) : tous ont leur point de base
/// au départ des poteaux (cote 0) ; la cale bois (0,08 m) est posée sous la plaque. Socles à roue : sans cale.
/// </summary>
public static class CatalogueSocles
{
    public const string ParDefaut = References.SocleReglable;

    public static IReadOnlyList<SocleInfo> Tous { get; } = new[]
    {
        new SocleInfo("HMSOCREG", "Socle réglable HMSOCREG", 0.0, -0.33),          // plaque à -0,25
        new SocleInfo("HMSOCREG800", "Socle réglable 800 HMSOCREG800", 0.0, -0.33), // plaque à -0,25, tige plus longue
        new SocleInfo("HMSOCROU", "Socle à roue HMSOCROU", 0.0, null),              // roue jusqu'à -0,75
        new SocleInfo("HMSOCROU200-1200", "Socle à roue 200-1200", 0.0, null),      // roue jusqu'à -0,70
        new SocleInfo("HMSOCROUFC", "Socle à roue frein HMSOCROUFC", 0.0, null),    // roue jusqu'à -0,50
        new SocleInfo("HMVERORI38780", "Vérin orientable 38780", 0.0, -0.574),      // plaque à -0,494
        new SocleInfo("HMVERORI38500B019", "Vérin orientable 38500", 0.0, -0.364),  // plaque à -0,284
    };

    public static SocleInfo De(string? reference) =>
        Tous.FirstOrDefault(s => s.Reference == reference) ?? Tous[0];
}
