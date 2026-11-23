using System.Collections.Generic;
using System.Linq;

namespace MultiDEC.Core.Facade.Regles;

/// <summary>Une maille qui reçoit des diagonales verticales.</summary>
public sealed class MailleDiagonale
{
    public MailleDiagonale(int numeroMaille, bool inverse, bool interieure)
    {
        NumeroMaille = numeroMaille;
        Inverse = inverse;
        Interieure = interieure;
    }

    /// <summary>Numéro de maille, de 1 à n.</summary>
    public int NumeroMaille { get; }

    /// <summary>Sens alterné d'une maille à diagonale à la suivante.</summary>
    public bool Inverse { get; }

    /// <summary>Diagonales aussi côté intérieur (garde-corps des deux côtés).</summary>
    public bool Interieure { get; }
}

/// <summary>
/// Répartition des diagonales verticales (NT-Echafaudage + ancien MultiRETO/MultiSystem) :
/// une maille sur 5 avec garde-corps MDS (1 m), une sur 3 avec lisse + sous-lisse ;
/// diagonales renforcées : une sur 3 (MDS) ou une sur 2 (lisses).
/// Les diagonales occupent toute la hauteur de la maille.
/// Nouveauté v3 : avec une protection intérieure, mêmes mailles côté intérieur.
/// </summary>
public static class RegleDiagonales
{
    public static int Pas(ParametresFacade p)
    {
        bool mds = p.SecuriteExterieure == TypeSecurite.GardeCorpsMds1 || p.SecuriteExterieure == TypeSecurite.GardeCorpsMds2;
        if (p.DiagonalesRenforcees)
        {
            return mds ? 3 : 2;
        }

        return mds ? 5 : 3;
    }

    public static List<MailleDiagonale> Calculer(ProjetFacade projet)
    {
        var p = projet.Parametres;
        int pas = Pas(p);
        int n = projet.Mailles.Count;
        bool interieur = p.SecuriteInterieure != TypeSecurite.Sans;
        var resultat = new List<MailleDiagonale>();
        bool inverse = false;
        for (int k = 1; k <= n; k++)
        {
            if ((k - 1) % pas != 0)
            {
                continue;
            }

            // Première maille : toujours si la façade dépasse un pas, sinon seulement sur option.
            if (k == 1 && n <= pas && !p.DiagonalePremiereMaille)
            {
                continue;
            }

            resultat.Add(new MailleDiagonale(k, inverse, interieur));
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
