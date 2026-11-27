using System;
using System.IO;
using System.Text;
using MultiDEC.Core.MiseAJour;
using Xunit;

namespace MultiDEC.Core.Tests;

/// <summary>Mise à jour automatique (Laurent, 27/11/2026) : fiche version.json publiée avec l'installateur.</summary>
public class FicheVersionTests
{
    private const string Json = "{\"version\":\"3.0.2\",\"libelle\":\"v3.0 bêta 2\",\"installateur\":\"MultiDEC-v3-Setup-3.0.2.exe\",\"sha256\":\"ABC\"}";

    [Theory]
    [InlineData("3.0.1", 3, 0, 1)]
    [InlineData("v3.0.1", 3, 0, 1)]
    [InlineData("3.0.1-beta", 3, 0, 1)]
    [InlineData("3.1", 3, 1, 0)]
    [InlineData("3", 3, 0, 0)]
    public void Numero_de_version_lisible(string texte, int majeur, int mineur, int correctif)
    {
        Assert.Equal(new Version(majeur, mineur, correctif, 0), FicheVersion.NumeroDe(texte));
    }

    [Fact]
    public void Numero_illisible_refuse()
    {
        Assert.Null(FicheVersion.NumeroDe("bêta"));
        Assert.Throws<InvalidDataException>(() => FicheVersion.Lire("{\"version\":\"bêta\",\"installateur\":\"a.exe\",\"sha256\":\"x\"}"));
        Assert.Throws<InvalidDataException>(() => FicheVersion.Lire("{\"version\":\"3.0.2\",\"installateur\":\"\",\"sha256\":\"x\"}"));
    }

    [Fact]
    public void Comparaison_avec_la_version_installee()
    {
        var fiche = FicheVersion.Lire(Json);
        Assert.True(fiche.EstPlusRecenteQue(new Version(3, 0, 0, 0)));
        Assert.True(fiche.EstPlusRecenteQue(new Version(3, 0, 1)));
        Assert.False(fiche.EstPlusRecenteQue(new Version(3, 0, 2, 0)));
        Assert.False(fiche.EstPlusRecenteQue(new Version(3, 1)));
    }

    [Fact]
    public void Installateur_a_cote_de_la_fiche()
    {
        var fiche = FicheVersion.Lire(Json);
        Assert.Equal("https://exemple.fr/versions/MultiDEC-v3-Setup-3.0.2.exe", fiche.AdresseInstallateur("https://exemple.fr/versions/version.json"));
        Assert.Equal(
            "https://github.com/retotub/multidec-v3-versions/releases/latest/download/MultiDEC-v3-Setup-3.0.2.exe",
            fiche.AdresseInstallateur("https://github.com/retotub/multidec-v3-versions/releases/latest/download/version.json"));
        Assert.Equal(Path.Combine("partage", "MultiDEC-v3-Setup-3.0.2.exe"), fiche.AdresseInstallateur(Path.Combine("partage", "version.json")));

        fiche.Installateur = "https://autre.fr/setup.exe";
        Assert.Equal("https://autre.fr/setup.exe", fiche.AdresseInstallateur("https://exemple.fr/versions/version.json"));
    }

    [Fact]
    public void Empreinte_controlee_et_aller_retour_json()
    {
        var contenu = Encoding.UTF8.GetBytes("installateur");
        var fiche = new FicheVersion { Version = "3.0.2", Installateur = "a.exe", Sha256 = FicheVersion.Empreinte(new MemoryStream(contenu)).ToUpperInvariant() };
        Assert.True(fiche.EmpreinteConforme(new MemoryStream(contenu)));
        Assert.False(fiche.EmpreinteConforme(new MemoryStream(Encoding.UTF8.GetBytes("autre"))));

        var relue = FicheVersion.Lire(fiche.Ecrire());
        Assert.Equal(fiche.Sha256, relue.Sha256);
        Assert.Equal("3.0.2", relue.Version);
    }
}
