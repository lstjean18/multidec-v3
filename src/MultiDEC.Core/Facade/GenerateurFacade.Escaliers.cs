using System;
using System.Collections.Generic;
using System.Linq;
using MultiDEC.Core.Catalogue;
using MultiDEC.Core.Facade.Regles;
using MultiDEC.Core.Geometrie;

namespace MultiDEC.Core.Facade;

/// <summary>
/// Escaliers rapportés côté extérieur (port de clModuleMaille v2 : AjouteSousMailleEsca, AjouteSousMailleDemiEsca).
/// - Escalier 1,50 m : tour de deux demi-largeurs ; à chaque niveau une volée HMESCALU de 2 m de montée,
///   alternativement dans la moitié côté façade et dans la moitié extérieure, en sens opposés (la dernière volée
///   arrive côté façade). L'ouverture vers la maille de travail est au bout où la volée arrive.
/// - Demi-escalier 0,70 / 1,00 m : une volée par niveau, toujours dans le même sens ; on repart du plancher
///   de la maille de travail. Garde-corps central sur piquages aux niveaux intermédiaires, ouverture aux deux bouts.
/// Convention relevée sur le serveur (BOITE) : une volée posée en axe X à (x, y, z) descend de z à z - 2,
///   sur la longueur de la maille (x → x + longueur), largeur vers +Y ; non symétrisée, elle arrive en haut côté x.
/// </summary>
public sealed partial class GenerateurFacade
{
    /// <summary>Escalier réalisable : volées de 2,50 ou 3,00 m dans la bibliothèque.</summary>
    private bool EscalierPossible(MailleDefinition m) =>
        m.EstEscalier && (Cotes.Egal(m.Longueur, 2.5) || Cotes.Egal(m.Longueur, 3.0)) && _h >= 1.0 - Cotes.Tolerance;

    private void PlacerEscalier(int index, MailleDefinition m, double w, List<Niveau> niveaux)
    {
        int num = index + 1;
        double x0 = _x[index];
        double x1 = _x[index + 1];
        double lm = m.Longueur;
        double y1 = -_l;
        double y2 = Cotes.Arrondi(-_l - w);
        bool demi = m.Type != TypeMaille.Escalier;
        bool large = m.Type == TypeMaille.DemiEscalier100;
        bool gaucheLibre = !MemeSousMaille(index - 1, m);
        bool droiteLibre = !MemeSousMaille(index + 1, m);

        // v2 : DecGC = largeur de volée (0,65 ou 0,88), volée à 5 cm de la file extérieure.
        double largeurVolee = large ? 0.88 : 0.65;
        double yVolee = Cotes.Arrondi(-_l - largeurVolee - 0.05);
        string lisse = References.LisseOuTraverse(lm);
        string? plintheLong = References.Plinthe(_p.PlintheExterieure, lm);
        string? plintheCote = References.Plinthe(_p.PlintheAbouts == TypePlinthe.Sans ? _p.PlintheExterieure : _p.PlintheAbouts, w);

        var cotes = new List<double> { 0 };
        cotes.AddRange(niveaux.Select(n => n.Hauteur)
            .Where(z => z > Cotes.Tolerance && z <= _h + Cotes.Tolerance)
            .Select(Cotes.Arrondi).Distinct().OrderBy(z => z));
        if (cotes.Count < 2)
        {
            return;
        }

        // Premier plancher à 1,00 m (Laurent, 24/11/2026) : la volée de 1 m sort de la tour, sur la maille voisine
        // du côté où part la volée suivante (maille suivante pour un demi-escalier), dans le prolongement.
        bool miroirSuivante = !demi && cotes.Count > 2 && (cotes.Count - 3) % 2 == 1;
        int? voisineDehors = Cotes.Egal(cotes[1], 1.0) ? VoisinePourVoleeDehors(index, miroirSuivante ? index - 1 : index + 1) : null;

        // Premier plancher sous 2 m, volée restée dans la tour (escalier) : on entre au sol par le bout du pied de la
        // première volée, sans traverse sous 2 m de ce côté (Laurent, 24/11/2026). Le demi-escalier y pose le pied de sa
        // volée suivante : sa traverse reste.
        bool entreeBasse = !demi && voisineDehors == null && cotes[1] < 2.0 - Cotes.Tolerance;
        double xEntree = PiedPremiereVoleeAGauche(demi, cotes.Count) ? x0 : x1;
        bool Entree(double x, double zt) => entreeBasse && Cotes.Egal(x, xEntree) && zt > Cotes.Tolerance && zt < 2.0 - Cotes.Tolerance;

        bool gardeCorpsManquant = false;
        for (int k = 0; k < cotes.Count; k++)
        {
            double z = cotes[k];
            bool haut = k == cotes.Count - 1;

            // Escalier : sens alterné, la volée du dernier niveau arrive côté façade non symétrisée.
            bool miroir = !demi && (cotes.Count - 1 - k) % 2 == 1;
            double xPalier = miroir ? x1 : x0;
            double xAutre = miroir ? x0 : x1;
            bool deuxBouts = demi || haut;
            if (k == 1 && voisineDehors != null)
            {
                // On entre par le bout où arrive la volée extérieure : on ferme l'autre bout seulement.
                xPalier = miroirSuivante ? x1 : x0;
                xAutre = miroirSuivante ? x0 : x1;
                deuxBouts = haut;
                miroir = miroirSuivante;
            }

            // Plancher : traverses renforcées aux deux bouts (les volées s'y accrochent), lisse de la file rapportée.
            string traverseVolee = References.LisseOuTraverse(w, TypeTraverse.Renforce);
            foreach (double xt in new[] { x0, x1 }.Where(xt => !Entree(xt, z)))
            {
                Travers(traverseVolee, Famille.Traverse, xt, y1, z, num);
            }

            Ajouter(lisse, Famille.Lisse, new Point3(x0, y2, z), Axe.X, Face.Exterieure, num);
            if (k == 1 && entreeBasse)
            {
                Travers(References.LisseOuTraverse(w), Famille.Traverse, xEntree, y1, 2.0, num);
            }

            if (z > Cotes.Tolerance)
            {
                if (plintheLong != null)
                {
                    Ajouter(plintheLong, Famille.Plinthe, new Point3(x0, y2, z), Axe.X, Face.Exterieure, num);
                }

                if (plintheCote != null)
                {
                    Ajouter(plintheCote, Famille.Plinthe, new Point3(xPalier, y1, z), Axe.Y, Face.Aucune, num, xPalier < x1 - Cotes.Tolerance);
                    if (deuxBouts && !Entree(xAutre, z))
                    {
                        Ajouter(plintheCote, Famille.Plinthe, new Point3(xAutre, y1, z), Axe.Y, Face.Aucune, num, xAutre < x1 - Cotes.Tolerance);
                    }
                }
            }

            // Protection tous les 0,50 m jusqu'au niveau suivant (1,00 m au-dessus du dernier) :
            // le long de la file rapportée, et en travers au bout du palier (aux deux bouts pour un demi-escalier).
            double hp = haut ? 1.0 : Cotes.Arrondi(cotes[k + 1] - z - 0.5);
            for (double h = 0.5; h <= hp + Cotes.Tolerance; h = Cotes.Arrondi(h + 0.5))
            {
                double zh = Cotes.Arrondi(z + h);
                Ajouter(lisse, Famille.Lisse, new Point3(x0, y2, zh), Axe.X, Face.Exterieure, num);
                if (z > Cotes.Tolerance)
                {
                    string traverse = References.LisseOuTraverse(w);
                    Travers(traverse, Famille.Traverse, xPalier, y1, zh, num);
                    if (deuxBouts && !Entree(xAutre, zh))
                    {
                        Travers(traverse, Famille.Traverse, xAutre, y1, zh, num);
                    }
                }
            }

            // Diagonales de la tour sur 2 m : face extérieure et en travers aux deux bouts, côté extérieur de la tour
            // (Laurent, 24/11/2026). Au sol, pas de diagonale devant l'entrée (pied de la première volée).
            if (!haut && cotes[k + 1] - z >= 2.0 - Cotes.Tolerance)
            {
                Ajouter(References.DiagonaleVerticale(lm), Famille.DiagonaleVerticale, new Point3(x0, Cotes.Arrondi(y2 - DemiTube), z), Axe.X, Face.Exterieure, num);
                // Volée de 1 m sortie de la tour : l'entrée est au plancher de 1 m, du côté de la maille voisine.
                // Entrée au sol sous un premier plancher bas : pas de diagonale non plus dans le passage, au-dessus.
                bool entreeAGauche = voisineDehors != null ? k == 1 && miroirSuivante : (k == 0 || (k == 1 && entreeBasse)) && PiedPremiereVoleeAGauche(demi, cotes.Count);
                bool entreeADroite = voisineDehors != null ? k == 1 && !miroirSuivante : (k == 0 || (k == 1 && entreeBasse)) && !PiedPremiereVoleeAGauche(demi, cotes.Count);
                if (!entreeAGauche)
                {
                    Travers(References.DiagonaleVerticale(w), Famille.DiagonaleVerticale, gaucheLibre ? x0 - DemiTube : x0 + DemiTube, y1, z, num);
                }

                if (droiteLibre && !entreeADroite)
                {
                    Travers(References.DiagonaleVerticale(w), Famille.DiagonaleVerticale, x1 + DemiTube, y1, z, num);
                }
            }

            if (k == 0)
            {
                continue;
            }

            // Volée arrivant à ce niveau.
            double montee = Cotes.Arrondi(z - cotes[k - 1]);
            if (!Cotes.Egal(montee, 2.0) && !Cotes.Egal(montee, 1.0))
            {
                _alertes.Add(FormattableString.Invariant($"Maille {num} : montée de {montee:0.00} m sous le niveau {z:0.00} m, pas de volée d'escalier (1,00 ou 2,00 m)."));
            }
            else if (k == 1 && voisineDehors is int v)
            {
                PlacerVoleeDehors(num, v, miroirSuivante ? x0 : x1, w, yVolee, largeurVolee, large, demi, miroirSuivante, haut, ref gardeCorpsManquant);
            }
            else
            {
                PlacerVolee(num, x0, x1, lm, z, montee, yVolee, largeurVolee, large, demi, miroir, premiere: k == 1, derniere: haut, ref gardeCorpsManquant);
                if (k == 1 && Cotes.Egal(montee, 1.0))
                {
                    PlacerPiedVolee1mDansTour(num, x0, x1, w, miroir);
                }
            }

            var n = niveaux.FirstOrDefault(v => Cotes.Egal(v.Hauteur, z));
            if (n != null)
            {
                PlacerOuvertureEscalier(num, x0, x1, lm, z, n, demi && !haut, miroir);
            }
        }

        if (gardeCorpsManquant)
        {
            _alertes.Add(FormattableString.Invariant($"Maille {num} : garde-corps de volée de {lm:0.00} m absent de la bibliothèque, volées posées sans garde-corps."));
        }
    }

    /// <summary>
    /// Volée de 1 m restée dans la tour : son pied repose sur une traverse intermédiaire HMTIP (largeur de la tour),
    /// et le reste de la tour jusqu'au bout d'entrée est couvert de plateaux acier posés en travers, de la largeur
    /// de la tour, sur les lisses de pied (Laurent, 24/11/2026 : HMTIP150 et plateaux de 1,50 m).
    /// </summary>
    private void PlacerPiedVolee1mDansTour(int num, double x0, double x1, double w, bool miroir)
    {
        double y1 = -_l;
        double xHaut = miroir ? x1 : x0;
        double xEntree = miroir ? x0 : x1;
        double xPied = Cotes.Arrondi(xHaut + (miroir ? -1 : 1) * LongueurVolee1m);
        Ajouter(References.TraverseIntermediaire(w), Famille.Traverse, new Point3(xPied, y1, 0), Axe.Y, Face.Exterieure, num);

        double reste = Cotes.Arrondi(Math.Abs(xEntree - xPied));
        if (!new[] { 0.75, 1.0, 1.05, 1.5, 1.75 }.Any(l => Cotes.Egal(l, w)))
        {
            _alertes.Add(FormattableString.Invariant(
                $"Maille {num} : pas de plateau acier de {w:0.00} m pour couvrir le pied de la volée de 1 m dans la tour, à compléter."));
            return;
        }

        // Plateau en travers : il part de y1 vers l'extérieur, sa largeur vers les X décroissants.
        foreach (var (_, rang) in RepartitionPlateaux(reste, GammePlateaux.Standard))
        {
            double x = miroir
                ? Cotes.Arrondi(xEntree + RetraitPlateau + LargeurPlateau * (rang + 1))
                : Cotes.Arrondi(xEntree - RetraitPlateau - LargeurPlateau * rang);
            Ajouter(References.PlateauAcierComplement(w), Famille.Plateau, new Point3(x, y1, 0), Axe.Y, Face.Aucune, num);
        }
    }

    /// <summary>Longueur de la volée de 1,00 m de montée (HMESCALU175100).</summary>
    private const double LongueurVolee1m = 1.75;

    /// <summary>
    /// Maille voisine qui peut recevoir la volée de 1 m sortie de la tour : maille de travail sans sous-maille ni stabilisation,
    /// assez longue pour la volée. Sinon alerte et la volée reste dans la tour.
    /// </summary>
    private int? VoisinePourVoleeDehors(int index, int voisine)
    {
        if (voisine >= 0 && voisine < _projet.Mailles.Count)
        {
            var m = _projet.Mailles[voisine];
            if (m.EstTravail && m.LargeurRapportee <= Cotes.Tolerance && m.Stabilisation <= Cotes.Tolerance
                && m.Longueur >= 2.0 - Cotes.Tolerance)
            {
                return voisine;
            }
        }

        _alertes.Add(FormattableString.Invariant(
            $"Maille {index + 1} : premier plancher à 1,00 m, la volée de 1 m devrait sortir de la tour sur la maille {voisine + 1}, qui ne s'y prête pas (absente, rapportée ou trop courte) : volée posée dans la tour."));
        return null;
    }

    /// <summary>
    /// Volée de 1 m posée hors de la tour, sur la maille voisine (côté extérieur, même déport que la tour) : elle arrive
    /// au plancher de 1,00 m au bout <paramref name="xTour"/> de la tour, son pied repose au sol sur une traverse
    /// intermédiaire HMTIP, et le reste de la maille est couvert de plateaux acier (Laurent, 24/11/2026). Une file de
    /// poteaux courts ferme la sous-maille au bout de la maille voisine.
    /// </summary>
    private void PlacerVoleeDehors(int num, int voisine, double xTour, double w, double yVolee, double largeurVolee,
        bool large, bool demi, bool versLaGauche, bool derniere, ref bool gardeCorpsManquant)
    {
        double y1 = -_l;
        double y2 = Cotes.Arrondi(-_l - w);
        double lv = _projet.Mailles[voisine].Longueur;
        double xBout = versLaGauche ? _x[voisine] : _x[voisine + 1];
        double sens = versLaGauche ? -1 : 1;
        double xPied = Cotes.Arrondi(xTour + sens * LongueurVolee1m);

        // Volée : non symétrisée elle arrive en haut côté x de son origine et descend vers les X croissants.
        PlacerVolee(num, xTour, xTour, LongueurVolee1m, 1.0, 1.0, yVolee, largeurVolee, large, demi, versLaGauche, premiere: true, derniere: derniere, ref gardeCorpsManquant);

        // Pied : traverse intermédiaire au sol, plateaux acier jusqu'au bout de la maille, lisse et traverse de pied.
        Ajouter(References.TraverseIntermediaire(w), Famille.Traverse, new Point3(xPied, y1, 0), Axe.Y, Face.Exterieure, num);
        Travers(References.LisseOuTraverse(w), Famille.Traverse, xBout, y1, 0, num);
        // Côté extérieur : lisses au sol, au palier (1 m), et garde-corps 1 m au-dessus du palier (Laurent, 24/11/2026).
        foreach (double zl in new[] { 0.0, 1.0, 1.5, 2.0 })
        {
            Ajouter(References.LisseOuTraverse(lv), Famille.Lisse, new Point3(_x[voisine], y2, zl), Axe.X, Face.Exterieure, num);
        }

        foreach (var t in ReglePoteaux.Empiler(_p, false, false, 2.0))
        {
            Ajouter(t.Reference, t.Famille, new Point3(xBout, y2, t.Z), Axe.Z, Face.Exterieure, num);
        }

        double reste = Cotes.Arrondi(lv - LongueurVolee1m);
        if (!new[] { 0.75, 1.0, 1.05, 1.5, 1.75 }.Any(l => Cotes.Egal(l, reste)))
        {
            _alertes.Add(FormattableString.Invariant(
                $"Maille {num} : pas de plateau acier de {reste:0.00} m au pied de la volée de 1 m (maille voisine de {lv:0.00} m), à compléter."));
            return;
        }

        double xReste = versLaGauche ? _x[voisine] : xPied;
        foreach (var (_, rang) in RepartitionPlateaux(w, GammePlateaux.Standard))
        {
            double y = Cotes.Arrondi(y1 - RetraitPlateau - LargeurPlateau * rang);
            Ajouter(References.PlateauAcierComplement(reste), Famille.Plateau, new Point3(xReste, y, 0), Axe.X, Face.Aucune, num);
        }

    }

    /// <summary>
    /// Pied de la première volée côté x0 ? Une volée non symétrisée monte vers x0 (pied côté x1) ;
    /// pour l'escalier, la première est symétrisée quand le nombre de niveaux au-dessus du sol est pair.
    /// </summary>
    private static bool PiedPremiereVoleeAGauche(bool demi, int nombreCotes) => !demi && (nombreCotes - 2) % 2 == 1;

    /// <summary>
    /// Volée et ses garde-corps (HMGCESCALU, posé 0,50 m après le haut de la volée), extrémités HMGCESCALUEXTR
    /// au pied de la première volée et en haut de la dernière.
    /// </summary>
    private void PlacerVolee(int num, double x0, double x1, double lm, double z, double montee, double yVolee, double largeurVolee,
        bool large, bool demi, bool miroir, bool premiere, bool derniere, ref bool gardeCorpsManquant)
    {
        // Escalier : la volée symétrisée passe dans la moitié extérieure (v2 : décalage de largeur + 5 cm).
        double yv = miroir ? Cotes.Arrondi(yVolee - largeurVolee - 0.05) : yVolee;
        Ajouter(References.Volee(lm, montee, large), Famille.Escalier, new Point3(miroir ? x1 : x0, yv, z), Axe.X, Face.Exterieure, num, miroir);

        string? gc = References.GardeCorpsVolee(lm, montee);
        if (gc == null)
        {
            gardeCorpsManquant = true;
            return;
        }

        // Demi-escalier : garde-corps côté maille seulement (le côté extérieur est tenu par les lisses de la tour).
        var cotesGc = demi ? new[] { Cotes.Arrondi(yv + largeurVolee) } : new[] { yv, Cotes.Arrondi(yv + largeurVolee) };
        double longueurGc = montee < 1.5 ? 0.75 : 1.5;
        double sens = miroir ? -1 : 1;
        double xHaut = miroir ? x1 : x0;
        foreach (double y in cotesGc)
        {
            Ajouter(gc, Famille.Escalier, new Point3(Cotes.Arrondi(xHaut + sens * 0.5), y, z), Axe.X, Face.Exterieure, num, miroir);
            if (premiere)
            {
                Ajouter(References.GardeCorpsVoleeExtremite, Famille.Escalier,
                    new Point3(Cotes.Arrondi(xHaut + sens * (0.5 + longueurGc)), y, Cotes.Arrondi(z - montee + 1.0)), Axe.X, Face.Exterieure, num, !miroir);
            }

            // En haut de la dernière volée : pas d'extrémité côté maille, on doit pouvoir sortir (Laurent, 24/11/2026) ;
            // le demi-escalier n'a de garde-corps que côté maille, donc aucune extrémité en haut.
            if (derniere && !demi && Cotes.Egal(y, yv))
            {
                Ajouter(References.GardeCorpsVoleeExtremite, Famille.Escalier,
                    new Point3(Cotes.Arrondi(xHaut + sens * 0.5), y, Cotes.Arrondi(z + 1.0)), Axe.X, Face.Exterieure, num, miroir);
            }
        }
    }

    /// <summary>
    /// Côté maille de travail (file extérieure), le garde-corps est remplacé :
    /// - escalier et haut d'un demi-escalier : fermé par un montant HMMONTGC(E), deux lisses et une plinthe
    ///   sur 1,50 m (2,00 m pour une maille de 3,00 m), ouvert sur 1,00 m au bout où la volée arrive ;
    /// - niveau intermédiaire d'un demi-escalier : garde-corps central sur deux piquages et poteaux de 1 m,
    ///   ouvert sur 0,75 m aux deux bouts (on arrive d'un côté, on repart de l'autre).
    /// Niveau « accès seul » : fermé par des lisses.
    /// </summary>
    private void PlacerOuvertureEscalier(int num, double x0, double x1, double lm, double z, Niveau n, bool piquages, bool ouvertADroite)
    {
        double y1 = -_l;
        if (n.EstAccesSeul)
        {
            PlacerGardeCorps(TypeSecurite.Lisses, TypePlinthe.Sans, x0, y1, z, lm, Face.Exterieure, num);
            return;
        }

        double longueur;
        double xDebut;
        if (piquages)
        {
            longueur = Cotes.Arrondi(lm - 1.5);
            xDebut = Cotes.Arrondi(x0 + 0.75);
            foreach (double x in new[] { xDebut, Cotes.Arrondi(xDebut + longueur) })
            {
                Ajouter(References.PiquageLisse(_p.Modele), Famille.GardeCorps, new Point3(x, y1, z), Axe.Z, Face.Exterieure, num);
                Ajouter(References.Poteau(_p.Modele, 1.0), Famille.GardeCorps, new Point3(x, y1, Cotes.Arrondi(z + 0.04)), Axe.Z, Face.Exterieure, num);
            }
        }
        else
        {
            longueur = Cotes.Egal(lm, 3.0) ? 2.0 : 1.5;
            xDebut = ouvertADroite ? x0 : Cotes.Arrondi(x1 - longueur);
            double xMontant = ouvertADroite ? Cotes.Arrondi(x0 + longueur) : xDebut;
            Ajouter(References.MontantGardeCorps(_p.Modele), Famille.GardeCorps, new Point3(xMontant, y1, z), Axe.Z, Face.Exterieure, num);
        }

        PlacerGardeCorps(TypeSecurite.Lisses, _p.PlintheExterieure, xDebut, y1, z, longueur, Face.Exterieure, num);
    }
}
