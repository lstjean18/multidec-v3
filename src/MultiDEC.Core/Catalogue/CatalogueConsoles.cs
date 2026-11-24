using System;
using System.Collections.Generic;
using System.Linq;
using MultiDEC.Core.Facade;

namespace MultiDEC.Core.Catalogue;

/// <summary>Fixation au bout d'une console (Laurent, 24/11/2026).</summary>
public enum FixationBout
{
    /// <summary>Platine : aucun poteau possible en bout, donc ni lisse ni garde-corps.</summary>
    Platine,

    /// <summary>
    /// Chape : en Multi-RETO, poteau de départ HMPOTDP10 directement pour le garde-corps ; en Multi-SYSTEM, embase HMEMB
    /// (pour accrocher la lisse) puis HMPOT10 dessus s'il faut le garde-corps.
    /// </summary>
    Chape,

    /// <summary>Nipple : embase intégrée, lisses accrochées directement au bout, poteau posé dessus s'il faut le garde-corps.</summary>
    Nipple,
}

/// <summary>Caractéristiques d'une console.</summary>
public sealed class ConsoleInfo
{
    public ConsoleInfo(string reference, double largeur, FixationBout fixation, bool multiReto, bool multiSystem)
    {
        Reference = reference;
        Largeur = largeur;
        Fixation = fixation;
        MultiReto = multiReto;
        MultiSystem = multiSystem;
    }

    public string Reference { get; }

    /// <summary>Débord de la console vers la façade (m).</summary>
    public double Largeur { get; }

    public FixationBout Fixation { get; }

    /// <summary>Compatible Multi-RETO (et Multi-RETO à embase).</summary>
    public bool MultiReto { get; }

    /// <summary>Compatible Multi-SYSTEM.</summary>
    public bool MultiSystem { get; }

    /// <summary>Un poteau peut être posé en bout (pas sur une platine).</summary>
    public bool PorteGardeCorps => Fixation != FixationBout.Platine;

    public bool Compatible(ModeleEchafaudage modele) => modele == ModeleEchafaudage.MultiSystem ? MultiSystem : MultiReto;

    /// <summary>Embase posée en bout : chape en Multi-SYSTEM (pour accrocher la lisse et le poteau).</summary>
    public string? Embase(ModeleEchafaudage modele) =>
        Fixation == FixationBout.Chape && modele == ModeleEchafaudage.MultiSystem ? References.Embase : null;

    /// <summary>Une lisse peut s'accrocher au bout sans poteau (nipple, ou chape munie d'une embase).</summary>
    public bool LisseSansPoteau(ModeleEchafaudage modele) => Fixation == FixationBout.Nipple || Embase(modele) != null;

    /// <summary>Poteau de garde-corps de 1 m posé en bout.</summary>
    public string PoteauGardeCorps(ModeleEchafaudage modele) =>
        modele == ModeleEchafaudage.MultiSystem ? "HMPOT10"
        : Fixation == FixationBout.Chape ? "HMPOTDP10"
        : "HMPOTST10"; // nipple en Multi-RETO : poteau courant sur l'embase intégrée (à confirmer par Laurent)

    /// <summary>Hauteur du pied du poteau au-dessus du plancher : 4 cm sur une embase (HMEMB ou intégrée) en Multi-SYSTEM.</summary>
    public double DecalageZ(ModeleEchafaudage modele) => modele == ModeleEchafaudage.MultiSystem && Fixation != FixationBout.Platine ? 0.04 : 0;
}

/// <summary>
/// Consoles par modèle d'échafaudage, liste de Laurent du 24/11/2026 (remplace la table v2 CheckConsole) :
/// compatibilité MR / MS et fixation en bout (platine, chape, nipple).
/// </summary>
public static class CatalogueConsoles
{
    private const bool MR = true, MS = true, Non = false;

    private static readonly ConsoleInfo[] Toutes =
    {
        new ConsoleInfo("HMCON300", 0.40, FixationBout.Platine, MR, MS),
        new ConsoleInfo("HMCON400E2", 0.40, FixationBout.Nipple, Non, MS),
        new ConsoleInfo("HMCON400E", 0.40, FixationBout.Chape, MR, MS),
        new ConsoleInfo("HMCON500EE", 0.60, FixationBout.Chape, Non, MS), // largeur reprise de l'ancien HMCON500E (à confirmer)
        new ConsoleInfo("HMCON700E", 0.70, FixationBout.Chape, MR, MS),
        new ConsoleInfo("HMCON700E2", 0.70, FixationBout.Nipple, Non, MS),
        new ConsoleInfo("HMCON700EE", 0.70, FixationBout.Chape, MR, MS),
        new ConsoleInfo("HMCON700EE2", 0.70, FixationBout.Nipple, Non, MS),
        new ConsoleInfo("HMCON750E", 0.75, FixationBout.Chape, MR, MS),
        new ConsoleInfo("HMCON750MR", 0.75, FixationBout.Nipple, MR, Non),
        new ConsoleInfo("HMCON750EE", 0.75, FixationBout.Chape, MR, MS),
        new ConsoleInfo("HMCON750MR2", 0.75, FixationBout.Nipple, MR, Non),
        new ConsoleInfo("HMCON1000E", 1.00, FixationBout.Chape, MR, MS),
        new ConsoleInfo("HMCON1000E2", 1.00, FixationBout.Nipple, Non, MS),
        new ConsoleInfo("HMCON1050E", 1.05, FixationBout.Chape, MR, MS),
        new ConsoleInfo("HMCON1050MR", 1.05, FixationBout.Nipple, MR, Non),
    };

    /// <summary>Toutes les consoles de la liste, quel que soit le modèle.</summary>
    public static IReadOnlyList<ConsoleInfo> Liste => Toutes;

    /// <summary>Consoles proposées pour un modèle (Multi-RETO à embase : mêmes consoles que Multi-RETO).</summary>
    public static IReadOnlyCollection<ConsoleInfo> Pour(ModeleEchafaudage modele) => Toutes.Where(c => c.Compatible(modele)).ToList();

    public static bool TryGet(ModeleEchafaudage modele, string reference, out ConsoleInfo info)
    {
        info = Toutes.FirstOrDefault(c => c.Compatible(modele) && string.Equals(c.Reference, reference, StringComparison.OrdinalIgnoreCase))!;
        return info != null;
    }
}
