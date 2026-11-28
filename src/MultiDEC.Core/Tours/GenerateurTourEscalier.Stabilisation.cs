using System;
using System.Collections.Generic;
using System.Linq;
using MultiDEC.Core.Catalogue;
using MultiDEC.Core.Facade;
using MultiDEC.Core.Facade.Regles;
using MultiDEC.Core.Geometrie;

namespace MultiDEC.Core.TourEscalier;

/// <summary>
/// Stabilisation de la tour sur deux côtés (Laurent, 28/11/2026, exemple : plan LV Tec O240610, tour 2,50 × 1,50 de 7 m,
/// emprise 5,00 × 4,00) : structure de base de 3,00 m (réglable) toujours côté C, prolongée vers le bout opposé à l'entrée :
/// - module contre le côté C (2,50 × 2,50) et module contre le bout (2,50 × largeur de la tour) : poteaux, lisses tous les
///   1,00 m, diagonales verticales HMDIA sur leurs faces extérieures ;
/// - module d'angle (en diagonale de la tour) : seulement des diagonales horizontales (tubes HMTUB + colliers HMCOLF49) ;
/// - bracons : diagonales verticales HMDIA du haut des poteaux extérieurs jusqu'aux poteaux de la tour, 2 m plus haut ;
/// - diagonales horizontales (tubes + colliers) dans chaque module, 10 cm sous la dernière rosace.
/// </summary>
public sealed partial class GenerateurTourEscalier
{
    /// <summary>Profondeur des modules de stabilisation (perpendiculaire au côté C).</summary>
    private const double ProfondeurStabilisation = 2.50;

    /// <summary>Module fait seulement de diagonales horizontales : celui d'angle (choix en attente de confirmation de Laurent).</summary>
    internal static bool AngleEnTubesSeuls { get; set; } = true;

    private void PlacerStabilisation()
    {
        double hs = _t.HauteurStabilisation;
        if (hs < 2.0 - Cotes.Tolerance || !Cotes.Egal(hs * 2, Math.Round(hs * 2)))
        {
            _alertes.Add(Fr($"Hauteur de stabilisation de {hs:0.00} m : multiple de 0,50 m d'au moins 2,00 m attendu, 3,00 m retenus."));
            hs = 3.0;
        }

        if (hs > _h - Cotes.Tolerance)
        {
            hs = Math.Max(2.0, Math.Floor((_h - 1.0) * 2) / 2);
            _alertes.Add(Fr($"Stabilisation ramenée à {hs:0.00} m (sous le dernier palier)."));
        }

        double d = ProfondeurStabilisation;
        double yC = -_w, yLoin = Cotes.Arrondi(-_w - d);
        double sens = _t.Entree == CoteTour.D ? 1 : -1;     // vers le bout opposé à l'entrée
        double xBout = sens > 0 ? L : 0.0;                 // bout de la tour contre lequel vient le module de bout
        double xLoin = Cotes.Arrondi(xBout + sens * L);
        double xb0 = Math.Min(xBout, xLoin), xb1 = Math.Max(xBout, xLoin);

        // Modules : (x0, x1, y0 haut, y1 bas, plein).
        var moduleC = (X0: 0.0, X1: L, Y0: yC, Y1: yLoin, Plein: true);
        var moduleBout = (X0: xb0, X1: xb1, Y0: 0.0, Y1: yC, Plein: AngleEnTubesSeuls);
        var moduleAngle = (X0: xb0, X1: xb1, Y0: yC, Y1: yLoin, Plein: !AngleEnTubesSeuls);
        var modules = new[] { moduleC, moduleBout, moduleAngle };

        // Poteaux : coins des modules pleins, sauf ceux de la tour (le coin isolé du module en tubes seuls ne sert à rien,
        // Laurent 28/11/2026 : les tubes du module d'angle vont d'un poteau existant à l'autre).
        var tour = new HashSet<(double, double)> { (0, 0), (L, 0), (0, yC), (L, yC) };
        var poteaux = modules.Where(m => m.Plein).SelectMany(m => new[] { (m.X0, m.Y0), (m.X1, m.Y0), (m.X0, m.Y1), (m.X1, m.Y1) })
            .Select(p => (Cotes.Arrondi(p.Item1), Cotes.Arrondi(p.Item2))).Distinct().Where(p => !tour.Contains(p)).ToList();
        foreach (var (x, y) in poteaux)
        {
            foreach (var t in ReglePoteaux.Empiler(_p, false, gardeCorps: false, sommetVise: hs))
            {
                Ajouter(t.Reference, t.Famille, new Point3(x, y, t.Z), Axe.Z, Face.Exterieure);
            }
        }

        // Côtés des modules pleins (sans ceux de la tour, une seule fois chacun) : lisses et diagonales verticales.
        var cotesTour = new HashSet<string> { Cle(0, yC, L, yC), Cle(xBout, 0, xBout, yC) };
        var faits = new HashSet<string>();
        foreach (var m in modules.Where(m => m.Plein))
        {
            foreach (var (xa, ya, xc, yc) in new[] { (m.X0, m.Y0, m.X1, m.Y0), (m.X0, m.Y1, m.X1, m.Y1), (m.X0, m.Y0, m.X0, m.Y1), (m.X1, m.Y0, m.X1, m.Y1) })
            {
                string cle = Cle(xa, ya, xc, yc);
                if (cotesTour.Contains(cle) || !faits.Add(cle))
                {
                    continue;
                }

                CoteStabilisation(xa, ya, xc, yc, hs);
            }
        }

        // Bracons : du haut des poteaux extérieurs aux poteaux de la tour, 2 m plus haut (faces perpendiculaires à la tour).
        if (hs + 2.0 <= _h + 1.0 + Cotes.Tolerance)
        {
            string diaD = References.DiagonaleVerticale(d);
            foreach (double x in new[] { 0.0, L })
            {
                Ajouter(diaD, Famille.DiagonaleVerticale, new Point3(x, yLoin, hs), Axe.Y, Face.Exterieure, rotation: 180);
            }

            string diaL = References.DiagonaleVerticale(L);
            foreach (double y in new[] { 0.0, yC })
            {
                Ajouter(diaL, Famille.DiagonaleVerticale, new Point3(xLoin, y, hs), Axe.X, y < 0 ? Face.Exterieure : Face.Interieure, inverse: sens > 0);
            }
        }
        else
        {
            _alertes.Add(Fr($"Tour trop basse pour les bracons de stabilisation (poteaux de la tour jusqu'à {hs + 2.0:0.00} m)."));
        }

        // Diagonales horizontales : un tube et deux colliers par module, 10 cm sous la dernière rosace.
        double zh = Cotes.Arrondi(hs - 0.10);
        foreach (var m in modules)
        {
            TubeHorizontal(m.X0, m.Y1, m.X1, m.Y0, zh);
        }

        _alertes.Add("Tour stabilisée côté extérieur : structure autostable pour un vent < 85 km/h ; au-delà, la stabiliser (ancrage, lest…).");
    }

    private static string Cle(double xa, double ya, double xc, double yc)
    {
        var a = (Cotes.Arrondi(Math.Min(xa, xc)), Cotes.Arrondi(Math.Max(ya, yc)));
        var b = (Cotes.Arrondi(Math.Max(xa, xc)), Cotes.Arrondi(Math.Min(ya, yc)));
        return FormattableString.Invariant($"{a.Item1};{a.Item2};{b.Item1};{b.Item2}");
    }

    /// <summary>Un côté de module : lisses tous les 1,00 m du pied au sommet, diagonales de 2 m en zigzag.</summary>
    private void CoteStabilisation(double xa, double ya, double xc, double yc, double hs)
    {
        bool enX = Cotes.Egal(ya, yc);
        double longueur = Cotes.Arrondi(enX ? Math.Abs(xc - xa) : Math.Abs(yc - ya));
        double x0 = Math.Min(xa, xc), x1 = Math.Max(xa, xc), yHaut = Math.Max(ya, yc), yBas = Math.Min(ya, yc);
        string lisse = References.LisseOuTraverse(longueur);
        var niveaux = new List<double>();
        for (double z = 0; z < hs - Cotes.Tolerance; z = Cotes.Arrondi(z + 1.0))
        {
            niveaux.Add(z);
        }

        niveaux.Add(hs);
        foreach (double z in niveaux)
        {
            if (enX)
            {
                Ajouter(lisse, Famille.Lisse, new Point3(x0, ya, z), Axe.X, Face.Exterieure);
            }
            else
            {
                Ajouter(lisse, Famille.Traverse, new Point3(xa, yHaut, z), Axe.Y, Face.Aucune);
            }
        }

        string dia = References.DiagonaleVerticale(longueur);
        int tranche = 0;
        for (double z = 0; z + 2.0 <= hs + Cotes.Tolerance; z = Cotes.Arrondi(z + 2.0), tranche++)
        {
            bool inverse = tranche % 2 == 1;
            if (enX)
            {
                Ajouter(dia, Famille.DiagonaleVerticale, new Point3(inverse ? x1 : x0, Cotes.Arrondi(ya - GenerateurFacade.DemiTube), z), Axe.X, Face.Exterieure, inverse: inverse);
            }
            else
            {
                Ajouter(dia, Famille.DiagonaleVerticale, new Point3(Cotes.Arrondi(xa + GenerateurFacade.DemiTube), inverse ? yBas : yHaut, z), Axe.Y, Face.Exterieure,
                    inverse: inverse, rotation: inverse ? 180 : 0);
            }
        }
    }

    /// <summary>Tube en diagonale horizontale de (xa, ya) à (xb, yb), un collier fixe à chaque bout.</summary>
    private void TubeHorizontal(double xa, double ya, double xb, double yb, double z)
    {
        double dx = xb - xa, dy = yb - ya;
        if (References.LongueurTube(Math.Sqrt(dx * dx + dy * dy)) is not double longueur)
        {
            _alertes.Add("Diagonale horizontale de stabilisation trop longue pour un tube de 6,00 m.");
            return;
        }

        double angle = Math.Atan2(dy, dx) * 180 / Math.PI;
        Ajouter(References.Tube(longueur), Famille.DiagonaleHorizontale, new Point3(Cotes.Arrondi(xa), Cotes.Arrondi(ya), z), Axe.X, Face.Aucune, rotation: angle);
        Ajouter(References.CollierFixe, Famille.DiagonaleHorizontale, new Point3(Cotes.Arrondi(xa), Cotes.Arrondi(ya), z), Axe.Z, Face.Aucune, rotation: angle);
        Ajouter(References.CollierFixe, Famille.DiagonaleHorizontale, new Point3(Cotes.Arrondi(xb), Cotes.Arrondi(yb), z), Axe.Z, Face.Aucune, rotation: angle);
    }
}
