using System;
using System.IO;
using System.Linq;
using System.Reflection;
using VectorDraw.Professional.Components;
using VectorDraw.Professional.vdObjects;
using VectorDraw.Professional.vdPrimaries;
using Xunit;
using Xunit.Abstractions;

namespace MultiDEC.Rendering.Tests;

/// <summary>
/// Exploration : comment faire lire à VectorDraw les solides 3D (ACIS) de Blocs-EM-3D.dxf ?
/// L'ouverture par défaut (vdDXF) laisse le bloc plateau vide. Le test essaie les options d'ouverture
/// et décrit le convertisseur vdxFcnv ; il ne fait qu'écrire ce qu'il observe.
/// </summary>
public class ImportSolidesTests
{
    private readonly ITestOutputHelper _sortie;

    public ImportSolidesTests(ITestOutputHelper sortie) => _sortie = sortie;

    [Fact]
    public void Explorer()
    {
        var gz = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Donnees", "blocs-facade-test.dxf.gz");
        var dxf = Path.Combine(Path.GetTempPath(), "multidec-import-solides.dxf");
        using (var e = new System.IO.Compression.GZipStream(File.OpenRead(gz), System.IO.Compression.CompressionMode.Decompress))
        using (var s = File.Create(dxf))
        {
            e.CopyTo(s);
        }

        var doc0 = new vdDocumentComponent().Document;
        var flagsProp = doc0.GetType().GetProperty("Openflags");
        _sortie.WriteLine($"Openflags : type {flagsProp?.PropertyType.FullName}, valeur par défaut {flagsProp?.GetValue(doc0)}");
        if (flagsProp != null && flagsProp.PropertyType.IsEnum)
        {
            foreach (var n in Enum.GetNames(flagsProp.PropertyType))
            {
                _sortie.WriteLine($"   {n} = {Convert.ToInt64(Enum.Parse(flagsProp.PropertyType, n))}");
            }
        }

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name is "vdxFcnv" or "vdDXF" or "VectorDraw.SolidModel"))
        {
            Type[] types;
            try
            {
                types = asm.GetExportedTypes();
            }
            catch (Exception ex)
            {
                _sortie.WriteLine($"{asm.GetName().Name} : {ex.GetBaseException().Message}");
                continue;
            }

            foreach (var t in types.Take(30))
            {
                var m = t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Select(x => (x.IsStatic ? "static " : string.Empty) + x.Name + "(" + string.Join(", ", x.GetParameters().Select(p => p.ParameterType.Name)) + ")").Distinct().Take(25);
                _sortie.WriteLine($"{asm.GetName().Name} {t.FullName} : {string.Join(" | ", m)}");
            }
        }

        Essayer("par défaut", d => { });
        if (flagsProp != null && flagsProp.PropertyType.IsEnum)
        {
            foreach (var n in Enum.GetNames(flagsProp.PropertyType))
            {
                var valeur = Enum.Parse(flagsProp.PropertyType, n);
                Essayer("Openflags=" + n, d => flagsProp.SetValue(d, valeur));
            }
        }

        void Essayer(string titre, Action<vdDocument> regler)
        {
            try
            {
                var d = new vdDocumentComponent().Document;
                regler(d);
                bool ok = d.Open(dxf);
                var types = new System.Collections.Generic.Dictionary<string, int>();
                foreach (vdBlock b in d.Blocks)
                {
                    foreach (vdFigure f in b.Entities)
                    {
                        var k = f.GetType().Name;
                        types[k] = types.TryGetValue(k, out var c) ? c + 1 : 1;
                    }
                }

                var plateau = d.Blocks.FindName("HMPLA250298CL6");
                _sortie.WriteLine($"[{titre}] Open={ok}, plateau : {plateau?.Entities.Count} entités ; blocs : {string.Join(", ", types.OrderByDescending(x => x.Value).Select(x => x.Value + "×" + x.Key))}");
            }
            catch (Exception ex)
            {
                _sortie.WriteLine($"[{titre}] {ex.GetBaseException().GetType().Name} : {ex.GetBaseException().Message}");
            }
        }
    }
}
