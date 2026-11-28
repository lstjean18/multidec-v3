using System;
using System.Collections.Generic;
using System.Globalization;

namespace MultiDEC.Core.Facade.Regles;

/// <summary>Une maille d'accès ne fait jamais moins de 1,50 m (Laurent, 25/11/2026) : sinon refus de dessiner.</summary>
public static class RegleMaillesAcces
{
    public const double LongueurMini = 1.50;

    public const string Rappel = "Règle : une maille d'accès mesure au moins 1,50 m (1,50 / 2,00 / 2,50 / 3,00 m).";

    public static bool LongueurPermise(TypeMaille type, double longueur) =>
        type != TypeMaille.Acces || longueur >= LongueurMini - Geometrie.Cotes.Tolerance;

    public static List<string> Ecarts(ProjetStructure projet)
    {
        var ecarts = new List<string>();
        var fr = CultureNombres.Documents;
        foreach (var f in projet.Faces)
        {
            for (int i = 0; i < f.Mailles.Count; i++)
            {
                var m = f.Mailles[i];
                if (!LongueurPermise(m.Type, m.Longueur))
                {
                    ecarts.Add(string.Format(fr, "{0}, maille {1} : maille d'accès de {2:0.00} m, trop courte.", f.Nom, i + 1, m.Longueur));
                }
            }
        }

        return ecarts;
    }

    public static void Verifier(ProjetStructure projet)
    {
        var ecarts = Ecarts(projet);
        if (ecarts.Count > 0)
        {
            throw new InvalidOperationException(
                string.Join(Environment.NewLine, ecarts) + Environment.NewLine + Environment.NewLine + Rappel +
                Environment.NewLine + "La structure n'est pas dessinée.");
        }
    }
}
