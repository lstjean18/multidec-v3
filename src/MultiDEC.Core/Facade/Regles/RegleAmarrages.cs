using System;
using System.Collections.Generic;
using MultiDEC.Core.Geometrie;

namespace MultiDEC.Core.Facade.Regles;

/// <summary>Un amarrage : une file de poteaux et une hauteur.</summary>
public sealed class Amarrage
{
    public Amarrage(int file, double x, double hauteur)
    {
        File = file;
        X = x;
        Hauteur = hauteur;
    }

    /// <summary>Indice de la file de poteaux (0 = file de gauche).</summary>
    public int File { get; }

    public double X { get; }

    public double Hauteur { get; }
}

/// <summary>Bilan des amarrages, comparé au contrôle par surface de la notice.</summary>
public sealed class BilanAmarrages
{
    public BilanAmarrages(IReadOnlyList<Amarrage> amarrages, double surface, int minimumParSurface, bool noteDeCalcul)
    {
        Amarrages = amarrages;
        Surface = surface;
        MinimumParSurface = minimumParSurface;
        NoteDeCalculObligatoire = noteDeCalcul;
    }

    public IReadOnlyList<Amarrage> Amarrages { get; }

    /// <summary>Surface de façade couverte (longueur × hauteur totale), m².</summary>
    public double Surface { get; }

    /// <summary>Nombre minimal d'ancrages : 1 pour 24 m² (non bâché) ou 1 pour 10 m² (bâché).</summary>
    public int MinimumParSurface { get; }

    public bool SurfaceRespectee => Amarrages.Count >= MinimumParSurface;

    public bool NoteDeCalculObligatoire { get; }
}

/// <summary>
/// Placement des amarrages (NT-Echafaudage p. 8-13, validé par Laurent le 23/11/2026) :
/// toutes les files sont amarrées, premier amarrage à 2 m, puis espacement vertical
/// de 4 m sur les files d'extrémité et de 8 m (non bâché) ou 4 m (bâché) sur les files courantes.
/// Quinconce entre files voisines : sur une file sur deux, le 2e amarrage est décalé
/// d'un demi-espacement.
/// </summary>
public static class RegleAmarrages
{
    public const double SurfaceParAncrageNonBache = 24.0;
    public const double SurfaceParAncrageBache = 10.0;

    /// <summary>Effort mini à reprendre par ancrage (daN), pour information sur le plan.</summary>
    public const double EffortMiniDaN = 300.0;

    public static BilanAmarrages Calculer(ProjetFacade projet)
    {
        var p = projet.Parametres;
        var x = projet.AbscissesFiles();
        int derniere = x.Count - 1;
        double hMax = p.HauteurDernierPlancher;
        var liste = new List<Amarrage>();

        for (int f = 0; f <= derniere; f++)
        {
            bool extremite = f == 0 || f == derniere;
            double pas = extremite
                ? p.EspacementAmarrageExtremite
                : (p.Bache ? p.EspacementAmarrageCourantBache : p.EspacementAmarrageCourant);

            // Les files courantes impaires sont décalées d'un demi-pas après le premier amarrage.
            bool quinconce = !extremite && f % 2 == 1;
            double z = p.HauteurPremierAmarrage;
            bool premier = true;
            while (z <= hMax + Cotes.Tolerance)
            {
                liste.Add(new Amarrage(f, x[f], Cotes.Arrondi(z)));
                z += premier && quinconce ? pas / 2 : pas;
                premier = false;
            }
        }

        double surface = Cotes.Arrondi(projet.LongueurObtenue * projet.HauteurTotale);
        double parAncrage = p.Bache ? SurfaceParAncrageBache : SurfaceParAncrageNonBache;
        int mini = (int)Math.Ceiling(surface / parAncrage - Cotes.Tolerance);
        return new BilanAmarrages(liste, surface, mini, projet.NoteDeCalculObligatoire);
    }
}
