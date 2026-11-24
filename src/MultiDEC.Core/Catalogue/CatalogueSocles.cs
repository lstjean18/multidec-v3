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
/// Socles et vérins proposés (liste de la v2, version2024). Cotes : HMSOCREG relevée dans la bibliothèque
/// (point de base = haut de la tige, cale bois à -0,33) ; les autres sont reprises des écarts de la v2
/// par rapport à HMSOCREG, à confirmer sur le dessin (test de rendu « BOITE … »).
/// </summary>
public static class CatalogueSocles
{
    public const string ParDefaut = References.SocleReglable;

    public static IReadOnlyList<SocleInfo> Tous { get; } = new[]
    {
        new SocleInfo("HMSOCREG", "Socle réglable HMSOCREG", 0.0, -0.33),
        new SocleInfo("HMSOCREG800", "Socle réglable 800 HMSOCREG800", -0.17, -0.50),
        new SocleInfo("HMSOCROU", "Socle à roue HMSOCROU", 0.26, null),
        new SocleInfo("HMSOCROU200-1200", "Socle à roue 200-1200", 0.26, null),
        new SocleInfo("HMSOCROUFC", "Socle à roue frein HMSOCROUFC", 0.0, null),
        new SocleInfo("HMVERORI38780", "Vérin orientable 38780", -0.17, -0.745),
        new SocleInfo("HMVERORI38500B019", "Vérin orientable 38500", -0.17, -0.54),
    };

    public static SocleInfo De(string? reference) =>
        Tous.FirstOrDefault(s => s.Reference == reference) ?? Tous[0];
}
