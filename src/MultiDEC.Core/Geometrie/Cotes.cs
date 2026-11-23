using System;
using System.Globalization;

namespace MultiDEC.Core.Geometrie;

/// <summary>Outils sur les cotes (mètres) : comparaison avec tolérance et codage dans les références.</summary>
public static class Cotes
{
    /// <summary>Tolérance de comparaison : 1 mm.</summary>
    public const double Tolerance = 0.001;

    public static bool Egal(double a, double b) => Math.Abs(a - b) < Tolerance;

    /// <summary>Arrondi au mm, pour éviter les 2.9999999 des additions successives.</summary>
    public static double Arrondi(double valeur) => Math.Round(valeur, 3);

    /// <summary>Code d'une longueur dans une référence, en cm sur 3 chiffres : 2,50 m → "250", 0,70 m → "070".</summary>
    public static string EnCm3(double metres) =>
        ((int)Math.Round(metres * 100)).ToString("000", CultureInfo.InvariantCulture);

    /// <summary>Code d'une longueur de poteau, en dm sur 2 chiffres : 2,00 m → "20", 0,50 m → "05".</summary>
    public static string EnDm2(double metres) =>
        ((int)Math.Round(metres * 10)).ToString("00", CultureInfo.InvariantCulture);
}
