using System;

namespace MultiDEC.Core.Facade;

/// <summary>
/// Une ligne de la grille des mailles, éditable par l'utilisateur.
/// La console est définie par maille (décision Laurent du 23/11/2026) et s'applique
/// à tous les niveaux de plancher de cette maille.
/// </summary>
public sealed class MailleDefinition
{
    public MailleDefinition()
    {
    }

    public MailleDefinition(double longueur, TypeMaille type = TypeMaille.Travail, string? console = null)
    {
        Longueur = longueur;
        Type = type;
        Console = console;
    }

    /// <summary>Longueur de la maille en mètres (0,70 / 1,00 / 1,50 / 2,00 / 2,50 / 3,00 ; 0,75 / 1,05 possibles).</summary>
    public double Longueur { get; set; }

    public TypeMaille Type { get; set; } = TypeMaille.Travail;

    /// <summary>Référence de la console côté intérieur (ex. HMCON700E), ou null si aucune.</summary>
    public string? Console { get; set; }

    public bool AConsole => !string.IsNullOrWhiteSpace(Console);

    public MailleDefinition Copie() => new MailleDefinition(Longueur, Type, Console);

    public override string ToString() =>
        FormattableString.Invariant($"{Longueur:0.00} m {Type}{(AConsole ? " + " + Console : string.Empty)}");
}
