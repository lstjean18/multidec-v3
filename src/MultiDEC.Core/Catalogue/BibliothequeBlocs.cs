using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MultiDEC.Core.Catalogue;

/// <summary>
/// Liste des noms de blocs disponibles dans la bibliothèque (Blocs-EM-3D.dxf).
/// Permet de signaler, avant le dessin, les références générées qui n'ont pas de bloc.
/// </summary>
public sealed class BibliothequeBlocs
{
    private readonly HashSet<string> _noms;

    public BibliothequeBlocs(IEnumerable<string> noms)
    {
        _noms = new HashSet<string>(
            noms.Select(n => n.Trim()).Where(n => n.Length > 0 && !n.StartsWith("*", StringComparison.Ordinal)),
            StringComparer.OrdinalIgnoreCase);
    }

    public int Nombre => _noms.Count;

    /// <summary>Charge un fichier texte : un nom de bloc par ligne (ex. catalogue/blocs-EM-3D.txt).</summary>
    public static BibliothequeBlocs DepuisFichier(string chemin) => new BibliothequeBlocs(File.ReadAllLines(chemin));

    public bool Contient(string reference) => _noms.Contains(reference);

    /// <summary>Références demandées absentes de la bibliothèque, triées et sans doublon.</summary>
    public IReadOnlyList<string> Manquantes(IEnumerable<string> references) =>
        references.Distinct(StringComparer.OrdinalIgnoreCase)
                  .Where(r => !Contient(r))
                  .OrderBy(r => r, StringComparer.Ordinal)
                  .ToList();
}
