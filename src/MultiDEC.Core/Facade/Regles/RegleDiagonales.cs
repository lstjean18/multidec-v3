using System.Collections.Generic;
using System.Linq;

namespace MultiDEC.Core.Facade.Regles;

/// <summary>Une maille qui reçoit des diagonales verticales.</summary>
public sealed class MailleDiagonale
{
    public MailleDiagonale(int numeroMaille, bool inverse, bool interieure, bool exterieure = true)
    {
        NumeroMaille = numeroMaille;
        Inverse = inverse;
        Interieure = interieure;
        Exterieure = exterieure;
    }

    /// <summary>Numéro de maille, de 1 à n.</summary>
    public int NumeroMaille { get; }

    /// <summary>Sens alterné d'une maille à diagonale à la suivante.</summary>
    public bool Inverse { get; }

    /// <summary>Diagonales côté intérieur (protection côté façade).</summary>
    public bool Interieure { get; }

    /// <summary>Diagonales côté extérieur (protection côté extérieur).</summary>
    public bool Exterieure { get; }
}

/// <summary>
/// Répartition des diagonales verticales : une maille sur 5, ou une sur 2 avec l'option
/// « diagonales 1 maille sur 2 » (règle MultiDEC v2 version2024).
/// Les diagonales occupent toute la hauteur de la maille.
/// Nouveauté v3 : avec une protection intérieure, mêmes mailles côté intérieur.
/// Pas de diagonale sur un côté sans garde-corps ; mailles choisies par case à cocher, pré-cochées par la règle (Laurent, 23/11/2026).
/// </summary>
public static class RegleDiagonales
{
    /// <summary>
    /// Pas des diagonales, règle de MultiDEC v2 (branche version2024, retenue par Laurent le 23/11/2026) :
    /// une maille sur 5, ou une sur 2 avec l'option « diagonales 1 maille sur 2 », quel que soit le garde-corps.
    /// </summary>
    public static int Pas(ParametresFacade p) => p.DiagonalesRenforcees ? 2 : 5;

    /// <summary>
    /// Pré-coche la case « Diagonale » des mailles selon la règle (une sur 5, ou une sur 2 avec l'option).
    /// L'utilisateur coche ou décoche ensuite librement dans la grille des mailles.
    /// </summary>
    public static void Proposer(ParametresFacade p, IList<MailleDefinition> mailles)
    {
        int pas = Pas(p);
        int n = mailles.Count;
        for (int k = 1; k <= n; k++)
        {
            // Première maille : toujours si la façade dépasse un pas, sinon seulement sur option.
            bool coche = (k - 1) % pas == 0 && !(k == 1 && n <= pas && !p.DiagonalePremiereMaille);
            mailles[k - 1].Diagonale = coche;
        }
    }

    /// <summary>
    /// Diagonales à poser : les mailles cochées, sens alterné de l'une à la suivante, sur les côtés
    /// qui ont un garde-corps (aucun côté → aucune diagonale).
    /// </summary>
    /// <param name="imposees">Mailles (index à partir de 0) qui reçoivent des diagonales même décochées :
    /// voisines d'une poutre de franchissement, côtés exposés d'un module volumique (Laurent, 25/11/2026).</param>
    public static List<MailleDiagonale> Calculer(FaceEchafaudage projet, ICollection<int>? imposees = null)
    {
        var p = projet.Parametres;
        bool interieur = p.SecuriteInterieure != TypeSecurite.Sans;
        bool exterieur = p.SecuriteExterieure != TypeSecurite.Sans;
        var resultat = new List<MailleDiagonale>();
        if (!interieur && !exterieur)
        {
            return resultat;
        }

        bool inverse = false;
        for (int k = 1; k <= projet.Mailles.Count; k++)
        {
            if (!projet.Mailles[k - 1].Diagonale && !(imposees?.Contains(k - 1) ?? false))
            {
                continue;
            }

            resultat.Add(new MailleDiagonale(k, inverse, interieur, exterieur));
            inverse = !inverse;
        }

        return resultat;
    }

    /// <summary>
    /// Conseil de l'ancienne version (CheckDiago) : au-delà de 2 niveaux, si les niveaux
    /// « accès seul » sont au moins aussi nombreux que les niveaux complets, renforcer les diagonales.
    /// </summary>
    public static bool RenforcementConseille(IReadOnlyCollection<Niveau> niveaux)
    {
        if (niveaux.Count <= 2)
        {
            return false;
        }

        int acces = niveaux.Count(n => n.EstAccesSeul);
        return acces >= niveaux.Count - acces;
    }
}
