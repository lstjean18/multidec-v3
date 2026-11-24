using System;
using System.Collections.Generic;
using System.Linq;
using MultiDEC.Core.Geometrie;

namespace MultiDEC.Core.Facade;

/// <summary>
/// Propose une grille des mailles et une grille des niveaux à partir des paramètres
/// (port nettoyé de frmParam.ProposeStructure). Les deux grilles restent ensuite éditables.
/// </summary>
public static class ProposeurStructure
{
    /// <summary>Longueurs utilisées pour compléter le reste, de la plus grande à la plus petite (1,50 m ajouté en v3).</summary>
    public static readonly IReadOnlyList<double> LongueursComplement = new[] { 3.00, 2.50, 2.00, 1.50, 1.00, 0.70 };

    /// <summary>Longueur de maille la plus courte utilisable.</summary>
    public const double LongueurMini = 0.70;

    /// <summary>Longueur de maille la plus longue utilisable.</summary>
    public const double LongueurMaxi = 3.00;

    /// <summary>
    /// Grille des mailles : les mailles d'accès (longueur préférée) sont réparties entre
    /// NombreAcces + 1 blocs de travail de taille égale ; le reste est complété à droite
    /// par les longueurs standard les plus grandes possibles.
    /// </summary>
    public static List<MailleDefinition> ProposerMailles(ParametresFacade p)
    {
        if (p.LongueurMaillePreferee < LongueurMini - Cotes.Tolerance || p.LongueurMaillePreferee > LongueurMaxi + Cotes.Tolerance)
        {
            throw new ArgumentOutOfRangeException(nameof(p), "Longueur de maille préférée hors plage (0,70 à 3,00 m).");
        }

        double lp = p.LongueurMaillePreferee;
        int nbAcces = Math.Max(0, p.NombreAcces);
        double restant = Cotes.Arrondi(p.LongueurTotaleSouhaitee - nbAcces * lp);
        if (restant < -Cotes.Tolerance)
        {
            throw new InvalidOperationException("La longueur totale est trop courte pour le nombre d'accès demandé.");
        }

        int nbCourantes = (int)Math.Floor((restant + Cotes.Tolerance) / lp);
        double reste = Cotes.Arrondi(restant - nbCourantes * lp);
        var complement = Completer(reste);

        // Si le reste ne tombe pas juste, on essaie de rendre une maille courante au complément
        // (ex. 1,20 m de reste en 2,50 → 3,70 m = 3,00 + 0,70). On garde la solution la plus proche
        // de la longueur souhaitée, puis celle qui a le moins de mailles de complément.
        if (nbCourantes > 0 && reste > Cotes.Tolerance)
        {
            var alternative = Completer(Cotes.Arrondi(reste + lp));
            double ecart = reste - complement.Sum();
            double ecartAlt = reste + lp - alternative.Sum();
            if (ecartAlt < ecart - Cotes.Tolerance || (Cotes.Egal(ecartAlt, ecart) && alternative.Count < complement.Count + 1))
            {
                nbCourantes--;
                complement = alternative;
            }
        }

        int nbBlocs = nbAcces + 1;
        var mailles = new List<MailleDefinition>();
        for (int bloc = 0; bloc < nbBlocs; bloc++)
        {
            // Répartition équilibrée (l'ancienne version mettait tout le surplus dans le premier bloc).
            int nb = nbCourantes / nbBlocs + (bloc < nbCourantes % nbBlocs ? 1 : 0);
            for (int i = 0; i < nb; i++)
            {
                mailles.Add(new MailleDefinition(lp, TypeMaille.Travail));
            }

            if (bloc < nbAcces)
            {
                mailles.Add(new MailleDefinition(lp, TypeMaille.Acces));
            }
        }

        foreach (double l in complement)
        {
            mailles.Add(new MailleDefinition(l, TypeMaille.Travail));
        }

        Regles.RegleDiagonales.Proposer(p, mailles);
        return mailles;
    }

    /// <summary>
    /// Découpe un reste en longueurs standard : somme exacte si possible avec le moins de mailles,
    /// sinon la plus grande somme possible sans dépasser le reste (le reliquat, &lt; 0,70 m, est abandonné).
    /// Les grandes longueurs sont rendues en premier.
    /// </summary>
    public static List<double> Completer(double reste)
    {
        // Programmation dynamique au centimètre (reste ≤ quelques mètres).
        int cible = (int)Math.Round(reste * 100);
        if (cible < (int)Math.Round(LongueurMini * 100))
        {
            return new List<double>();
        }

        int[] pieces = LongueursComplement.Select(l => (int)Math.Round(l * 100)).ToArray();
        var nb = new int[cible + 1];
        var choix = new int[cible + 1];
        for (int s = 1; s <= cible; s++)
        {
            nb[s] = int.MaxValue;
            foreach (int pc in pieces)
            {
                if (pc <= s && nb[s - pc] != int.MaxValue && nb[s - pc] + 1 < nb[s])
                {
                    nb[s] = nb[s - pc] + 1;
                    choix[s] = pc;
                }
            }
        }

        int atteint = cible;
        while (atteint > 0 && nb[atteint] == int.MaxValue)
        {
            atteint--;
        }

        var resultat = new List<double>();
        for (int s = atteint; s > 0; s -= choix[s])
        {
            resultat.Add(choix[s] / 100.0);
        }

        resultat.Sort((a, b) => b.CompareTo(a));
        return resultat;
    }

    /// <summary>
    /// Grille des niveaux : du dernier plancher vers le bas, tous les 2 m.
    /// Niveaux &gt; 0 en plancher complet, niveau 0 en accès seul (comme l'ancienne version).
    /// </summary>
    public static List<Niveau> ProposerNiveaux(ParametresFacade p)
    {
        double h = p.HauteurDernierPlancher;
        if (h <= 0 || Math.Abs(h * 2 - Math.Round(h * 2)) > Cotes.Tolerance)
        {
            throw new ArgumentOutOfRangeException(nameof(p), "La hauteur du dernier plancher doit être un multiple de 0,50 m.");
        }

        var niveaux = new List<Niveau>();
        for (double z = h; z > -Cotes.Tolerance; z = Cotes.Arrondi(z - 2.0))
        {
            niveaux.Add(new Niveau(Cotes.Arrondi(z), z > Cotes.Tolerance ? TypePlancher.Complet : TypePlancher.AccesSeul));
        }

        niveaux.Reverse();
        return niveaux;
    }

    /// <summary>Crée un projet complet avec les deux grilles proposées.</summary>
    /// <summary>
    /// Grille d'une face qui commence par le carré d'angle (jonction avec la face précédente) :
    /// une maille de longueur = largeur de maille, comprise dans la longueur souhaitée, puis la proposition habituelle.
    /// </summary>
    public static List<MailleDefinition> ProposerMailles(ParametresFacade p, bool carreDAngle, double? largeurFacePrecedente = null)
    {
        var mailles = ProposerMailles(p);
        if (carreDAngle)
        {
            // La longueur saisie ne comprend jamais le carré d'angle (Laurent, 24/11/2026) : il s'ajoute devant,
            // de longueur = largeur de la face précédente (ex. 20 m saisis derrière une face de 0,70 m → 20,70 m).
            mailles.Insert(0, new MailleDefinition(largeurFacePrecedente ?? p.LargeurMaille, TypeMaille.Travail));
        }

        return mailles;
    }

    public static FaceEchafaudage NouveauProjet(ParametresFacade p) => new FaceEchafaudage
    {
        Parametres = p,
        Mailles = ProposerMailles(p),
        Niveaux = ProposerNiveaux(p),
    };
}
