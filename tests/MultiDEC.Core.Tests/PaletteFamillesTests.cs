using System;
using System.Linq;
using MultiDEC.Core.Facade;
using MultiDEC.Rendering.VectorDraw;
using Xunit;

namespace MultiDEC.Core.Tests;

public class PaletteFamillesTests
{
    [Fact]
    public void Couleurs_de_la_v2()
    {
        Assert.Equal(PaletteFamilles.Vert, PaletteFamilles.Couleur(Famille.Plinthe));
        Assert.Equal(PaletteFamilles.Rouge, PaletteFamilles.Couleur(Famille.DiagonaleVerticale));
        Assert.Equal(PaletteFamilles.Orange, PaletteFamilles.Couleur(Famille.Socle));
        Assert.Equal(PaletteFamilles.Bois, PaletteFamilles.Couleur(Famille.Echelle));
        Assert.Equal(PaletteFamilles.Acier, PaletteFamilles.Couleur(Famille.Poteau));
    }

    [Fact]
    public void Volees_en_bleu_et_montants_de_garde_corps_en_violet()
    {
        var o = new MultiDEC.Core.Geometrie.Point3(0, 0, 0);
        Assert.Equal(PaletteFamilles.Bleu, PaletteFamilles.Couleur(new ElementPlace("HMESCALU250200", Famille.Escalier, o, Axe.X)));
        Assert.Equal(PaletteFamilles.Violet, PaletteFamilles.Couleur(new ElementPlace("HMMONTGCE", Famille.GardeCorps, o, Axe.Z)));
        Assert.Equal(PaletteFamilles.Acier, PaletteFamilles.Couleur(new ElementPlace("HMGCSE250GT", Famille.GardeCorps, o, Axe.X)));
    }

    [Fact]
    public void Plateaux_distincts_des_tubes()
    {
        Assert.NotEqual(PaletteFamilles.Couleur(Famille.Poteau), PaletteFamilles.Couleur(Famille.Plateau));
        Assert.All(Enum.GetValues(typeof(Famille)).Cast<Famille>(), f => Assert.NotEqual(0, PaletteFamilles.Couleur(f).A));
    }
}
