using System;
using System.Collections.Generic;
using System.Linq;
using MultiDEC.Core.Catalogue;
using MultiDEC.Core.Facade;
using MultiDEC.Core.Facade.Regles;
using MultiDEC.Core.Geometrie;

namespace MultiDEC.Core.TourEscalier;

/// <summary>Côté et palier d'une tour auxquels appartient un élément du dessin (clic droit « Ouvrir / Fermer »).</summary>
public sealed class RepereCoteTour
{
    public RepereCoteTour(CoteTour cote, double palier)
    {
        Cote = cote;
        Palier = palier;
    }

    public CoteTour Cote { get; }

    public double Palier { get; }
}

/// <summary>Résultat de la génération d'une tour, dans son repère local.</summary>
public sealed class ResultatTour
{
    internal ResultatTour(IReadOnlyList<ElementPlace> elements, IReadOnlyList<Amarrage> amarrages, IReadOnlyList<string> alertes)
    {
        Elements = elements;
        Amarrages = amarrages;
        Alertes = alertes;
    }

    public IReadOnlyList<ElementPlace> Elements { get; }

    public IReadOnlyList<Amarrage> Amarrages { get; }

    public IReadOnlyList<string> Alertes { get; }
}

/// <summary>
/// Moteur de la tour escalier alu (port de clTESCALU, branche version2024, avec les conventions de blocs relevées pour
/// l'escalier rapporté de la façade). Repère local de la tour : X de 0 (bout D) à 2,50 m (bout B), Y de 0 (côté A) à
/// -largeur (côté C), Z vers le haut.
/// - 4 files de poteaux, lisses tous les 0,50 m sur les quatre côtés, traverses renforcées au bout où arrivent les volées ;
/// - une volée par palier, alternativement dans la moitié côté A et la moitié côté C, en sens opposés ; la dernière volée
///   arrive dans la moitié côté A ; volée de 1 m (HMESCALU175…) avec traverse intermédiaire HMTIP et plateaux acier ;
/// - sorties : ouverture de 0,50 / 1,00 m sur un grand côté, ou sur le bout où arrive la volée (montant de garde-corps,
///   lisses et plinthe raccourcies) ;
/// - diagonales de 2 m sur les quatre côtés, sauf devant l'entrée et les sorties ;
/// - départ à 0,50 m (hauteur en xx,50 m) : plancher de départ en plateaux acier et maille d'entrée de 0,70 m au niveau 0.
/// </summary>
public sealed partial class GenerateurTourEscalier
{
    private const double L = TourEscalierAlu.Longueur;

    /// <summary>Jeu entre une volée et la file de poteaux voisine.</summary>
    private const double Jeu = 0.05;

    /// <summary>Longueur en plan de la volée de 1,00 m de montée (HMESCALU175100).</summary>
    private const double LongueurVolee1m = 1.75;

    /// <summary>Hauteur du montant de garde-corps (HMMONTGC) au-dessus du palier, où se pose le poteau de 0,50 m (v2).</summary>
    private const double HautMontant = 1.05;

    private readonly TourEscalierAlu _t;
    private readonly ParametresFacade _p;
    private readonly double _w;
    private readonly IReadOnlyList<double> _paliers;
    private readonly double _h;
    private readonly double _zb;
    private readonly List<ElementPlace> _el = new List<ElementPlace>();
    private readonly List<string> _alertes = new List<string>();
    private readonly List<Amarrage> _amarrages = new List<Amarrage>();
    private readonly List<(CoteTour Cote, double Palier, double Debut, double Fin)> _ouvertures = new List<(CoteTour, double, double, double)>();

    private GenerateurTourEscalier(TourEscalierAlu tour, ParametresGeneraux general)
    {
        _t = tour;
        _w = TourEscalierAlu.Largeurs.Any(l => Cotes.Egal(l, tour.Largeur)) ? tour.Largeur : 1.50;
        _paliers = tour.Paliers();
        _h = _paliers[_paliers.Count - 1];
        _zb = _paliers[0];
        _p = new ParametresFacade
        {
            LargeurMaille = _w,
            HauteurDernierPlancher = _h,
            Depart3mExterieur = tour.Depart3m,
        };
        _p.AppliquerGeneraux(general);
        _p.Grutable = false;
        _p.GardeCorpsCouvreur = false;
    }

    /// <summary>Génère la tour dans son repère local. <paramref name="accolee"/> : tenue par la façade, pas d'amarrage au bâtiment.</summary>
    public static ResultatTour Generer(TourEscalierAlu tour, ParametresGeneraux general, bool accolee = false)
    {
        if (tour.ErreurHauteur() is string erreur)
        {
            return new ResultatTour(Array.Empty<ElementPlace>(), Array.Empty<Amarrage>(), new[] { erreur + " Tour non dessinée." });
        }

        var g = new GenerateurTourEscalier(tour, general);
        if (!Cotes.Egal(g._w, tour.Largeur))
        {
            g._alertes.Add(FormattableString.Invariant($"Largeur de {tour.Largeur:0.00} m non prévue : tour dessinée en 1,50 m."));
        }

        g.Construire(accolee, general);
        return new ResultatTour(g._el, g._amarrages, g._alertes);
    }

    private void Construire(bool accolee, ParametresGeneraux general)
    {
        ReleverOuvertures();
        PlacerPoteaux();
        PlacerLisses();
        PlacerPlinthes();
        PlacerDiagonales();
        PlacerVolees();
        PlacerAcroteres();
        if (_zb > Cotes.Tolerance)
        {
            PlacerPlancherDepart();
        }

        if (!accolee)
        {
            if (_t.Stabilite == StabiliteTour.Amarree)
            {
                PlacerAmarrages(general);
            }
            else if (_t.Stabilite == StabiliteTour.Stabilisee)
            {
                PlacerStabilisation();
            }
            else
            {
                _alertes.Add("Tour autostable lestée : faire calculer le lest (note de calcul) avant le montage.");
            }
        }

        if (_h > general.SeuilNoteDeCalcul + Cotes.Tolerance)
        {
            _alertes.Add(string.Format(CultureNombres.Documents, "Tour de plus de {0:0} m : note de calcul obligatoire.", general.SeuilNoteDeCalcul));
        }
    }

    // ------------------------------------------------------------------------------------------
    // Géométrie des paliers et des sorties
    // ------------------------------------------------------------------------------------------

    private int NombreVolees => _paliers.Count - 1;

    /// <summary>
    /// Dernière volée de 1 m : son palier est fait de 2 plateaux en travers (toute la largeur) entre la traverse
    /// intermédiaire et le bout, dont la traverse ne porte plus rien (Laurent, 28/11/2026).
    /// </summary>
    private bool PalierHautEnTravers => NombreVolees >= 1 && Cotes.Egal(_paliers[NombreVolees] - _paliers[NombreVolees - 1], 1.0);

    /// <summary>Moitié de la largeur occupée par la volée <paramref name="k"/> (1..n) : 0 côté A, 1 côté C ; la dernière côté A.</summary>
    private int Moitie(int k) => (NombreVolees - k) % 2 == 0 ? 0 : 1;

    private double XBout(CoteTour bout) => bout == CoteTour.B ? L : 0.0;

    private int IndexPalier(double z)
    {
        for (int i = 0; i < _paliers.Count; i++)
        {
            if (Cotes.Egal(_paliers[i], z))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Palier auquel se rattache une pièce posée à la cote <paramref name="z"/> (le plus haut palier en dessous).</summary>
    private double PalierDe(double z) => _paliers.Where(p => p <= z + Cotes.Tolerance).DefaultIfEmpty(_paliers[0]).Max();

    /// <summary>
    /// Ouvertures retenues : (côté, palier, début, fin) le long du côté (X pour A et C, -Y pour B et D, mesuré depuis Y = 0).
    /// Une sortie sur un bout n'est possible qu'au bout où arrive la volée ; sinon elle est ignorée avec une alerte.
    /// </summary>
    private void ReleverOuvertures()
    {
        foreach (var s in _t.Sorties.GroupBy(s => (Cotes.Arrondi(s.Hauteur), s.Cote)).Select(g => g.First()))
        {
            int k = IndexPalier(s.Hauteur);
            if (k <= 0)
            {
                _alertes.Add(Fr($"Sortie {NomsCotesTour.Nom(s.Cote)} à {s.Hauteur:0.00} m : pas de palier à cette hauteur, ignorée."));
                continue;
            }

            var arrivee = _t.BoutArrivee(k);
            double o = _t.LargeurOuverture(s);
            if (s.Cote == CoteTour.A || s.Cote == CoteTour.C)
            {
                // Grand côté : ouverture au bout du palier (v2 : 0,50 ou 1,00 m, le reste fermé par une lisse de 2,00 / 1,50 m).
                _ouvertures.Add(arrivee == CoteTour.D ? (s.Cote, s.Hauteur, 0.0, o) : (s.Cote, s.Hauteur, L - o, L));
            }
            else if (s.Cote != arrivee)
            {
                _alertes.Add(Fr($"Sortie {NomsCotesTour.Nom(s.Cote)} à {s.Hauteur:0.00} m : la volée arrive au {NomsCotesTour.Nom(arrivee)}, ignorée."));
            }
            else
            {
                // Bout : ouverture devant la moitié où arrive la volée.
                _ouvertures.Add(Moitie(k) == 0 ? (s.Cote, s.Hauteur, 0.0, o) : (s.Cote, s.Hauteur, _w - o, _w));
            }
        }

        if (_zb > Cotes.Tolerance)
        {
            // Départ de 0,50 m : entrée devant le pied de la première volée, l'autre moitié du bout fermée par un montant de
            // garde-corps et des lisses courtes, sans plinthe (Laurent, 28/11/2026).
            double o = _t.LargeurOuverture(new SortieTour(_zb, _t.Entree));
            _ouvertures.Add(Moitie(1) == 0 ? (_t.Entree, _zb, 0.0, o) : (_t.Entree, _zb, _w - o, _w));
        }
    }

    private (double Debut, double Fin)? Ouverture(CoteTour cote, double palier) =>
        _ouvertures.Where(o => o.Cote == cote && Cotes.Egal(o.Palier, palier)).Select(o => ((double, double)?)(o.Debut, o.Fin)).FirstOrDefault();

    /// <summary>
    /// Lisses des bouts (Laurent, 28/11/2026) : au bout d'un palier d'escalier, lisses tous les 0,50 m de 0 à 2 m au-dessus
    /// du palier ; au bout opposé, pas de lisse entre +0,50 et +1,50 m (la volée suivante y monte). Au sol, pas de palier :
    /// aucune lisse entre 0,50 et 1,50 m sur les deux bouts (départ à 0,50 m : entrée traitée comme une ouverture). Au-dessus du dernier palier, le garde-corps reste complet. Une lisse demandée par un palier l'emporte.
    /// </summary>
    private bool SansLisseDeBout(CoteTour cote, double z)
    {
        if (cote == CoteTour.A || cote == CoteTour.C || z <= Cotes.Tolerance)
        {
            return false;
        }

        bool exclue = false;
        for (int k = 0; k < NombreVolees; k++)
        {
            if (k == 0 && _zb > Cotes.Tolerance)
            {
                continue;
            }

            // Au sol, il n'y a pas de palier : aucun des deux bouts n'a de lisse entre 0,50 et 1,50 m (Laurent, 28/11/2026).
            double dz = Cotes.Arrondi(z - _paliers[k]);
            if ((k == 0 || _t.BoutArrivee(k) != cote) && dz >= 0.5 - Cotes.Tolerance && dz <= 1.5 + Cotes.Tolerance)
            {
                exclue = true;
            }
        }

        if (!exclue)
        {
            return false;
        }

        for (int k = 1; k <= NombreVolees; k++)
        {
            double dz = Cotes.Arrondi(z - _paliers[k]);
            if (_t.BoutArrivee(k) == cote && dz >= -Cotes.Tolerance && dz <= 2.0 + Cotes.Tolerance)
            {
                return false;
            }
        }

        return true;
    }

    // ------------------------------------------------------------------------------------------
    // Poteaux, lisses, plinthes, diagonales
    // ------------------------------------------------------------------------------------------

    private void PlacerPoteaux()
    {
        foreach (double x in new[] { 0.0, L })
        {
            foreach (double y in new[] { 0.0, -_w })
            {
                foreach (var t in ReglePoteaux.Empiler(_p, _t.Depart3m, gardeCorps: true))
                {
                    Ajouter(t.Reference, t.Famille, new Point3(x, y, t.Z), Axe.Z, y < 0 ? Face.Exterieure : Face.Interieure);
                }
            }
        }
    }

    /// <summary>
    /// Lisses (grands côtés) et traverses (bouts) tous les 0,50 m, du pied au garde-corps du dernier palier (+1,00 m).
    /// Traverses renforcées aux paliers, au bout où arrivent les volées (elles y sont accrochées) et au départ de 0,50 m.
    /// </summary>
    private void PlacerLisses()
    {
        for (double z = 0; z <= _h + 1.0 + Cotes.Tolerance; z = Cotes.Arrondi(z + 0.5))
        {
            int k = IndexPalier(z);
            foreach (var cote in new[] { CoteTour.A, CoteTour.B, CoteTour.C, CoteTour.D })
            {
                if (SansLisseDeBout(cote, z))
                {
                    continue;
                }

                bool longue = cote == CoteTour.A || cote == CoteTour.C;
                bool renforce = !longue && k >= 0 && z > Cotes.Tolerance && (k == 0 ? cote == _t.Entree : _t.BoutArrivee(k) == cote)
                    && !(k == NombreVolees && PalierHautEnTravers);
                double longueur = longue ? L : _w;

                // Ouverture d'une sortie : les lisses à +0,50 / +1,00 / +1,50 m sont raccourcies.
                double palier = PalierDe(z);
                double dz = Cotes.Arrondi(z - palier);
                var ouverture = dz > Cotes.Tolerance && dz < 2.0 - Cotes.Tolerance ? Ouverture(cote, palier) : null;
                if (ouverture is (double debut, double fin))
                {
                    PlacerBarreFermee(cote, z, longueur, debut, fin, palier);
                    continue;
                }

                string reference = References.LisseOuTraverse(longueur, renforce ? TypeTraverse.Renforce : TypeTraverse.Lisse);
                Barre(reference, longue ? Famille.Lisse : Famille.Traverse, cote, 0, z, palier);
            }
        }

        // Montants de garde-corps aux bords des ouvertures, et poteau de 0,50 m pour la lisse à +1,50 m (v2).
        foreach (var (cote, palier, debut, fin) in _ouvertures)
        {
            bool longue = cote == CoteTour.A || cote == CoteTour.C;
            double longueur = longue ? L : _w;
            foreach (double bord in new[] { debut, fin }.Where(b => b > Cotes.Tolerance && b < longueur - Cotes.Tolerance))
            {
                var point = PointSurCote(cote, bord, palier);
                var repere = new RepereCoteTour(cote, palier);
                Ajouter(References.MontantGardeCorps(_p.Modele), Famille.GardeCorps, point, Axe.Z, FaceDe(cote), repere: repere);
                if (palier + 1.5 <= _h + 1.0 + Cotes.Tolerance && PalierDe(palier + 1.5) == palier)
                {
                    Ajouter(References.Poteau(_p.Modele, 0.5), Famille.GardeCorps, point.Decale(dz: HautMontant), Axe.Z, FaceDe(cote), repere: repere);
                }
            }
        }
    }

    /// <summary>Barre raccourcie sur la partie fermée du côté (hors de l'ouverture [debut, fin]).</summary>
    private void PlacerBarreFermee(CoteTour cote, double z, double longueur, double debut, double fin, double palier)
    {
        bool longue = cote == CoteTour.A || cote == CoteTour.C;
        foreach (var (a, b) in new[] { (0.0, debut), (fin, longueur) })
        {
            double l = Cotes.Arrondi(b - a);
            if (l <= Cotes.Tolerance)
            {
                continue;
            }

            Barre(References.LisseOuTraverse(l), longue ? Famille.Lisse : Famille.Traverse, cote, a, z, palier);
        }
    }

    /// <summary>
    /// Lisse le long d'un côté à partir de l'abscisse <paramref name="debut"/> : grands côtés le long de X,
    /// bouts le long de -Y depuis Y = 0 (comme les abouts de la façade).
    /// </summary>
    private void Barre(string reference, Famille famille, CoteTour cote, double debut, double z, double palier)
    {
        var repere = z > Cotes.Tolerance ? new RepereCoteTour(cote, palier) : null;
        switch (cote)
        {
            case CoteTour.A:
                Ajouter(reference, famille, new Point3(debut, 0, z), Axe.X, Face.Interieure, repere: repere);
                break;
            case CoteTour.C:
                Ajouter(reference, famille, new Point3(debut, -_w, z), Axe.X, Face.Exterieure, repere: repere);
                break;
            default:
                Ajouter(reference, famille, new Point3(XBout(cote), Cotes.Arrondi(-debut), z), Axe.Y, Face.Aucune, inverse: cote == CoteTour.D, repere: repere);
                break;
        }
    }

    private Point3 PointSurCote(CoteTour cote, double abscisse, double z) => cote switch
    {
        CoteTour.A => new Point3(Cotes.Arrondi(abscisse), 0, z),
        CoteTour.C => new Point3(Cotes.Arrondi(abscisse), -_w, z),
        _ => new Point3(XBout(cote), Cotes.Arrondi(-abscisse), z),
    };

    private static Face FaceDe(CoteTour cote) => cote == CoteTour.A ? Face.Interieure : Face.Exterieure;

    /// <summary>
    /// Dernier palier, bout d'arrivée : la moitié qui n'est pas celle de la dernière volée n'a pas de palier (aucune volée ne
    /// repart) ; un bout de plinthe entièrement de ce côté ne sert à rien (Laurent, 28/11/2026).
    /// </summary>
    private bool SansPalierAttenant(CoteTour cote, double z, double debut, double fin)
    {
        if (NombreVolees < 1 || !Cotes.Egal(z, _paliers[NombreVolees]) || cote != _t.BoutArrivee(NombreVolees))
        {
            return false;
        }

        double milieu = _w / 2;
        return Moitie(NombreVolees) == 0 ? debut >= milieu - Cotes.Tolerance : fin <= milieu + Cotes.Tolerance;
    }

    /// <summary>
    /// Plinthes seulement le long d'un plateau ou d'un palier d'escalier (Laurent, 28/11/2026 ; v2 : une plinthe par niveau,
    /// au bout du palier) : à chaque palier, au bout d'arrivée de la volée (raccourcie devant une sortie) ; au dernier palier,
    /// seulement devant la moitié de la dernière volée. Départ de 0,50 m : pas de plancher ni de plinthe dans la tour.
    /// </summary>
    private void PlacerPlinthes()
    {
        for (int k = 1; k <= NombreVolees; k++)
        {
            double z = _paliers[k];
            var bout = _t.BoutArrivee(k);
            var parties = Ouverture(bout, z) is (double debut, double fin)
                ? new[] { (0.0, debut), (fin, _w) }
                : new[] { (0.0, _w) };
            bool enTravers = k == NombreVolees && PalierHautEnTravers;
            foreach (var (a, b) in parties.Where(p => enTravers || !SansPalierAttenant(bout, z, p.Item1, p.Item2)))
            {
                Plinthe(bout, a, b, z);
            }

            if (enTravers)
            {
                // Plateaux en travers sur toute la largeur : plinthes aussi le long des grands côtés, d'un poteau à
                // l'autre (une plinthe ne s'arrête pas entre deux poteaux, Laurent 28/11/2026).
                foreach (var cote in new[] { CoteTour.A, CoteTour.C }.Where(c => Ouverture(c, z) == null))
                {
                    Plinthe(cote, 0, L, z);
                }
            }
        }
    }

    private void Plinthe(CoteTour cote, double debut, double fin, double z)
    {
        bool longue = cote == CoteTour.A || cote == CoteTour.C;
        var type = longue ? _p.PlintheExterieure : (_p.PlintheAbouts == TypePlinthe.Sans ? _p.PlintheExterieure : _p.PlintheAbouts);
        double l = Cotes.Arrondi(fin - debut);
        if (l > Cotes.Tolerance && References.Plinthe(type, l) is string plinthe)
        {
            Barre(plinthe, Famille.Plinthe, cote, debut, z, z);
        }
    }

    /// <summary>
    /// Diagonales de 2 m de haut sur les quatre côtés, par tranches de 2 m depuis le départ, parallèles (Laurent, 28/11/2026) ; pas de diagonale
    /// devant l'entrée ni dans une tranche où le côté a une sortie.
    /// </summary>
    private void PlacerDiagonales()
    {
        int tranche = 0;
        for (double z = _zb; z + 2.0 <= _h + Cotes.Tolerance; z = Cotes.Arrondi(z + 2.0), tranche++)
        {
            bool inverse = false; // parallèles : deux diagonales d'une face n'arrivent jamais au même nœud (Laurent, 28/11/2026)
            double xLong = inverse ? L : 0; // sens inversé : origine au bout B, la diagonale part vers -X
            int longues = 0;
            int bouts = 0;
            foreach (var cote in new[] { CoteTour.A, CoteTour.B, CoteTour.C, CoteTour.D })
            {
                double z0 = z;
                bool entree = cote == _t.Entree && tranche == 0;
                bool sortie = _ouvertures.Any(o => o.Cote == cote && o.Palier < z0 + 2.0 - Cotes.Tolerance && o.Palier + 2.0 > z0 + Cotes.Tolerance);
                if (entree || sortie)
                {
                    continue;
                }

                var repere = new RepereCoteTour(cote, PalierDe(z0 + 1.0));
                switch (cote)
                {
                    case CoteTour.A:
                        Ajouter(References.DiagonaleVerticale(L), Famille.DiagonaleVerticale, new Point3(xLong, GenerateurFacade.DemiTube, z0), Axe.X, Face.Interieure, inverse: inverse, repere: repere);
                        longues++;
                        break;
                    case CoteTour.C:
                        Ajouter(References.DiagonaleVerticale(L), Famille.DiagonaleVerticale, new Point3(xLong, Cotes.Arrondi(-_w - GenerateurFacade.DemiTube), z0), Axe.X, Face.Exterieure, inverse: inverse, repere: repere);
                        longues++;
                        break;
                    default:
                        double x = cote == CoteTour.B ? L + GenerateurFacade.DemiTube : -GenerateurFacade.DemiTube;
                        // En travers, le sens se change par un demi-tour autour de Z depuis le côté C (l'inversion d'un bloc
                        // en travers ne fait que retourner sa face).
                        Ajouter(References.DiagonaleVerticale(_w), Famille.DiagonaleVerticale, new Point3(Cotes.Arrondi(x), inverse ? -_w : 0, z0), Axe.Y,
                            Face.Exterieure, inverse: inverse, rotation: inverse ? 180 : 0, repere: repere);
                        bouts++;
                        break;
                }
            }

            if (longues == 0 || bouts == 0)
            {
                _alertes.Add(Fr($"Entre {z:0.00} et {z + 2.0:0.00} m, aucune diagonale sur les {(longues == 0 ? "grands côtés" : "bouts")} (sorties) : contreventement à vérifier."));
            }
        }
    }

    // ------------------------------------------------------------------------------------------
    // Volées
    // ------------------------------------------------------------------------------------------

    private void PlacerVolees()
    {
        double lv = _t.LargeurVolee;
        bool large = _t.VoleeLarge;
        for (int k = 1; k <= NombreVolees; k++)
        {
            double zHaut = _paliers[k];
            double montee = Cotes.Arrondi(zHaut - _paliers[k - 1]);
            if (!Cotes.Egal(montee, 1.0) && !Cotes.Egal(montee, 2.0))
            {
                _alertes.Add(Fr($"Montée de {montee:0.00} m sous le palier {zHaut:0.00} m : pas de volée (1,00 ou 2,00 m)."));
                continue;
            }

            var bout = _t.BoutArrivee(k);
            bool miroir = bout == CoteTour.B;        // la volée monte vers les X croissants
            double sens = miroir ? -1 : 1;           // du haut de la volée vers son pied
            double xBout = XBout(bout);
            int moitie = Moitie(k);
            double yv = moitie == 0 ? Cotes.Arrondi(-Jeu - lv) : Cotes.Arrondi(-_w + Jeu);
            bool unMetre = Cotes.Egal(montee, 1.0);
            bool auSol = k == 1;

            // Volée de 1 m dans les étages : son haut est à 0,75 m du bout, sur une traverse intermédiaire, et le palier
            // est complété par des plateaux acier dans sa moitié (v2). Au départ, elle arrive au bout et son pied repose
            // sur la traverse intermédiaire, les plateaux couvrant le pied jusqu'à l'entrée (comme l'escalier de façade).
            double xHaut = unMetre && !auSol ? Cotes.Arrondi(xBout + sens * (L - LongueurVolee1m)) : xBout;
            Ajouter(References.Volee(L, montee, large), Famille.Escalier, new Point3(xHaut, yv, zHaut), Axe.X, Face.Exterieure, inverse: miroir);
            if (unMetre)
            {
                if (auSol)
                {
                    PlacerPiedVolee1m(xBout, sens, _paliers[0]);
                }
                else
                {
                    if (k == NombreVolees)
                    {
                        PlacerPalierHautVolee1m(bout, xBout, xHaut, zHaut);
                    }
                    else
                    {
                        PlacerPalierVolee1m(xBout, xHaut, sens, moitie, zHaut);
                    }
                }
            }

            PlacerGardeCorpsVolee(k, montee, xHaut, sens, miroir, yv, lv, moitie, zHaut);
        }
    }

    private void PlacerGardeCorpsVolee(int k, double montee, double xHaut, double sens, bool miroir, double yv, double lv, int moitie, double z)
    {
        string? gc = References.GardeCorpsVolee(L, montee);
        if (gc == null)
        {
            return;
        }

        double longueurGc = montee < 1.5 ? 0.75 : 1.5;
        double yMilieu = moitie == 0 ? yv : Cotes.Arrondi(yv + lv); // bord de la volée côté vide central
        foreach (double y in new[] { yv, Cotes.Arrondi(yv + lv) })
        {
            Ajouter(gc, Famille.Escalier, new Point3(Cotes.Arrondi(xHaut + sens * 0.5), y, z), Axe.X, Face.Exterieure, inverse: miroir);
            if (k == 1)
            {
                Ajouter(References.GardeCorpsVoleeExtremite, Famille.Escalier,
                    new Point3(Cotes.Arrondi(xHaut + sens * (0.5 + longueurGc)), y, Cotes.Arrondi(z - montee + 1.0)), Axe.X, Face.Exterieure, inverse: !miroir);
            }

            if (k == NombreVolees && Cotes.Egal(y, yMilieu))
            {
                Ajouter(References.GardeCorpsVoleeExtremite, Famille.Escalier,
                    new Point3(Cotes.Arrondi(xHaut + sens * 0.5), y, Cotes.Arrondi(z + 1.0)), Axe.X, Face.Exterieure, inverse: miroir);
            }
        }
    }

    /// <summary>Première volée de 1 m : pied sur une traverse intermédiaire, plateaux acier en travers jusqu'au bout d'entrée.</summary>
    private void PlacerPiedVolee1m(double xBout, double sens, double z)
    {
        double xPied = Cotes.Arrondi(xBout + sens * LongueurVolee1m);
        Ajouter(References.TraverseIntermediaire(_w), Famille.Traverse, new Point3(xPied, 0, z), Axe.Y, Face.Exterieure);
        double xEntree = Cotes.Arrondi(xBout + sens * L);
        double reste = Cotes.Arrondi(Math.Abs(xEntree - xPied));
        foreach (var (_, rang) in GenerateurFacade.RepartitionPlateaux(reste, GammePlateaux.Standard))
        {
            // Plateau en travers : il part de Y = 0 vers -Y, sa largeur vers les X décroissants.
            double x = sens < 0
                ? Cotes.Arrondi(xEntree + GenerateurFacade.RetraitPlateau + GenerateurFacade.LargeurPlateau * (rang + 1))
                : Cotes.Arrondi(xEntree - GenerateurFacade.RetraitPlateau - GenerateurFacade.LargeurPlateau * rang);
            Ajouter(References.PlateauAcierComplement(_w), Famille.Plateau, new Point3(x, 0, z), Axe.Y, Face.Aucune);
        }
    }

    /// <summary>
    /// Dernière volée de 1 m (Laurent, 28/11/2026) : traverse intermédiaire sous son haut, 2 plateaux acier en travers sur
    /// toute la largeur jusqu'au bout ; devant une sortie au bout, montant de garde-corps sur la traverse intermédiaire
    /// relié à celui du bord de l'ouverture par des tubes et colliers juste sous les rosaces de +0,50 et +1,00 m.
    /// </summary>
    private void PlacerPalierHautVolee1m(CoteTour bout, double xBout, double xHaut, double z)
    {
        Ajouter(References.TraverseIntermediaire(_w), Famille.Traverse, new Point3(xHaut, 0, z), Axe.Y, Face.Exterieure);
        double xa = Math.Min(xBout, xHaut), xb = Math.Max(xBout, xHaut);
        foreach (var (_, rang) in GenerateurFacade.RepartitionPlateaux(Cotes.Arrondi(xb - xa), GammePlateaux.Standard))
        {
            // Plateau en travers : il part de Y = 0 vers -Y, sa largeur vers les X décroissants.
            double x = Cotes.Arrondi(xb - GenerateurFacade.RetraitPlateau - GenerateurFacade.LargeurPlateau * rang);
            Ajouter(References.PlateauAcierComplement(_w), Famille.Plateau, new Point3(x, 0, z), Axe.Y, Face.Aucune);
        }

        if (Ouverture(bout, z) is not (double debut, double fin))
        {
            return;
        }

        double bord = debut > Cotes.Tolerance ? debut : fin;
        if (bord <= Cotes.Tolerance || bord >= _w - Cotes.Tolerance)
        {
            return;
        }

        double y = Cotes.Arrondi(-bord);
        var repere = new RepereCoteTour(bout, z);
        Ajouter(References.MontantGardeCorps(_p.Modele), Famille.GardeCorps, new Point3(xHaut, y, z), Axe.Z, Face.Exterieure, repere: repere);
        double longueur = References.LongueurTube(xb - xa) ?? 1.0;
        foreach (double dz in new[] { 0.45, 0.95 })
        {
            double zt = Cotes.Arrondi(z + dz);
            Ajouter(References.Tube(longueur), Famille.GardeCorps, new Point3(xa, y, zt), Axe.X, Face.Aucune, repere: repere);
            Ajouter(References.CollierFixe, Famille.GardeCorps, new Point3(xa, y, zt), Axe.Z, Face.Aucune, repere: repere);
            Ajouter(References.CollierFixe, Famille.GardeCorps, new Point3(xb, y, zt), Axe.Z, Face.Aucune, repere: repere);
        }
    }

    /// <summary>Volée de 1 m dans les étages : traverse intermédiaire sous son haut, plateaux acier de 0,75 m jusqu'au bout.</summary>
    private void PlacerPalierVolee1m(double xBout, double xHaut, double sens, int moitie, double z)
    {
        Ajouter(References.TraverseIntermediaire(_w), Famille.Traverse, new Point3(xHaut, 0, z), Axe.Y, Face.Exterieure);
        double longueur = Cotes.Arrondi(L - LongueurVolee1m);
        double x0 = sens > 0 ? xBout : Cotes.Arrondi(xBout - longueur);
        double yBord = moitie == 0 ? 0.0 : Cotes.Arrondi(-_w / 2);
        foreach (var (_, rang) in GenerateurFacade.RepartitionPlateaux(_w / 2, GammePlateaux.Standard))
        {
            double y = Cotes.Arrondi(yBord - GenerateurFacade.RetraitPlateau - GenerateurFacade.LargeurPlateau * rang);
            Ajouter(References.PlateauAcierComplement(longueur), Famille.Plateau, new Point3(x0, y, z), Axe.X, Face.Aucune);
        }
    }

    /// <summary>Départ de 0,50 m : pas de plancher dans la tour (Laurent, 28/11/2026), seulement la maille d'entrée.</summary>
    private void PlacerPlancherDepart() => PlacerMailleEntree();

    /// <summary>Saillie de la console de la marche intermédiaire (HMCON300).</summary>
    private const double LargeurConsoleMarche = 0.30;

    /// <summary>Largeur de la maille d'entrée d'une tour à départ de 0,50 m.</summary>
    public const double LargeurMailleEntree = 0.70;

    /// <summary>
    /// Départ de 0,50 m (hauteur en xx,50 m, Laurent 28/11/2026) : maille complémentaire de 0,70 m devant le bout d'entrée,
    /// structure de 1,00 m de haut avec traverses au sol et lisses tous les 0,50 m sur les deux grands côtés, sans plinthes,
    /// 2 plateaux au niveau 0 ; le bout extérieur reste ouvert (entrée) ; marche à 0,25 m sur consoles.
    /// </summary>
    private void PlacerMailleEntree()
    {
        double e = LargeurMailleEntree;
        double xTour = XBout(_t.Entree);
        double x0 = _t.Entree == CoteTour.D ? -e : L, x1 = Cotes.Arrondi(x0 + e);
        double xLoin = _t.Entree == CoteTour.D ? x0 : x1;

        foreach (double y in new[] { 0.0, -_w })
        {
            foreach (var t in ReglePoteaux.Empiler(_p, false, gardeCorps: false, sommetVise: 1.0, departCourt: true))
            {
                Ajouter(t.Reference, t.Famille, new Point3(xLoin, y, t.Z), Axe.Z, y < 0 ? Face.Exterieure : Face.Interieure);
            }
        }

        // Grands côtés : traverse au niveau 0, lisses à 0,50 et 1,00 m.
        string barre = References.LisseOuTraverse(e);
        foreach (var (y, face) in new[] { (0.0, Face.Interieure), (-_w, Face.Exterieure) })
        {
            foreach (double z in new[] { 0.0, 0.5, 1.0 })
            {
                Ajouter(barre, z > Cotes.Tolerance ? Famille.Lisse : Famille.Traverse, new Point3(x0, y, z), Axe.X, face);
            }
        }

        // Bout extérieur : traverse au niveau 0 seulement (entrée).
        Ajouter(References.LisseOuTraverse(_w), Famille.Traverse, new Point3(xLoin, 0, 0), Axe.Y, Face.Aucune, inverse: _t.Entree == CoteTour.D);

        // Plateaux en travers au niveau 0 (première marche), longueur = largeur de la tour, sans plinthes.
        foreach (var (variante, rang) in GenerateurFacade.RepartitionPlateaux(e, _p.Plateaux))
        {
            double x = Cotes.Arrondi(x1 - GenerateurFacade.RetraitPlateau - GenerateurFacade.LargeurPlateau * rang);
            Ajouter(References.Plateau(_p.Plateaux, _w, e, variante), Famille.Plateau, new Point3(x, 0, 0), Axe.Y, Face.Aucune);
        }

        // Marche intermédiaire à mi-hauteur (Laurent, 28/11/2026) : collier rosace sur les deux poteaux du bout d'entrée,
        // console HMCON300 vers l'extérieur sur chacun, plateau de la largeur de la tour posé dessus.
        double zm = Cotes.Arrondi(_zb / 2);
        bool versMoinsX = _t.Entree == CoteTour.D;
        foreach (var (y, face) in new[] { (0.0, Face.Interieure), (-_w, Face.Exterieure) })
        {
            Ajouter(References.CollierRosace, Famille.Console, new Point3(xTour, y, zm), Axe.Z, face);
            Ajouter(References.ConsoleMarche, Famille.Console, new Point3(xTour, y, zm), Axe.X, face, rotation: versMoinsX ? 180 : 0);
        }

        double xMarche = versMoinsX ? xTour : Cotes.Arrondi(xTour + LargeurConsoleMarche);
        Ajouter(References.PlateauAcierComplement(_w), Famille.Plateau, new Point3(Cotes.Arrondi(xMarche - GenerateurFacade.RetraitPlateau), 0, zm), Axe.Y, Face.Aucune);

        _alertes.Add(Fr($"Départ à {_zb:0.00} m : maille d'entrée de {e:0.00} m au niveau 0 devant le {NomsCotesTour.Nom(_t.Entree)}, marche à {zm:0.00} m, escalier à partir de {_zb:0.00} m."));
    }

    // ------------------------------------------------------------------------------------------
    // Amarrages (tour seule)
    // ------------------------------------------------------------------------------------------

    /// <summary>Tour amarrée par le côté A : sur les deux files du côté A, tous les 4 m depuis le premier amarrage (2 m).</summary>
    private void PlacerAmarrages(ParametresGeneraux general)
    {
        double pas = general.EspacementAmarrageExtremite > Cotes.Tolerance ? general.EspacementAmarrageExtremite : 4.0;
        string tube = References.TubeAmarrage(_w + _t.DistanceBatiment + 0.10);
        for (double z = Math.Max(general.HauteurPremierAmarrage, _zb + 0.5); z <= _h + Cotes.Tolerance; z = Cotes.Arrondi(z + pas))
        {
            for (int file = 0; file < 2; file++)
            {
                double x = file == 0 ? 0.0 : L;
                _amarrages.Add(new Amarrage(file, x, z, _t.Nom));
                Ajouter(tube, Famille.Amarrage, new Point3(x, 0, z), Axe.Y, Face.Interieure);
                Ajouter(References.CollierFixe, Famille.Amarrage, new Point3(x, 0, z), Axe.Z, Face.Interieure, rotation: 90);
                Ajouter(References.CollierFixe, Famille.Amarrage, new Point3(x, -_w, z), Axe.Z, Face.Exterieure, rotation: 90);
                Ajouter(References.PitonAmarrage, Famille.Amarrage, new Point3(x, _t.DistanceBatiment, z), Axe.Y, Face.Interieure);
                Ajouter(References.ChevilleAmarrage, Famille.Amarrage, new Point3(x, _t.DistanceBatiment, z), Axe.Y, Face.Interieure);
            }
        }

        if (_amarrages.Count == 0)
        {
            _alertes.Add("Tour trop basse pour un amarrage : vérifier sa stabilité (lest ou stabilisateurs).");
        }
    }

    // ------------------------------------------------------------------------------------------

    private void Ajouter(string reference, Famille famille, Point3 origine, Axe axe, Face face = Face.Aucune, bool inverse = false, double rotation = 0, RepereCoteTour? repere = null) =>
        _el.Add(new ElementPlace(reference, famille, origine, axe, face, 0, inverse, 0, rotation) { CoteTour = repere });

    private static string Fr(FormattableString texte) => texte.ToString(CultureNombres.Documents);
}
