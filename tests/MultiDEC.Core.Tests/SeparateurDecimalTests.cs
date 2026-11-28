using System;
using System.Globalization;
using System.Linq;
using MultiDEC.Core.Decompte;
using MultiDEC.Core.Facade;
using MultiDEC.Core.MiseEnPlan;
using Xunit;

namespace MultiDEC.Core.Tests;

/// <summary>Remarque 64 (Laurent, 28/11/2026) : séparateur décimal du poste partout, saisie avec « . » ou « , ».</summary>
public class SeparateurDecimalTests
{
    /// <summary>Poste français réglé sur le point décimal (réglage personnalisé de Windows).</summary>
    private static CultureInfo PostePoint()
    {
        var c = (CultureInfo)CultureInfo.GetCultureInfo("fr-FR").Clone();
        c.NumberFormat.NumberDecimalSeparator = ".";
        return c;
    }

    private static T Avec<T>(CultureInfo culture, Func<T> action)
    {
        var avant = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = culture;
        try
        {
            return action();
        }
        finally
        {
            CultureInfo.CurrentCulture = avant;
        }
    }

    [Fact]
    public void Documents_en_francais_avec_le_separateur_du_poste()
    {
        Assert.Equal("2,50", Cote.Format(2.5));
        Assert.Equal("2.50", Avec(PostePoint(), () => Cote.Format(2.5)));
        Assert.Equal("2.50", Avec(CultureInfo.GetCultureInfo("en-US"), () => Cote.Format(2.5)));
        Assert.Equal("1 234.5", Avec(PostePoint(), () => 1234.5.ToString("N1", CultureNombres.Documents)));
        Assert.Equal("1 234,5", 1234.5.ToString("N1", CultureNombres.Documents));
        Assert.Equal("28/11/2026", Avec(PostePoint(), () => new DateTime(2026, 11, 28).ToString("d", CultureNombres.Documents)));
    }

    [Fact]
    public void Textes_remis_au_separateur_du_poste()
    {
        Assert.Equal("Lisse GC accès 1.50 m au niveau 0", Avec(PostePoint(), () => CultureNombres.Localiser("Lisse GC accès 1,50 m au niveau 0")));
        Assert.Equal("About de 1,20 m : plus de 1,05 m", CultureNombres.Localiser("About de 1.20 m : plus de 1,05 m"));
        // Ni les énumérations « 3, 4 », ni les numéros de version, ni les fins de phrase.
        Assert.Equal("poutres de 3, 4, 5 ou 6 m", Avec(PostePoint(), () => CultureNombres.Localiser("poutres de 3, 4, 5 ou 6 m")));
        Assert.Equal("version 3.0.1 ; maille 3.", CultureNombres.Localiser("version 3.0.1 ; maille 3."));
        Assert.Equal("multidec.bib", CultureNombres.Localiser("multidec.bib"));
    }

    [Theory]
    [InlineData("1,50", 1.5)]
    [InlineData("1.50", 1.5)]
    [InlineData(" 12 ", 12)]
    [InlineData("1 234,5", 1234.5)]
    public void Saisie_avec_point_ou_virgule(string texte, double attendu)
    {
        Assert.True(CultureNombres.Lire(texte, out double v));
        Assert.Equal(attendu, v, 9);
        Assert.False(CultureNombres.Lire("abc", out _));
        Assert.Equal("1.5", Avec(PostePoint(), () => CultureNombres.Normaliser("1,5")));
        Assert.Equal("1,5", CultureNombres.Normaliser("1.5"));
    }

    [Fact]
    public void Liste_du_materiel_au_point_decimal()
    {
        var lignes = new[] { new LigneDecompte("HMPOT20", Famille.Poteau, 1000, new Article("HMPOT20", "Poteau de 2,00m", 9.1, "Poteaux", 2.06)) };
        var textes = Avec(PostePoint(), () => ListeMateriel.Feuilles(new EnTeteListe(), lignes, Array.Empty<string>()).Single().Textes.Select(t => t.Contenu).ToList());
        Assert.Contains("9.10", textes);
        Assert.Contains("9 100.0", textes);
        Assert.Contains("1 000", textes);
    }
}
