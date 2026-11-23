using System;

namespace MultiDEC.Core.Facade.Regles;

/// <summary>
/// Nombre d'accès proposé pour une longueur de façade : un accès, plus un par tranche de 20,50 m
/// (formule de la v2, d'après la recommandation CNAMTS « une zone d'accès tous les 20 m » de NT-Echafaudage).
/// Recalculé quand l'utilisateur change la longueur ; il reste modifiable ensuite.
/// </summary>
public static class RegleAcces
{
    public const double Tranche = 20.5;

    public static int NombreConseille(double longueurFacade) =>
        longueurFacade <= 0 ? 1 : 1 + (int)Math.Floor(longueurFacade / Tranche + 1e-9);
}
