using System;
using System.Collections.Generic;
using System.Linq;
using MultiDEC.Core.Catalogue;
using MultiDEC.Core.Facade;
using MultiDEC.Core.Facade.Regles;
using MultiDEC.Core.Geometrie;

namespace MultiDEC.Core.TourEscalier;

/// <summary>
/// Sorties acrotère (port de clFaceEscalu.AjouteSortieAccrotère, AddFranchissementAcrotere, AddStructureSortie et
/// AddStructureAngle de la v2, branche version2024 ; demande de Laurent du 28/11/2026) :
/// - ouverture de 1,00 m dans le côté de la tour ;
/// - pont de franchissement de l'acrotère (0,70 / 1,00 / 1,50 / 2,00 m × 1,00 m) : plateaux, garde-corps et plinthes des
///   deux côtés, deux poteaux de 2 m posés sur le toit (1,00 m sous le palier), tube et colliers en diagonale horizontale ;
/// - structure de sortie posée sur le toit, avec une volée de 1 m du palier au toit : droite (2,50 × 1,00 m, dans le
///   prolongement du pont) ou tournée à 90° (largeur 0,70 m pour une tour de 1,50 m, 1,00 m pour 2,00 m, sur 2,50 m le long
///   du côté, tournée vers le milieu du côté).
/// Construit dans un repère de sortie (X sortant de la tour, Y le long du côté, ouverture de Y = -1 à Y = 0), puis tourné
/// dans le repère de la tour.
/// </summary>
public sealed partial class GenerateurTourEscalier
{
    private const double LargeurPont = 1.00;
    private const double LongueurSortieAcrotere = 2.50;

    private void PlacerAcroteres()
    {
        foreach (var s in _t.Sorties.Where(s => s.Acrotere))
        {
            if (Ouverture(s.Cote, s.Hauteur) is not (double debut, double fin))
            {
                continue; // sortie ignorée (alerte déjà donnée)
            }

            double lp = TourEscalierAlu.LongueursPont.Any(l => Cotes.Egal(l, s.LongueurPont)) ? s.LongueurPont : 1.50;
            if (!Cotes.Egal(lp, s.LongueurPont))
            {
                _alertes.Add(Fr($"Pont d'acrotère de {s.LongueurPont:0.00} m non prévu (0,70 / 1,00 / 1,50 / 2,00 m) : pont de 1,50 m."));
            }

            new SortieAcrotere(this, s, debut, lp).Construire();
            _alertes.Add(Fr($"Sortie acrotère côté {s.Cote} au palier {s.Hauteur:0.00} m : toit supposé {TourEscalierAlu.HauteurAcrotere:0.00} m sous le palier, à vérifier sur place."));
        }
    }

    /// <summary>Une sortie acrotère, dans son repère propre.</summary>
    private sealed class SortieAcrotere
    {
        private readonly GenerateurTourEscalier _g;
        private readonly SortieTour _s;
        private readonly double _lp;
        private readonly double _z;
        private readonly double _zr;
        private readonly double _angle;
        private readonly Point3 _origine;
        private readonly RepereCoteTour _repere;

        /// <summary>Le côté de la tour, dans le repère de sortie : de <see cref="_yCote0"/> (abscisse 0 du côté) à <see cref="_yCote1"/>.</summary>
        private readonly double _yCote0;
        private readonly double _yCote1;

        public SortieAcrotere(GenerateurTourEscalier g, SortieTour s, double debut, double lp)
        {
            _g = g;
            _s = s;
            _lp = lp;
            _z = s.Hauteur;
            _zr = Cotes.Arrondi(s.Hauteur - TourEscalierAlu.HauteurAcrotere);
            _repere = new RepereCoteTour(s.Cote, s.Hauteur);
            double w = g._w;
            double longueurCote = s.Cote == CoteTour.A || s.Cote == CoteTour.C ? L : w;

            // Repère de sortie : X vers l'extérieur du côté ; l'ouverture [debut, debut + 1] du côté va de Y = -1 à Y = 0.
            switch (s.Cote)
            {
                case CoteTour.A: // X sortant = +Y tour, Y de sortie = -X tour
                    _angle = 90;
                    _origine = new Point3(debut, 0, 0);
                    break;
                case CoteTour.C: // X sortant = -Y tour, Y de sortie = +X tour
                    _angle = 270;
                    _origine = new Point3(Cotes.Arrondi(debut + LargeurPont), -w, 0);
                    break;
                case CoteTour.B: // X sortant = +X tour, Y de sortie = +Y tour
                    _angle = 0;
                    _origine = new Point3(L, Cotes.Arrondi(-debut), 0);
                    break;
                default: // D : X sortant = -X tour, Y de sortie = -Y tour
                    _angle = 180;
                    _origine = new Point3(0, Cotes.Arrondi(-debut - LargeurPont), 0);
                    break;
            }

            bool aEndroit = s.Cote == CoteTour.A || s.Cote == CoteTour.B;
            _yCote0 = aEndroit ? debut : -debut - LargeurPont;
            _yCote1 = aEndroit ? debut - longueurCote : longueurCote - debut - LargeurPont;
        }

        public void Construire()
        {
            Pont();
            Contreventement();
            if (_s.Tournee)
            {
                StructureTournee();
            }
            else
            {
                StructureDroite();
            }
        }

        // ---------------------------------------------------------------- pont de franchissement

        private void Pont()
        {
            var p = _g._p;
            foreach (var (variante, rang) in GenerateurFacade.RepartitionPlateaux(LargeurPont, p.Plateaux))
            {
                double y = Cotes.Arrondi(-GenerateurFacade.RetraitPlateau - GenerateurFacade.LargeurPlateau * rang);
                Poser(References.Plateau(p.Plateaux, _lp, LargeurPont, variante), Famille.Plateau, 0, y, _z, Axe.X, Face.Aucune);
            }

            foreach (double y in new[] { 0.0, -LargeurPont })
            {
                var face = y < 0 ? Face.Exterieure : Face.Interieure;
                foreach (double h in new[] { 0.5, 1.0 })
                {
                    Poser(References.LisseOuTraverse(_lp), Famille.Lisse, 0, y, _z + h, Axe.X, face);
                }

                if (References.Plinthe(p.PlintheExterieure, _lp) is string plinthe)
                {
                    Poser(plinthe, Famille.Plinthe, 0, y, _z, Axe.X, face);
                }

                PoteauSurToit(_lp, y);
            }

            // Bout du pont : traverse porteuse des plateaux au palier, traverse de pied sur le toit.
            Poser(References.LisseOuTraverse(LargeurPont, TypeTraverse.Renforce), Famille.Traverse, _lp, 0, _z, Axe.Y, Face.Aucune);
            Poser(References.LisseOuTraverse(LargeurPont), Famille.Traverse, _lp, 0, _zr, Axe.Y, Face.Aucune);
        }

        /// <summary>
        /// Tube faisant fonction de diagonale horizontale sous le pont (v2) : du bout du pont au poteau de la tour le plus
        /// éloigné de l'ouverture sur le même côté, avec un collier fixe à chaque bout.
        /// </summary>
        private void Contreventement()
        {
            double coin = Math.Abs(_yCote0 + 0.5) >= Math.Abs(_yCote1 + 0.5) ? _yCote0 : _yCote1;
            double yPont = coin > -0.5 ? 0.0 : -LargeurPont;
            double dy = coin - yPont;
            double longueur = Math.Sqrt(_lp * _lp + dy * dy);
            if (Math.Abs(dy) < 0.3 || References.LongueurTube(longueur) is not double tube)
            {
                return;
            }

            double z = Cotes.Arrondi(_z - 0.10);
            double angle = Math.Atan2(dy, -_lp) * 180 / Math.PI;
            Poser(References.Tube(tube), Famille.Amarrage, _lp, yPont, z, Axe.X, Face.Aucune, rotation: angle);
            Poser(References.CollierFixe, Famille.Amarrage, _lp, yPont, z, Axe.Z, Face.Aucune, rotation: angle);
            Poser(References.CollierFixe, Famille.Amarrage, 0, coin, z, Axe.Z, Face.Aucune, rotation: angle);
        }

        // ---------------------------------------------------------------- structure droite (« D »)

        private void StructureDroite()
        {
            double x0 = _lp, x1 = Cotes.Arrondi(_lp + LongueurSortieAcrotere);
            foreach (double y in new[] { 0.0, -LargeurPont })
            {
                var face = y < 0 ? Face.Exterieure : Face.Interieure;
                PoteauSurToit(x1, y);
                foreach (double z in Niveaux())
                {
                    Poser(References.LisseOuTraverse(LongueurSortieAcrotere), Famille.Lisse, x0, y, z, Axe.X, face);
                }
            }

            Poser(References.LisseOuTraverse(LargeurPont, TypeTraverse.Renforce), Famille.Traverse, x1, 0, _zr, Axe.Y, Face.Aucune);
            Poser(References.LisseOuTraverse(LargeurPont), Famille.Traverse, x1, 0, _z + 1.0, Axe.Y, Face.Aucune);

            // Volée de 1 m (large, v2 RefEsca(2, 1)) du bout du pont au toit, traverse intermédiaire et plateaux au pied.
            double lv = 0.88;
            Poser(References.Volee(L, 1.0, true), Famille.Escalier, x0, Cotes.Arrondi(-LargeurPont + (LargeurPont - lv) / 2), _z, Axe.X, Face.Exterieure);
            double xPied = Cotes.Arrondi(x0 + LongueurVolee1m);
            Poser(References.TraverseIntermediaire(LargeurPont), Famille.Traverse, xPied, 0, _zr, Axe.Y, Face.Exterieure);
            foreach (var (_, rang) in GenerateurFacade.RepartitionPlateaux(Cotes.Arrondi(x1 - xPied), GammePlateaux.Standard))
            {
                double x = Cotes.Arrondi(x1 - GenerateurFacade.RetraitPlateau - GenerateurFacade.LargeurPlateau * rang);
                Poser(References.PlateauAcierComplement(LargeurPont), Famille.Plateau, x, 0, _zr, Axe.Y, Face.Aucune);
            }

            // Diagonales de 2 m sur les deux grands côtés, en sens opposés.
            string dia = References.DiagonaleVerticale(LongueurSortieAcrotere);
            Poser(dia, Famille.DiagonaleVerticale, x0, Cotes.Arrondi(-LargeurPont - GenerateurFacade.DemiTube), _zr, Axe.X, Face.Exterieure);
            Poser(dia, Famille.DiagonaleVerticale, x1, GenerateurFacade.DemiTube, _zr, Axe.X, Face.Interieure, inverse: true);
        }

        // ---------------------------------------------------------------- structure tournée (« T »)

        /// <summary>
        /// Structure tournée à 90° : le long du côté de la tour, de l'ouverture vers le milieu du côté. Palier de 0,75 m au
        /// niveau du pont (deux plateaux et la traverse intermédiaire), puis la volée de 1 m descend jusqu'au toit.
        /// Coordonnées écrites pour une structure tournée vers +Y, puis retournées par <see cref="Y"/> si besoin.
        /// </summary>
        private void StructureTournee()
        {
            var p = _g._p;
            double ls = _g._t.VoleeLarge ? 1.00 : 0.70;
            double x0 = _lp, x1 = Cotes.Arrondi(_lp + ls);
            double yFin = Cotes.Arrondi(-LargeurPont + LongueurSortieAcrotere); // 1,50
            double yPalier = Cotes.Arrondi(-LargeurPont + (LongueurSortieAcrotere - LongueurVolee1m)); // -0,25

            PoteauSurToit(x1, Y(-LargeurPont));
            PoteauSurToit(x1, Y(yFin));
            PoteauSurToit(x0, Y(yFin));

            foreach (double z in Niveaux())
            {
                BarreY(References.LisseOuTraverse(Cotes.Arrondi(yFin)), Famille.Lisse, x0, 0, yFin, z);
                BarreY(References.LisseOuTraverse(LongueurSortieAcrotere), Famille.Lisse, x1, -LargeurPont, yFin, z);
            }

            foreach (double z in new[] { _zr, _z, _z + 0.5, _z + 1.0 })
            {
                var type = Cotes.Egal(z, _z) ? TypeTraverse.Renforce : TypeTraverse.Lisse;
                Poser(References.LisseOuTraverse(ls, type), Famille.Traverse, x0, Y(-LargeurPont), z, Axe.X, FaceY(Face.Exterieure));
            }

            foreach (double z in new[] { _zr, _z + 1.0 })
            {
                Poser(References.LisseOuTraverse(ls), Famille.Traverse, x0, Y(yFin), z, Axe.X, FaceY(Face.Interieure));
            }

            // Palier au niveau du pont : deux plateaux et la traverse intermédiaire sous le haut de la volée.
            foreach (var (variante, rang) in GenerateurFacade.RepartitionPlateaux(Cotes.Arrondi(yPalier + LargeurPont), p.Plateaux))
            {
                double a = Cotes.Arrondi(-LargeurPont + GenerateurFacade.RetraitPlateau + GenerateurFacade.LargeurPlateau * rang);
                double b = Cotes.Arrondi(a + GenerateurFacade.LargeurPlateau);
                Poser(References.Plateau(p.Plateaux, ls, ls, variante), Famille.Plateau, x0, Haut(a, b), _z, Axe.X, Face.Aucune);
            }

            Poser(References.TraverseIntermediaire(ls), Famille.Traverse, x0, Y(yPalier), _z, Axe.X, Face.Exterieure);
            if (References.Plinthe(p.PlintheExterieure, ls) is string plintheBout)
            {
                Poser(plintheBout, Famille.Plinthe, x0, Y(-LargeurPont), _z, Axe.X, FaceY(Face.Exterieure));
            }

            if (References.Plinthe(p.PlintheExterieure, Cotes.Arrondi(yPalier + LargeurPont)) is string plintheCote)
            {
                BarreY(plintheCote, Famille.Plinthe, x1, -LargeurPont, yPalier, _z);
            }

            // Volée de 1 m le long du côté, du palier au toit (v2 RefEsca(largeur de la tour, 1)).
            double lv = _g._t.LargeurVolee;
            double milieu = Cotes.Arrondi(x0 + ls / 2);
            bool versPlus = Sens > 0;
            Poser(References.Volee(L, 1.0, _g._t.VoleeLarge), Famille.Escalier, Cotes.Arrondi(versPlus ? milieu + lv / 2 : milieu - lv / 2), Y(yPalier), _z,
                Axe.X, Face.Exterieure, rotation: versPlus ? 90 : -90);

            BarreY(References.DiagonaleVerticale(LongueurSortieAcrotere), Famille.DiagonaleVerticale, Cotes.Arrondi(x1 + GenerateurFacade.DemiTube), -LargeurPont, yFin, _zr);
        }

        /// <summary>Sens de la structure tournée : vers le milieu du côté de la tour (+1 = vers +Y de sortie).</summary>
        private double Sens => (_yCote0 + _yCote1) / 2 > -0.5 ? 1 : -1;

        /// <summary>Ordonnée écrite pour une structure tournée vers +Y, retournée autour du milieu de l'ouverture sinon.</summary>
        private double Y(double y) => Sens > 0 ? y : Cotes.Arrondi(-LargeurPont - y);

        /// <summary>Bord haut (origine d'un plateau en long, sa largeur vers -Y) de l'intervalle [a, b] une fois retourné.</summary>
        private double Haut(double a, double b) => Math.Max(Y(a), Y(b));

        private Face FaceY(Face face) => Sens > 0 ? face : face == Face.Exterieure ? Face.Interieure : Face.Exterieure;

        /// <summary>Barre le long de Y de sortie entre a et b (écrits vers +Y) : origine au bout haut, elle part vers -Y.</summary>
        private void BarreY(string reference, Famille famille, double x, double a, double b, double z) =>
            Poser(reference, famille, x, Math.Max(Y(a), Y(b)), z, Axe.Y, Face.Aucune);

        // ---------------------------------------------------------------- outils

        /// <summary>Lisses de la structure de sortie : du toit au garde-corps du palier, tous les 0,50 m (v2).</summary>
        private IEnumerable<double> Niveaux()
        {
            for (double z = _zr; z <= _z + 1.0 + Cotes.Tolerance; z = Cotes.Arrondi(z + 0.5))
            {
                yield return z;
            }
        }

        /// <summary>Poteau de 2 m posé sur le toit (cale, socle, embase) : il monte au garde-corps du palier.</summary>
        private void PoteauSurToit(double x, double y)
        {
            foreach (var t in ReglePoteaux.Empiler(_g._p, false, gardeCorps: true, sommetVise: TourEscalierAlu.HauteurAcrotere + 1.0))
            {
                Poser(t.Reference, t.Famille, x, y, Cotes.Arrondi(_zr + t.Z), Axe.Z, y < -0.5 ? Face.Exterieure : Face.Interieure);
            }
        }

        /// <summary>Pose un élément écrit dans le repère de sortie, tourné et placé dans le repère de la tour.</summary>
        private void Poser(string reference, Famille famille, double x, double y, double z, Axe axe, Face face, bool inverse = false, double rotation = 0)
        {
            double a = _angle * Math.PI / 180;
            double c = Math.Round(Math.Cos(a), 12), s = Math.Round(Math.Sin(a), 12);
            var origine = new Point3(
                Cotes.Arrondi(_origine.X + x * c - y * s),
                Cotes.Arrondi(_origine.Y + x * s + y * c),
                Cotes.Arrondi(z));
            _g._el.Add(new ElementPlace(reference, famille, origine, axe, face, 0, inverse, 0, rotation + _angle) { CoteTour = _repere });
        }
    }
}
