using System;
using System.Collections.Generic;
using System.Linq;
using MultiDEC.Core.Catalogue;
using MultiDEC.Core.Facade.Regles;
using MultiDEC.Core.Geometrie;

namespace MultiDEC.Core.Facade;

/// <summary>
/// Sous-mailles rapportées côté extérieur et stabilisations (port de clModuleMaille v2 :
/// AjouteSousMailleAcces, AjouteSousMailleModulappro, AjouteSousMailleRecette, AjouteSousMailleStabilisation,
/// et des files supplémentaires de clJonction).
/// Une sous-maille rapportée prolonge une maille de travail vers l'extérieur, de la file extérieure (y = -largeur)
/// à une file de poteaux supplémentaire (y = -largeur - profondeur) :
/// - accès rapporté (0,70 m) : colonne d'accès sur toute la hauteur, trappes et échelles en sens alterné,
///   le garde-corps extérieur passe sur la file rapportée ;
/// - module d'appro (2,00 m) : plancher et garde-corps basculant (HMGCAPPRO…) aux niveaux cochés « Appro » ;
/// - recette (profondeur choisie) : plancher et garde-corps aux niveaux cochés « Recette ».
/// La sous-maille monte au moins jusqu'à la hauteur max de sa maille ; aux niveaux desservis, la maille principale
/// n'a pas de garde-corps extérieur (elle s'ouvre sur la sous-maille).
/// </summary>
public sealed partial class GenerateurFacade
{
    /// <summary>Hauteur de la file rapportée au-dessus du dernier plancher desservi (garde-corps).</summary>
    private const double HauteurGardeCorps = 1.0;

    /// <summary>Éléments en travers déjà posés sur une file rapportée (une seule pièce si deux mailles voisines la partagent).</summary>
    private readonly HashSet<string> _travers = new HashSet<string>();

    /// <summary>
    /// Hauteur de la sous-maille rapportée (0 = pas de sous-maille). Elle monte au moins jusqu'à la hauteur max de sa maille
    /// (colonne « Ht max », Laurent 25/11/2026) ; accès et escalier vont aussi jusqu'au dernier plancher de la face,
    /// appro et recette jusqu'au plus haut niveau desservi (et n'existent que s'ils desservent un niveau).
    /// </summary>
    private double HauteurSousMaille(MailleDefinition m, IEnumerable<Niveau> niveaux)
    {
        double hm = m.HauteurMax(_h);
        switch (m.Type)
        {
            case TypeMaille.AccesRapporte:
                return Math.Max(_h, hm);

            case TypeMaille.Escalier:
            case TypeMaille.DemiEscalier070:
            case TypeMaille.DemiEscalier100:
                return EscalierPossible(m) ? Math.Max(_h, hm) : 0;

            case TypeMaille.Appro:
                return AvecPlancher(niveaux.Where(n => n.Appro), hm);

            case TypeMaille.Recette:
                return AvecPlancher(niveaux.Where(n => n.Recette), hm);

            default:
                return 0;
        }
    }

    private static double AvecPlancher(IEnumerable<Niveau> desservis, double hauteurMaille)
    {
        double h = desservis.Where(n => !n.EstAccesSeul && n.Hauteur > Cotes.Tolerance).Select(n => n.Hauteur).DefaultIfEmpty(0).Max();
        return h > Cotes.Tolerance ? Math.Max(h, hauteurMaille) : 0;
    }

    /// <summary>Le niveau est desservi par la sous-maille (appro ou recette) : la maille principale s'ouvre vers l'extérieur.</summary>
    private bool SousMailleDesservie(MailleDefinition m, Niveau n) =>
        n.Hauteur > Cotes.Tolerance && !n.EstAccesSeul && n.Hauteur <= HauteurSousMaille(m, _projet.Niveaux) + Cotes.Tolerance &&
        ((m.Type == TypeMaille.Appro && n.Appro) || (m.Type == TypeMaille.Recette && n.Recette));

    /// <summary>Même sous-maille sur la maille voisine (même type, même profondeur) : file et côté partagés.</summary>
    private bool MemeSousMaille(int indexVoisine, MailleDefinition m) =>
        indexVoisine >= 0 && indexVoisine < _projet.Mailles.Count &&
        _projet.Mailles[indexVoisine].Type == m.Type &&
        Cotes.Egal(_projet.Mailles[indexVoisine].LargeurRapportee, m.LargeurRapportee);

    private void PlacerSousMailles(List<Niveau> niveaux)
    {
        // Poteaux des files rapportées : une file par (abscisse, profondeur), à la hauteur la plus grande des mailles voisines.
        var files = new Dictionary<(int File, double Profondeur), double>();
        for (int i = 0; i < _projet.Mailles.Count; i++)
        {
            var m = _projet.Mailles[i];
            double w = m.LargeurRapportee;
            if (w <= Cotes.Tolerance)
            {
                continue;
            }

            double hs = HauteurSousMaille(m, niveaux);
            if (hs <= Cotes.Tolerance)
            {
                _alertes.Add(m.EstEscalier
                    ? FormattableString.Invariant($"Maille {i + 1} : escalier impossible sur une maille de {m.Longueur:0.00} m (volées de 2,50 ou 3,00 m), ignoré.")
                    : m.Type == TypeMaille.Recette
                        ? $"Maille {i + 1} : la recette ne sera pas représentée, car elle n'est indiquée sur aucun niveau (colonne « Recette » de la grille des niveaux)."
                        : $"Maille {i + 1} : le module d'appro ne sera pas représenté, car il n'est indiqué sur aucun niveau (colonne « Appro » de la grille des niveaux).");
                continue;
            }

            foreach (int f in new[] { i, i + 1 })
            {
                var cle = (f, Cotes.Arrondi(w));
                files[cle] = files.TryGetValue(cle, out double h) ? Math.Max(h, hs) : hs;
            }

            // Niveaux de la maille, y compris ceux qui dépassent le dernier plancher de la face (Ht max).
            var niveauxMaille = niveaux.Concat(NiveauxPignon(m.HauteurMax(_h), TypePlancher.Complet)).ToList();
            if (m.EstEscalier)
            {
                PlacerEscalier(i, m, w, niveauxMaille);
            }
            else
            {
                PlacerSousMaille(i, m, w, hs, niveauxMaille);
            }

            GardeCorpsSousMailleAuDessus(i, m, hs, niveaux);
        }

        foreach (var kv in files)
        {
            double x = _x[kv.Key.File];
            double y = Cotes.Arrondi(-_l - kv.Key.Profondeur);
            foreach (var t in ReglePoteaux.Empiler(_p, false, true, Cotes.Arrondi(kv.Value + HauteurGardeCorps)))
            {
                Ajouter(t.Reference, t.Famille, new Point3(x, y, t.Z), Axe.Z, Face.Exterieure);
            }
        }
    }

    /// <summary>
    /// Sous-maille rapportée plus haute que sa maille (Laurent, 24/11/2026) : au-dessus du dernier plancher de la maille,
    /// le côté façade de la sous-maille (file extérieure de la maille) reçoit le garde-corps intérieur des paramètres.
    /// </summary>
    private void GardeCorpsSousMailleAuDessus(int index, MailleDefinition m, double hs, List<Niveau> niveaux)
    {
        double hm = m.HauteurMax(_h);
        if (hs <= hm + Cotes.Tolerance)
        {
            return;
        }

        foreach (var n in niveaux.Where(n => n.Hauteur > hm + Cotes.Tolerance && n.Hauteur <= hs + Cotes.Tolerance))
        {
            bool plancher = m.Type == TypeMaille.Appro || m.Type == TypeMaille.Recette ? SousMailleDesservie(m, n) : !(n.EstAccesSeul && m.Type == TypeMaille.AccesRapporte);
            if (plancher)
            {
                PlacerGardeCorps(SecuriteInterieure(index), PlintheInterieure(index), _x[index], -_l, n.Hauteur, m.Longueur, Face.Interieure, index + 1);
            }
        }
    }

    private void PlacerSousMaille(int index, MailleDefinition m, double w, double hs, List<Niveau> niveaux)
    {
        int num = index + 1;
        double x0 = _x[index];
        double x1 = _x[index + 1];
        double lm = m.Longueur;
        double y1 = -_l;
        double y2 = Cotes.Arrondi(-_l - w);
        string lisse = References.LisseOuTraverse(lm);
        bool gaucheLibre = !MemeSousMaille(index - 1, m);
        bool droiteLibre = !MemeSousMaille(index + 1, m);
        bool inverseTrappe = false;

        var cotes = niveaux.Select(n => n.Hauteur).Where(z => z <= hs + Cotes.Tolerance).ToList();
        if (!cotes.Any(z => Cotes.Egal(z, 0)))
        {
            cotes.Insert(0, 0);
        }

        // Traverses en travers aux deux files et lisse le long de la file rapportée, à chaque niveau.
        foreach (double z in cotes)
        {
            var n = niveaux.FirstOrDefault(k => Cotes.Egal(k.Hauteur, z));
            bool desservi = n != null && (m.Type == TypeMaille.AccesRapporte ? z > Cotes.Tolerance : SousMailleDesservie(m, n));
            var type = !desservi ? TypeTraverse.Lisse : m.Type == TypeMaille.AccesRapporte ? _p.Traverses : TypeTraverse.Poutrelle;
            Travers(References.LisseOuTraverse(w, type), Famille.Traverse, x0, y1, z, num);
            Travers(References.LisseOuTraverse(w, type), Famille.Traverse, x1, y1, z, num);
            Ajouter(lisse, Famille.Lisse, new Point3(x0, y2, z), Axe.X, Face.Exterieure, num);

            // Appro : lisse le long de la file extérieure à chaque niveau ; recette : aux niveaux desservis (v2).
            if (z > Cotes.Tolerance && (m.Type == TypeMaille.Appro || (m.Type == TypeMaille.Recette && desservi)))
            {
                Ajouter(lisse, Famille.Lisse, new Point3(x0, y1, z), Axe.X, Face.Exterieure, num);
            }

            // Diagonales en travers aux deux bouts (appro, recette) sous le dernier plancher desservi,
            // et le long de la file rapportée aux niveaux non desservis (v2 AjouteDiagos forcées).
            if (m.Type != TypeMaille.AccesRapporte && z < hs - Cotes.Tolerance)
            {
                // En travers : côté extérieur de la sous-maille (Laurent, 24/11/2026), sauf sur une file partagée.
                string diaW = References.DiagonaleVerticale(w);
                Travers(diaW, Famille.DiagonaleVerticale, gaucheLibre ? x0 - DemiTube : x0 + DemiTube, y1, z, num);
                if (droiteLibre)
                {
                    Travers(diaW, Famille.DiagonaleVerticale, x1 + DemiTube, y1, z, num);
                }
                if (!desservi && !m.Diagonale)
                {
                    Ajouter(References.DiagonaleVerticale(lm), Famille.DiagonaleVerticale, new Point3(x0, y2 - DemiTube, z), Axe.X, Face.Exterieure, num);
                }
            }

            if (n == null)
            {
                continue;
            }

            switch (m.Type)
            {
                case TypeMaille.AccesRapporte:
                    PlacerAccesRapporte(m, num, x0, n, gaucheLibre, droiteLibre, ref inverseTrappe);
                    break;

                case TypeMaille.Appro when desservi:
                    PlacerPlateaux(x0, y1, z, w, lm, num);
                    CotesSousMaille(x0, x1, z, w, gaucheLibre, droiteLibre, num);
                    PlacerGardeCorpsAppro(x0, x1, lm, z, num);
                    break;

                case TypeMaille.Recette when desservi:
                    PlacerPlateaux(x0, y1, z, w, lm, num);
                    CotesSousMaille(x0, x1, z, w, gaucheLibre, droiteLibre, num);
                    PlacerGardeCorps(TypeSecurite.Lisses, _p.PlintheExterieure, x0, y2, z, lm, Face.Exterieure, num);
                    break;
            }
        }
    }

    /// <summary>
    /// Colonne d'accès rapportée de 0,70 m (v2 AjoutePlateauxTravail, branche MailleAccesRap) :
    /// garde-corps extérieur sur la file rapportée, plateaux sous 2 m, trappe + échelle au-dessus, abouts de 0,70 m.
    /// </summary>
    private void PlacerAccesRapporte(MailleDefinition m, int num, double x0, Niveau n, bool gaucheLibre, bool droiteLibre, ref bool inverse)
    {
        double z = n.Hauteur;
        double lm = m.Longueur;
        double w = m.LargeurRapportee;
        double y1 = -_l;
        double y2 = Cotes.Arrondi(-_l - w);

        if (z >= 0.5 - Cotes.Tolerance)
        {
            PlacerGardeCorps(_p.SecuriteExterieure == TypeSecurite.Sans ? TypeSecurite.Lisses : _p.SecuriteExterieure, _p.PlintheExterieure, x0, y2, z, lm, Face.Exterieure, num);

            // Niveau « accès seul » : la maille principale n'a pas de plancher, on protège côté maille.
            if (n.EstAccesSeul)
            {
                PlacerGardeCorps(TypeSecurite.Lisses, TypePlinthe.Sans, x0, y1, z, lm, Face.Exterieure, num);
            }

            if (gaucheLibre)
            {
                PlacerAbout(x0, z, w, gauche: true, console: false, num, yOrigine: y1);
            }

            if (droiteLibre)
            {
                PlacerAbout(x0 + lm, z, w, gauche: false, console: false, num, yOrigine: y1);
            }
        }

        if (z <= Cotes.Tolerance)
        {
            return;
        }

        PlacerTremie(x0, lm, y1, w, z, num, ref inverse, secuCoteBord: false);
    }

    /// <summary>Côtés en travers d'une sous-maille desservie (lisses à 0,50 et 1,00 m + plinthe), sauf côté partagé.</summary>
    private void CotesSousMaille(double x0, double x1, double z, double w, bool gaucheLibre, bool droiteLibre, int num)
    {
        if (gaucheLibre)
        {
            CoteSousMaille(x0, z, w, gauche: true, num);
        }

        if (droiteLibre)
        {
            CoteSousMaille(x1, z, w, gauche: false, num);
        }
    }

    private void CoteSousMaille(double x, double z, double w, bool gauche, int num)
    {
        string lisse = References.LisseOuTraverse(w);
        var o = new Point3(x, -_l, z);
        Ajouter(lisse, Famille.Traverse, o.Decale(dz: 0.5), Axe.Y, Face.Aucune, num, gauche);
        Ajouter(lisse, Famille.Traverse, o.Decale(dz: 1.0), Axe.Y, Face.Aucune, num, gauche);
        var plinthe = References.Plinthe(_p.PlintheAbouts == TypePlinthe.Sans ? _p.PlintheExterieure : _p.PlintheAbouts, w);
        if (plinthe != null)
        {
            Ajouter(plinthe, Famille.Plinthe, o, Axe.Y, Face.Aucune, num, gauche);
        }
    }

    /// <summary>
    /// Garde-corps d'approvisionnement (NT-GC_Approvisionnement, v2 AjouteSousMailleModulappro) : un support HMGCAPPROSUP
    /// sur les lisses de chaque côté, à mi-profondeur, un cadre basculeur HMGCAPPRO-C sur chaque support,
    /// le garde-corps HMGCAPPROGC (longueur de maille - 0,50 m) et deux lisses entre les cadres.
    /// </summary>
    private void PlacerGardeCorpsAppro(double x0, double x1, double lm, double z, int num)
    {
        double yMilieu = Cotes.Arrondi(-_l - 1.0);
        double lgc = Cotes.Arrondi(lm - 0.5);
        if (lgc < 1.5 - Cotes.Tolerance)
        {
            _alertes.Add($"Maille {num} : module d'appro sur une maille de {lm:0.00} m (garde-corps d'appro à partir de 2,00 m).");
            return;
        }

        // Support relevé sur le serveur : montant de 0,63 m sous la lisse, bras de 0,29 m vers +X → posé en axe X
        // (symétrisé à droite pour que le bras parte vers l'intérieur de la maille).
        Ajouter(References.SupportAppro, Famille.GardeCorps, new Point3(x0, yMilieu, z + 1.0), Axe.X, Face.Exterieure, num);
        Ajouter(References.SupportAppro, Famille.GardeCorps, new Point3(x1, yMilieu, z + 1.0), Axe.X, Face.Exterieure, num, inverse: true);
        Ajouter(References.CadreAppro, Famille.GardeCorps, new Point3(x0 + 0.25, yMilieu, z + 0.9), Axe.Y, Face.Exterieure, num);
        Ajouter(References.CadreAppro, Famille.GardeCorps, new Point3(x1 - 0.25, yMilieu, z + 0.9), Axe.Y, Face.Exterieure, num, inverse: true);
        Ajouter(References.GardeCorpsAppro(lgc), Famille.GardeCorps, new Point3(x0 + 0.25, Cotes.Arrondi(-_l - 2.0), z + 0.5), Axe.X, Face.Exterieure, num);

        // Lisses hautes des cadres (position v2 : lisses tournées de 90° autour de l'axe des pivots).
        string lisse = References.LisseOuTraverse(lgc);
        Ajouter(lisse, Famille.Lisse, new Point3(x0 + 0.25, Cotes.Arrondi(-_l - 0.6), z + 1.9), Axe.X, Face.Exterieure, num);
        Ajouter(lisse, Famille.Lisse, new Point3(x0 + 0.25, Cotes.Arrondi(-_l - 1.1), z + 1.9), Axe.X, Face.Exterieure, num);
    }

    /// <summary>Pièce en travers (axe Y) posée une seule fois à un endroit donné.</summary>
    /// <param name="versLaFacade">Élément posé à partir de <paramref name="y"/> vers la façade (+Y), tourné d'un demi-tour.</param>
    private void Travers(string reference, Famille famille, double x, double y, double z, int num, bool versLaFacade = false)
    {
        string cle = FormattableString.Invariant($"{reference}|{Cotes.Arrondi(x)}|{Cotes.Arrondi(y)}|{Cotes.Arrondi(z)}");
        if (_travers.Add(cle))
        {
            Ajouter(reference, famille, new Point3(Cotes.Arrondi(x), y, z), Axe.Y, Face.Exterieure, num, rotation: versLaFacade ? 180 : 0);
        }
    }

    // ------------------------------------------------------------------------------------------
    // Stabilisations (colonne « Stab. » : v2 AjouteSousMailleStabilisation + files de clJonction)
    // ------------------------------------------------------------------------------------------

    private void PlacerStabilisations(List<Niveau> niveaux)
    {
        double hs = Cotes.Arrondi(_h - 2.0);
        var files = new HashSet<(int File, double Largeur)>();
        for (int i = 0; i < _projet.Mailles.Count; i++)
        {
            var m = _projet.Mailles[i];
            double s = m.Stabilisation;
            if (s <= Cotes.Tolerance)
            {
                continue;
            }

            if (hs <= Cotes.Tolerance)
            {
                _alertes.Add($"Maille {i + 1} : stabilisation inutile sous 2 m de dernier plancher, ignorée.");
                continue;
            }

            int num = i + 1;
            double x0 = _x[i];
            double x1 = _x[i + 1];
            double lm = m.Longueur;
            double y1 = -_l;
            double y = Cotes.Arrondi(-_l - s);
            bool memeADroite = i + 1 < _projet.Mailles.Count && Cotes.Egal(_projet.Mailles[i + 1].Stabilisation, s);
            bool memeAGauche = i > 0 && Cotes.Egal(_projet.Mailles[i - 1].Stabilisation, s);
            files.Add((i, Cotes.Arrondi(s)));
            files.Add((i + 1, Cotes.Arrondi(s)));

            var cotes = niveaux.Select(n => n.Hauteur).Where(z => z <= hs + Cotes.Tolerance).ToList();
            if (!cotes.Any(z => Cotes.Egal(z, 0)))
            {
                cotes.Insert(0, 0);
            }

            if (m.Lest)
            {
                PlacerLest(x0, x1, lm, s, num);
            }

            foreach (double z in cotes)
            {
                Ajouter(References.LisseOuTraverse(lm), Famille.Lisse, new Point3(x0, y, z), Axe.X, Face.Exterieure, num);
                Travers(References.LisseOuTraverse(s), Famille.Traverse, x0, y1, z, num);
                Travers(References.LisseOuTraverse(s), Famille.Traverse, x1, y1, z, num);
                if (z < hs - Cotes.Tolerance)
                {
                    Ajouter(References.DiagonaleVerticale(lm), Famille.DiagonaleVerticale, new Point3(x0, y - DemiTube, z), Axe.X, Face.Exterieure, num);
                }

                // Diagonales en travers : du pied côté stabilisation vers le haut côté structure (Laurent, 24/11/2026),
                // la dernière relie le haut de la stabilisation au dernier plancher de la structure.
                // Pas de diagonale en travers dans la hauteur du platelage de lest : elle traverserait les plateaux
                // (Laurent, 27/11/2026, remarque 55).
                if (m.Lest && z < HauteurLest - Cotes.Tolerance && z + 2.0 > HauteurLest + Cotes.Tolerance)
                {
                    continue;
                }

                Travers(References.DiagonaleVerticale(s), Famille.DiagonaleVerticale, memeAGauche ? x0 + DemiTube : x0 - DemiTube, y, z, num, versLaFacade: true);
                if (!memeADroite)
                {
                    Travers(References.DiagonaleVerticale(s), Famille.DiagonaleVerticale, x1 + DemiTube, y, z, num, versLaFacade: true);
                }
            }
        }

        foreach (var (f, s) in files)
        {
            foreach (var t in ReglePoteaux.Empiler(_p, false, false, hs))
            {
                Ajouter(t.Reference, t.Famille, new Point3(_x[f], Cotes.Arrondi(-_l - s), t.Z), Axe.Z, Face.Exterieure);
            }
        }
    }

    /// <summary>Hauteur du platelage de lest dans une stabilisation.</summary>
    internal const double HauteurLest = 0.5;

    /// <summary>
    /// Platelage de lest (colonne « Lest », Laurent 25/11/2026) : à 0,50 m, entre la file extérieure et la file de
    /// stabilisation, plateaux de la longueur de la maille posés sur des lisses renforcées en travers aux deux files
    /// (règle des lisses porteuses), lisse le long de la file de stabilisation pour tenir le lest.
    /// </summary>
    private void PlacerLest(double x0, double x1, double lm, double s, int num)
    {
        double z = HauteurLest;
        double y1 = -_l;
        string porteuse = LissePorteuse(s);
        Travers(porteuse, Famille.Traverse, x0, y1, z, num);
        Travers(porteuse, Famille.Traverse, x1, y1, z, num);
        Ajouter(References.LisseOuTraverse(lm), Famille.Lisse, new Point3(x0, Cotes.Arrondi(-_l - s), z), Axe.X, Face.Exterieure, num);
        PlacerPlateaux(x0, y1, z, s, lm, num);
    }
}
