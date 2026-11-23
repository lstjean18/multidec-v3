using System;
using System.Collections.Generic;
using System.Linq;
using MultiDEC.Core.Facade;

namespace MultiDEC.Core.Decompte;

/// <summary>Une ligne du décompte matériel.</summary>
public sealed class LigneDecompte
{
    public LigneDecompte(string reference, Famille famille, int quantite)
    {
        Reference = reference;
        Famille = famille;
        Quantite = quantite;
    }

    public string Reference { get; }

    public Famille Famille { get; }

    public int Quantite { get; }

    public override string ToString() => $"{Reference} x{Quantite}";
}

/// <summary>
/// Décompte calculé à partir du modèle (et non plus en relisant les blocs du dessin comme en v2).
/// Les désignations, prix et poids viendront du tarif (à brancher).
/// </summary>
public static class CalculDecompte
{
    public static IReadOnlyList<LigneDecompte> Calculer(IEnumerable<ElementPlace> elements) =>
        elements.GroupBy(e => e.Reference, StringComparer.OrdinalIgnoreCase)
                .Select(g => new LigneDecompte(g.First().Reference, g.First().Famille, g.Count()))
                .OrderBy(l => l.Famille)
                .ThenBy(l => l.Reference, StringComparer.Ordinal)
                .ToList();
}
