using System;

namespace MultiDEC.Core.Facade;

/// <summary>Un niveau de plancher (hauteur en mètres au-dessus du départ) et son type.</summary>
public sealed class Niveau
{
    public Niveau()
    {
    }

    public Niveau(double hauteur, TypePlancher type)
    {
        Hauteur = hauteur;
        Type = type;
    }

    public double Hauteur { get; set; }

    public TypePlancher Type { get; set; } = TypePlancher.Complet;

    public bool EstAccesSeul => Type != TypePlancher.Complet;

    public override string ToString() => FormattableString.Invariant($"{Hauteur:0.00} m {Type}");
}
