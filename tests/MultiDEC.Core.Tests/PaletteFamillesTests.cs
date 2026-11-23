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
    public void Plateaux_distincts_des_tubes()
    {
        Assert.NotEqual(PaletteFamilles.Couleur(Famille.Poteau), PaletteFamilles.Couleur(Famille.Plateau));
        Assert.All(Enum.GetValues(typeof(Famille)).Cast<Famille>(), f => Assert.NotEqual(0, PaletteFamilles.Couleur(f).A));
    }
}
