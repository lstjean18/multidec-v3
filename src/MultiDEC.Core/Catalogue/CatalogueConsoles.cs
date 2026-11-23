using System;
using System.Collections.Generic;
using MultiDEC.Core.Facade;

namespace MultiDEC.Core.Catalogue;

/// <summary>Caractéristiques d'une console (port de CheckConsole / ListeConsoles.csv).</summary>
public sealed class ConsoleInfo
{
    public ConsoleInfo(string reference, double largeur, double decalageZ = 0, string? accessoireBout = null)
    {
        Reference = reference;
        Largeur = largeur;
        DecalageZ = decalageZ;
        AccessoireBout = accessoireBout;
    }

    public string Reference { get; }

    /// <summary>Débord de la console vers l'intérieur (m).</summary>
    public double Largeur { get; }

    /// <summary>Décalage vertical du poteau de garde-corps en bout de console.</summary>
    public double DecalageZ { get; }

    /// <summary>Accessoire posé en bout de console (HMEMB pour les consoles chape/chape en MS).</summary>
    public string? AccessoireBout { get; }

    /// <summary>La « traverse en console » HMTRA030 ne porte ni poteau ni garde-corps.</summary>
    public bool PorteGardeCorps => Reference != "HMTRA030";
}

/// <summary>Consoles disponibles par gamme. Données reprises de CheckConsole (Facade.vb).</summary>
public static class CatalogueConsoles
{
    private static readonly Dictionary<string, ConsoleInfo> MultiReto = Index(
        new ConsoleInfo("HMTRA030", 0.30),
        new ConsoleInfo("HMCON300", 0.40),
        new ConsoleInfo("HMCON400E", 0.40),
        new ConsoleInfo("HMCON700E", 0.70),
        new ConsoleInfo("HMCON700EE", 0.70),
        new ConsoleInfo("HMCON700MR2", 0.70),
        new ConsoleInfo("HMCON750MR", 0.75),
        new ConsoleInfo("HMCON750EE", 0.75),
        new ConsoleInfo("HMCON750MR2", 0.75),
        new ConsoleInfo("HMCON1000E", 1.00),
        new ConsoleInfo("HMCON1000EE", 1.00),
        new ConsoleInfo("HMCON1050E", 1.05),
        new ConsoleInfo("HMCON1050MR", 1.05),
        new ConsoleInfo("HMCON1050EE", 1.05),
        new ConsoleInfo("HMCON1050MR2", 1.05));

    private static readonly Dictionary<string, ConsoleInfo> MultiSystem = Index(
        new ConsoleInfo("HMTRA030", 0.30),
        new ConsoleInfo("HMCON300", 0.40),
        new ConsoleInfo("HMCON400E", 0.40, 0.04, References.Embase),
        new ConsoleInfo("HMCON400E2", 0.40, 0.04),
        new ConsoleInfo("HMCON500E", 0.60, 0.04),
        new ConsoleInfo("HMCON700E", 0.70, 0.04, References.Embase),
        new ConsoleInfo("HMCON700E2", 0.70, 0.04),
        new ConsoleInfo("HMCON700EE", 0.70, 0.04, References.Embase),
        new ConsoleInfo("HMCON700EE2", 0.70, 0.04),
        new ConsoleInfo("HMCON750E", 0.75, 0.04, References.Embase),
        new ConsoleInfo("HMCON750EE", 0.75, 0.04, References.Embase),
        new ConsoleInfo("HMCON1000E", 1.00, 0.04, References.Embase),
        new ConsoleInfo("HMCON1000E2", 1.00, 0.04),
        new ConsoleInfo("HMCON1000EE", 1.00, 0.04, References.Embase),
        new ConsoleInfo("HMCON1050E", 1.05, 0.04, References.Embase),
        new ConsoleInfo("HMCON1050EE", 1.05, 0.04));

    /// <summary>Consoles proposées pour une gamme (Multi-RETO à embase : mêmes consoles que Multi-RETO).</summary>
    public static IReadOnlyCollection<ConsoleInfo> Pour(ModeleEchafaudage modele) => Table(modele).Values;

    public static bool TryGet(ModeleEchafaudage modele, string reference, out ConsoleInfo info) =>
        Table(modele).TryGetValue(reference, out info!);

    private static Dictionary<string, ConsoleInfo> Table(ModeleEchafaudage modele) =>
        modele == ModeleEchafaudage.MultiSystem ? MultiSystem : MultiReto;

    private static Dictionary<string, ConsoleInfo> Index(params ConsoleInfo[] consoles)
    {
        var d = new Dictionary<string, ConsoleInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in consoles)
        {
            d[c.Reference] = c;
        }

        return d;
    }
}
