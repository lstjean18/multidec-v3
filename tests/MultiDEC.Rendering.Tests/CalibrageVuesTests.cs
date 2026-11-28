using System;
using System.Globalization;
using VectorDraw.Geometry;
using VectorDraw.Professional.Components;
using Xunit;
using Xunit.Abstractions;

namespace MultiDEC.Rendering.Tests;

/// <summary>Affiche où tombent les axes monde à l'écran pour chaque vue prédéfinie (Z doit monter).</summary>
public class CalibrageVuesTests
{
    private readonly ITestOutputHelper _sortie;

    public CalibrageVuesTests(ITestOutputHelper sortie) => _sortie = sortie;

    [Fact]
    public void Vues()
    {
        var doc = new vdDocumentComponent().Document;
        foreach (var vue in new[] { "VISE", "VISW", "VINE", "VINW", "VFRONT", "VTOP", "VLEFT", "VRIGHT" })
        {
            try
            {
                doc.CommandAction.View3D(vue);
                var m = doc.World2ViewMatrix;
                var o = m.Transform(new gPoint(0, 0, 0));
                string Axe(double x, double y, double z)
                {
                    var p = m.Transform(new gPoint(x, y, z));
                    return string.Format(CultureInfo.InvariantCulture, "({0:0.##};{1:0.##};{2:0.##})", p.x - o.x, p.y - o.y, p.z - o.z);
                }

                _sortie.WriteLine($"{vue,-7} X→{Axe(1, 0, 0)}  Y→{Axe(0, 1, 0)}  Z→{Axe(0, 0, 1)}");
            }
            catch (Exception ex)
            {
                _sortie.WriteLine($"{vue,-7} ERREUR {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
