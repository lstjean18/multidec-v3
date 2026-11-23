using System;
using System.IO;
using System.Linq;
using System.Reflection;
using VectorDraw.Professional.Components;
using VectorDraw.Professional.vdFigures;
using Xunit;
using Xunit.Abstractions;

namespace MultiDEC.Rendering.Tests;

/// <summary>Exploration : API VectorDraw 9.9003 de sélection (poignées) et d'effacement, pour le clic droit « Supprimer ».</summary>
public class SelectionApiTests
{
    private readonly ITestOutputHelper _sortie;

    public SelectionApiTests(ITestOutputHelper sortie) => _sortie = sortie;

    [Fact]
    public void Decrire()
    {
        var doc = new vdDocumentComponent().Document;
        var types = new[] { doc.GetType(), doc.Model.GetType(), doc.CommandAction.GetType(), typeof(vdInsert), doc.Selections.GetType() }.ToList();
        var dir = Environment.GetEnvironmentVariable("VectorDrawDir") ?? @"C:\Program Files (x86)\VectorDraw\Common\";
        foreach (var dll in new[] { "VDrawBaseControl.dll", "vdScrollableControl.dll" })
        {
            try
            {
                var asm = Assembly.LoadFrom(Path.Combine(dir, dll));
                types.AddRange(asm.GetExportedTypes().Where(t => t.Name is "VectorDrawBaseControl" or "vdScrollableControl" or "BaseControl"));
            }
            catch (Exception ex)
            {
                _sortie.WriteLine(dll + " : " + ex.GetBaseException().Message);
            }
        }

        string[] mots = { "Grip", "Select", "Erase", "Delete", "Pick", "Context", "Mouse", "Tabs" };
        foreach (var t in types.Distinct())
        {
            var membres = t.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Where(m => mots.Any(w => m.Name.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0))
                .Select(m => m is MethodInfo mi ? mi.Name + "(" + string.Join(", ", mi.GetParameters().Select(p => p.ParameterType.Name)) + ")" : m.MemberType + " " + m.Name)
                .Distinct().OrderBy(x => x);
            _sortie.WriteLine($"{t.FullName} : {string.Join(" | ", membres)}");
        }

        _sortie.WriteLine("Selections : " + string.Join(", ", doc.Selections.Cast<object>().Select(s => s.GetType().GetProperty("Name")?.GetValue(s))));
    }
}
