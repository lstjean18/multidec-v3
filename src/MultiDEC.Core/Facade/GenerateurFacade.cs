using System;
using System.Collections.Generic;
using System.Linq;
using MultiDEC.Core.Catalogue;
using MultiDEC.Core.Facade.Regles;
using MultiDEC.Core.Geometrie;

namespace MultiDEC.Core.Facade;

/// <summary>Résultat de la génération : éléments positionnés, amarrages et alertes.</summary>
public sealed class StructureFacade
{
    internal StructureFacade(IReadOnlyList<ElementPlace> elements, BilanAmarrages amarrages, IReadOnlyList<string> alertes)
    {
        Elements = elements;
        Amarrages = amarrages;
        Alertes = alertes;
    }

    public IReadOnlyList<ElementPlace> Elements { get; }

    public BilanAmarrages Amarrages { get; }

    /// <summary>Messages à montrer à l'utilisateur (console inconnue, note de calcul, surface d'ancrage...).</summary>
    public IReadOnlyList<string> Alertes { get; }

    /// <summary>
    /// Même structure sans les éléments supprimés à la main dans le dessin (comparaison par référence d'objet :
    /// deux pièces identiques au même endroit restent distinctes).
    /// </summary>
    public StructureFacade SansElements(IEnumerable<ElementPlace> retires)
    {
        var aRetirer = new List<ElementPlace>(retires);
        var restants = Elements.Where(e => !aRetirer.Any(r => ReferenceEquals(r, e))).ToList();
        return new StructureFacade(restants, Amarrages, Alertes);
    }
}

/// <summary>
/// Moteur façade v3 : transforme un <see cref="FaceEchafaudage"/> en liste d'éléments positionnés.
/// Port structuré de Facade.vb (MultiRETO / MultiSystem / PlacePieds / PlacePoteaux / PlaceMailles /
/// PlaceGC / PlaceConsole / PlacePlateaux / PlaceAcces / PlaceDiago / Placeabout), sans dépendance
/// à l'interface ni à VectorDraw, plus les nouveautés v3 (consoles par niveau et par maille, diagonales choisies par maille, diagonales intérieures,
/// amarrages placés).
/// Conventions (alignées sur les blocs de Blocs-EM-3D.dxf, dessinés dans le plan XY, hauteur selon +Y) :
/// - élément d'axe X : origine à l'extrémité gauche (X mini), ou à droite si <c>Inverse</c> (bloc symétrisé) ;
/// - plateaux et trappes : origine sur le bord côté façade, la largeur part vers l'extérieur (Y négatifs) ;
/// - élément d'axe Y : origine côté file intérieure, part vers l'extérieur ; les consoles partent vers la façade.
/// Le rendu (MultiDEC.Rendering.VectorDraw) traduit ces conventions en matrices d'insertion.
/// </summary>
public sealed partial class GenerateurFacade
{
    /// <summary>Demi-diamètre de tube : décalage des diagonales et lisses posées contre les poteaux.</summary>
    public const double DemiTube = 0.0483;

    /// <summary>Retrait du premier plateau par rapport à la file.</summary>
    public const double RetraitPlateau = 0.025;

    /// <summary>Largeur nominale d'un plateau acier.</summary>
    public const double LargeurPlateau = 0.30;

    private readonly FaceEchafaudage _projet;
    private readonly ParametresFacade _p;
    private readonly List<ElementPlace> _el = new List<ElementPlace>();
    private readonly List<string> _alertes = new List<string>();
    private readonly IReadOnlyList<double> _x;
    private readonly double _l;
    private readonly double _h;

    /// <summary>Mailles qui ont déjà leurs lisses de pied (une seule paire par maille).</summary>
    private readonly HashSet<int> _lissesPied = new HashSet<int>();

    /// <summary>Consoles reconnues par niveau (null = pas de console ou console inconnue).</summary>
    private readonly Dictionary<Niveau, ConsoleInfo?> _consolesNiveau = new Dictionary<Niveau, ConsoleInfo?>();

    private Niveau? _niveauSommet;

    /// <summary>Diagonales horizontales déjà posées par niveau (pour alterner le sens).</summary>
    private readonly Dictionary<Niveau, int> _diagosH = new Dictionary<Niveau, int>();

    private GenerateurFacade(FaceEchafaudage projet)
    {
        _projet = projet;
        _p = projet.Parametres;
        if (_p.TrappesAcier && (_p.SecuriteExterieure == TypeSecurite.GardeCorpsMds2 || _p.SecuriteInterieure == TypeSecurite.GardeCorpsMds2))
        {
            // Trappes acier incompatibles avec les garde-corps MDS : lisses sur cette face (v2).
            _p = _p.Copie();
            if (_p.SecuriteExterieure == TypeSecurite.GardeCorpsMds2)
            {
                _p.SecuriteExterieure = TypeSecurite.Lisses;
            }

            if (_p.SecuriteInterieure == TypeSecurite.GardeCorpsMds2)
            {
                _p.SecuriteInterieure = TypeSecurite.Lisses;
            }

            _alertes.Add("Trappes et plateaux acier : garde-corps MDS remplacés par des lisses (incompatibles, comme dans la v2).");
        }

        _x = projet.AbscissesFiles();
        _l = _p.LargeurMaille;
        _h = _p.HauteurDernierPlancher;
    }

    private int DerniereFile => _x.Count - 1;

    // ---- Structure volumique : côtés communs à deux modules (Laurent, 24/11/2026) ------------------------
    // Un module peut être plus long que son voisin : seul le tronçon couvert par le voisin est commun ;
    // au-delà, le côté donne dans le vide et a son garde-corps (et ses diagonales).

    /// <summary>Côté façade de la maille partagé avec le module précédent (M1 pour M2…).</summary>
    private bool InterieurCommun(int maille) =>
        _p.ModuleVolumique > 0 && _x[maille + 1] <= _p.LongueurModulePrecedent + Cotes.Tolerance;

    /// <summary>Côté extérieur de la maille partagé avec le module suivant.</summary>
    private bool ExterieurCommun(int maille) =>
        _p.ModuleVolumique is int module && module < _p.NombreModules - 1 && _x[maille + 1] <= _p.LongueurModuleSuivant + Cotes.Tolerance;

    /// <summary>Protection côté façade d'une maille : sur un module au-delà de M1, ce côté donne dans le vide hors tronçon commun.</summary>
    private TypeSecurite SecuriteInterieure(int maille) =>
        InterieurCommun(maille) ? TypeSecurite.Sans : _p.ModuleVolumique > 0 ? _p.SecuriteExterieure : _p.SecuriteInterieure;

    private TypePlinthe PlintheInterieure(int maille) =>
        InterieurCommun(maille) ? TypePlinthe.Sans : _p.ModuleVolumique > 0 ? _p.PlintheExterieure : _p.PlintheInterieure;

    private TypeSecurite SecuriteExterieure(int maille) => ExterieurCommun(maille) ? TypeSecurite.Sans : _p.SecuriteExterieure;

    private TypePlinthe PlintheExterieure(int maille) => ExterieurCommun(maille) ? TypePlinthe.Sans : _p.PlintheExterieure;

    /// <summary>Mailles qui touchent une file (index).</summary>
    private IEnumerable<int> IndexVoisines(int file)
    {
        if (file > 0)
        {
            yield return file - 1;
        }

        if (file < _projet.Mailles.Count)
        {
            yield return file;
        }
    }

    private double LongueurTotale => _x[DerniereFile];

    public static StructureFacade Generer(FaceEchafaudage projet)
    {
        if (projet.Mailles.Count == 0)
        {
            throw new InvalidOperationException("La grille des mailles est vide.");
        }

        var g = new GenerateurFacade(projet);
        return g.Executer();
    }

    private StructureFacade Executer()
    {
        var niveaux = _projet.Niveaux.OrderBy(n => n.Hauteur).ToList();
        _niveauSommet = niveaux.LastOrDefault();
        foreach (var n in niveaux.Where(n => n.AConsole))
        {
            ConsoleInfo? info = null;
            if (!CatalogueConsoles.TryGet(_p.Modele, n.Console!, out info))
            {
                _alertes.Add(FormattableString.Invariant($"Niveau {n.Hauteur:0.00} m : console {n.Console} inconnue pour le modèle {_p.Modele}, ignorée."));
                info = null;
            }

            _consolesNiveau[n] = info;
        }

        ValiderPoutres();
        ValiderHauteurs();
        for (int i = 0; i < _projet.Mailles.Count; i++)
        {
            PlacerMaille(i, NiveauxMaille(_projet.Mailles[i], niveaux));
        }

        PlacerSousMailles(niveaux);
        PlacerStabilisations(niveaux);
        PlacerDiagonales();
        PlacerPoutres(niveaux);
        PlacerAbouts(niveaux);
        PlacerPareGravois(niveaux);

        // Poteaux en dernier : chacun monte jusqu'à la plus haute protection qui s'y accroche (Laurent, 24/11/2026 :
        // un garde-corps a forcément un poteau de chaque côté ; sans garde-corps, le poteau s'arrête au plancher).
        PlacerFiles(niveaux);
        var bilan = PlacerAmarrages();

        if (bilan.NoteDeCalculObligatoire)
        {
            _alertes.Add(FormattableString.Invariant(
                $"Hauteur {_projet.HauteurTotale:0.00} m > {_p.SeuilNoteDeCalcul:0} m : note de calcul obligatoire."));
        }
        else if (_p.Bache)
        {
            _alertes.Add("Échafaudage bâché : note de calcul recommandée.");
        }

        if (!bilan.SurfaceRespectee)
        {
            _alertes.Add($"Amarrages : {bilan.Amarrages.Count} placés pour {bilan.MinimumParSurface} requis par la surface ({bilan.Surface:0.#} m²).");
        }

        if (CatalogueSocles.De(_p.Socle).AvecRoues && _projet.Mailles.Count > 1)
        {
            _alertes.Add("Socles à roue : seulement sur une structure à 4 pieds (une maille), comme dans la v2.");
        }

        if (_p.SecuriteExterieure == TypeSecurite.Sans && _p.SecuriteInterieure == TypeSecurite.Sans && _p.ModuleVolumique == null)
        {
            _alertes.Add("Aucun garde-corps : pas de diagonale verticale posée, contreventement à prévoir.");
        }

        if (RegleDiagonales.RenforcementConseille(niveaux) && !_p.DiagonalesRenforcees)
        {
            _alertes.Add("Beaucoup de niveaux « accès seul » : option « diagonales 1 maille sur 2 » conseillée.");
        }

        return new StructureFacade(_el, bilan, _alertes);
    }

    // ------------------------------------------------------------------------------------------
    // Files de poteaux : pieds, poteaux, traverses
    // ------------------------------------------------------------------------------------------

    private void PlacerFiles(List<Niveau> niveaux)
    {
        for (int f = 0; f <= DerniereFile; f++)
        {
            bool extremite = f == 0 || f == DerniereFile;
            double x = _x[f];

            // Colonne « Ht max » : la file monte (ou s'arrête) au dernier plancher de la plus haute maille voisine.
            double hFile = HauteurFile(f);

            // File du milieu d'une poutre de franchissement : poteaux posés sur la poutre, rien dessous.
            double? zPoutre = PoutreSousFile(f);

            // Chaque poteau monte au moins au plancher de la file, et jusqu'à la plus haute protection
            // (garde-corps, lisse, about, diagonale) qui s'y accroche. Départ 3 m côté extérieur si option.
            double sommetExt = Math.Max(hFile, SommetProtections(x, -_l));
            Empiler(x, -_l, Face.Exterieure, _p.Depart3mExterieur, sommetExt > hFile + Cotes.Tolerance, sommetExt, zPoutre);

            double sommetInt = Math.Max(hFile, SommetProtections(x, 0));
            Empiler(x, 0, Face.Interieure, _p.Depart3mExterieur && extremite, sommetInt > hFile + Cotes.Tolerance, sommetInt, zPoutre);

            // Traverses à chaque niveau (renforcées aux planchers complets si demandé).
            foreach (var n in niveaux.Where(n => n.Hauteur <= hFile + Cotes.Tolerance).Concat(NiveauxPignon(hFile, TypePlancher.Complet))
                                     .Where(n => zPoutre == null || n.Hauteur >= zPoutre.Value - Cotes.Tolerance))
            {
                var type = n.Hauteur > Cotes.Tolerance && !n.EstAccesSeul ? _p.Traverses : TypeTraverse.Lisse;
                Ajouter(References.LisseOuTraverse(_l, type), Famille.Traverse, new Point3(x, 0, n.Hauteur), Axe.Y);
            }

            if (!niveaux.Any(n => Cotes.Egal(n.Hauteur, 0)) && zPoutre == null)
            {
                Ajouter(References.LisseOuTraverse(_l), Famille.Traverse, new Point3(x, 0, 0), Axe.Y);
            }

            if (_p.GardeCorpsCouvreur && (extremite || !Cotes.Egal(hFile, _h)))
            {
                Ajouter(References.LisseOuTraverse(_l), Famille.Traverse, new Point3(x, 0, hFile + 1.5), Axe.Y);
            }
        }
    }

    /// <summary>
    /// Planchers du pignon d'une maille d'accès (v2 : niveaux ajoutés tous les 2 m au-dessus du plancher commun),
    /// traités comme des niveaux « accès seul » : trappe, échelle et abouts des deux côtés.
    /// </summary>
    private IEnumerable<Niveau> NiveauxPignon(MailleDefinition m) =>
        NiveauxPignon(m.HauteurMax(_h), m.Type == TypeMaille.Acces ? TypePlancher.AccesSeul : TypePlancher.Complet);

    private IEnumerable<Niveau> NiveauxPignon(double hauteurMax, TypePlancher type)
    {
        for (double z = _h + 2.0; z <= hauteurMax + Cotes.Tolerance; z = Cotes.Arrondi(z + 2.0))
        {
            yield return new Niveau(Cotes.Arrondi(z), type);
        }
    }

    /// <summary>Niveaux d'une maille : ceux de la face jusqu'à sa hauteur (colonne « Ht max »), puis ceux qui la dépassent.</summary>
    private List<Niveau> NiveauxMaille(MailleDefinition m, List<Niveau> niveaux)
    {
        double hm = m.HauteurMax(_h);
        return niveaux.Where(n => n.Hauteur <= hm + Cotes.Tolerance).Concat(NiveauxPignon(m)).ToList();
    }

    /// <summary>Colonne « Ht max » : l'écart au dernier plancher de la face doit être un multiple de 2 m.</summary>
    private void ValiderHauteurs()
    {
        for (int i = 0; i < _projet.Mailles.Count; i++)
        {
            var m = _projet.Mailles[i];
            if (!m.EcartValide)
            {
                _alertes.Add(FormattableString.Invariant(
                    $"Maille {i + 1} : hauteur {_h + m.EcartHauteur:0.00} m ignorée, l'écart avec le dernier plancher ({_h:0.00} m) doit être un multiple de 2 m."));
            }
            else if (_h + m.EcartHauteur <= Cotes.Tolerance)
            {
                _alertes.Add(FormattableString.Invariant($"Maille {i + 1} : hauteur {_h + m.EcartHauteur:0.00} m ignorée (sous le premier plancher)."));
            }
        }
    }

    /// <summary>Maille voisine (index) arrêtée sous la cote z, ou absente : son côté donne dans le vide à ce niveau.</summary>
    private bool VoisineSous(int indexVoisine, double z) =>
        indexVoisine >= 0 && indexVoisine < _projet.Mailles.Count && _projet.Mailles[indexVoisine].HauteurMax(_h) < z - Cotes.Tolerance;

    /// <summary>Dernier plancher atteint à une file (pignon d'une maille voisine compris).</summary>
    private double HauteurFile(int file) => MaillesVoisines(file).Select(m => m.HauteurMax(_h)).DefaultIfEmpty(_h).Max();

    /// <summary>
    /// Plus haute cote atteinte par les protections déjà posées qui s'accrochent au poteau (x, y) : garde-corps
    /// (cadre MDS = plancher + 1 m), lisses et plinthes le long d'une maille dont c'est une extrémité, abouts
    /// et lisses en travers partant de ce poteau ou y arrivant, diagonales verticales. 0 si aucune.
    /// </summary>
    private double SommetProtections(double x, double y)
    {
        const double ecart = DemiTube + Cotes.Tolerance;
        double sommet = 0;
        foreach (var e in _el)
        {
            double haut;
            if (e.Axe == Axe.X && (e.Famille == Famille.GardeCorps || e.Famille == Famille.Lisse || e.Famille == Famille.Plinthe || e.Famille == Famille.DiagonaleVerticale))
            {
                int i = e.NumeroMaille - 1;
                if (i < 0 || i >= _projet.Mailles.Count || Math.Abs(e.Origine.Y - y) > ecart
                    || !(Cotes.Egal(x, _x[i]) || Cotes.Egal(x, _x[i + 1])))
                {
                    continue;
                }

                haut = e.Famille == Famille.DiagonaleVerticale ? e.Origine.Z + 2.0 : e.Origine.Z;
            }
            else if (e.Axe == Axe.Y && (e.Famille == Famille.GardeCorps || e.Famille == Famille.Traverse || e.Famille == Famille.Plinthe))
            {
                // About : de son origine vers l'extérieur sur la largeur de la maille ; about de console : de son bout à la façade.
                double yFin = e.Origine.Y > Cotes.Tolerance ? 0 : e.Origine.Y - _l;
                if (!Cotes.Egal(e.Origine.X, x) || !(Cotes.Egal(e.Origine.Y, y) || Cotes.Egal(yFin, y)))
                {
                    continue;
                }

                haut = e.Origine.Z;
            }
            else
            {
                continue;
            }

            if (e.Famille == Famille.GardeCorps && e.Reference.StartsWith("HMGCSE", StringComparison.OrdinalIgnoreCase))
            {
                haut += 1.0; // cadre MDS posé au plancher, lisse haute à 1 m
            }

            sommet = Math.Max(sommet, haut);
        }

        return Cotes.Arrondi(sommet);
    }

    private IEnumerable<MailleDefinition> MaillesVoisines(int file)
    {
        if (file > 0)
        {
            yield return _projet.Mailles[file - 1];
        }

        if (file < _projet.Mailles.Count)
        {
            yield return _projet.Mailles[file];
        }
    }

    /// <summary>Console posée sur une maille à un niveau (case « ConXm » cochée, console connue), ou null.</summary>
    private ConsoleInfo? ConsoleDe(MailleDefinition m, Niveau n) =>
        _projet.ConsoleDe(m, n) != null && _consolesNiveau.TryGetValue(n, out var c) ? c : null;

    private ConsoleInfo? ConsoleDe(int indexMaille, Niveau n) =>
        indexMaille >= 0 && indexMaille < _projet.Mailles.Count ? ConsoleDe(_projet.Mailles[indexMaille], n) : null;

    private ConsoleInfo? ConsoleSommet(MailleDefinition m) => _niveauSommet == null ? null : ConsoleDe(m, _niveauSommet);

    private void Empiler(double x, double y, Face face, bool depart3m, bool gardeCorps, double? sommet = null, double? surPoutre = null)
    {
        var troncons = surPoutre is double zp
            ? ReglePoteaux.EmpilerDepuis(_p, zp, sommet ?? _h + (gardeCorps ? 1.0 : 0.0) + (gardeCorps && _p.GardeCorpsCouvreur ? 0.5 : 0.0))
            : ReglePoteaux.Empiler(_p, depart3m, gardeCorps, sommet);
        foreach (var t in troncons)
        {
            Ajouter(t.Reference, t.Famille, new Point3(x, y, t.Z), Axe.Z, face);
        }
    }

    // ------------------------------------------------------------------------------------------
    // Mailles : lisses, garde-corps, plinthes, plateaux, accès, consoles
    // ------------------------------------------------------------------------------------------

    private void PlacerMaille(int index, List<Niveau> niveaux)
    {
        var m = _projet.Mailles[index];
        int num = index + 1;
        double x0 = _x[index];
        double lm = m.Longueur;
        string lisse = References.LisseOuTraverse(lm);
        bool inverseTrappe = false;

        // Lisses de pied : option générale, et toujours sur la maille de jonction (carré d'angle) et la maille
        // escalier, qui ont ainsi des lisses de pied sur leurs 4 côtés avec les traverses des files (Laurent, 24/11/2026).
        double? zPoutre = PoutreSous(index);
        if ((_p.LissesEnPied || CarreDAngle(index) || (m.EstEscalier && EscalierPossible(m))) && zPoutre == null)
        {
            LissesDePied(num, x0, lm);
        }

        foreach (var n in niveaux)
        {
            double z = n.Hauteur;
            if (SousPoutre(index, z))
            {
                continue; // passage sous une poutre de franchissement
            }

            bool surPoutre = zPoutre is double zp0 && Cotes.Egal(z, zp0);
            bool sansPlancher = n.EstAccesSeul && m.EstTravail;
            var console = ConsoleDe(m, n);

            if (z > Cotes.Tolerance && !surPoutre) // sur une poutre, sa membrure haute fait la lisse
            {
                Ajouter(lisse, Famille.Lisse, new Point3(x0, 0, z), Axe.X, Face.Interieure, num);

                // Niveau « accès seul » : lisse des deux côtés, façade et extérieur (Laurent, 23/11/2026).
                if (n.EstAccesSeul)
                {
                    Ajouter(lisse, Famille.Lisse, new Point3(x0, -_l, z), Axe.X, Face.Exterieure, num);
                }
            }

            if (z >= 0.5 - Cotes.Tolerance && !sansPlancher)
            {
                var plintheExt = n.EstAccesSeul ? TypePlinthe.Sans : PlintheExterieure(index);
                if (CarreDAngleInverse(index))
                {
                    // Structure inversée : le côté extérieur du carré d'angle donne sur le bout de la face précédente (passage).
                }
                else if ((m.Type == TypeMaille.AccesRapporte || (m.EstEscalier && EscalierPossible(m))) && z <= HauteurSousMaille(m, _projet.Niveaux) + Cotes.Tolerance)
                {
                    // Garde-corps extérieur reporté sur la file de l'accès rapporté ; lisse de plancher ici (v2).
                    Ajouter(lisse, Famille.Lisse, new Point3(x0, -_l, z), Axe.X, Face.Exterieure, num);
                }
                else if (!SousMailleDesservie(m, n))
                {
                    // Maille plus haute que sa sous-maille rapportée : garde-corps extérieur des paramètres au-dessus (Laurent, 24/11/2026).
                    // Appro / recette desservis : la maille s'ouvre sur la sous-maille, pas de garde-corps extérieur.
                    // Sur une poutre : garde-corps en lisses (v2 remplace les cadres MDS).
                    PlacerGardeCorps(surPoutre ? GardeCorpsSurPoutre(SecuriteExterieure(index)) : SecuriteExterieure(index), plintheExt, x0, -_l, z, lm, Face.Exterieure, num);
                }

                // v3 : même règle côté intérieur (l'ancienne version posait le GC intérieur même sans plancher).
                // Carré d'angle : son côté intérieur donne sur le bout de la face précédente (passage), pas de garde-corps.
                if (console == null && !(index == 0 && _p.CarreDAngleEnTete && !_p.CarreDAngleInverse))
                {
                    PlacerGardeCorps(surPoutre ? GardeCorpsSurPoutre(SecuriteInterieure(index)) : SecuriteInterieure(index), PlintheInterieure(index), x0, 0, z, lm, Face.Interieure, num);
                }
            }

            // Tout about qui donne dans le vide a un garde-corps (Laurent, 24/11/2026) : ici, maille voisine arrêtée
            // plus bas (colonne « Ht max »). Les niveaux « accès seul » d'une maille d'accès ont déjà leurs abouts.
            bool plancher = m.EstTravail ? n.Type == TypePlancher.Complet && !SansPlancherDamier(index) : !n.EstAccesSeul;
            if (z > Cotes.Tolerance && plancher && !surPoutre)
            {
                if (VoisineSous(index - 1, z))
                {
                    PlacerAbout(x0, z, _l, gauche: true, console: false, num);
                }

                if (VoisineSous(index + 1, z))
                {
                    PlacerAbout(x0 + lm, z, _l, gauche: false, console: false, num);
                }
            }

            if (m.EstTravail)
            {
                if (n.Type == TypePlancher.Complet && z > Cotes.Tolerance && !SansPlancherDamier(index))
                {
                    if (AccesParTrappe(index, z))
                    {
                        // Maille plus haute que ses voisines (Laurent, 24/11/2026) : plateau d'accès pour y monter.
                        PlacerPlancherTrappe(x0, lm, 0, _l, z, num, inverseTrappe);
                        inverseTrappe = !inverseTrappe;
                    }
                    else
                    {
                        PlacerPlateaux(x0, 0, z, _l, lm, num);
                    }
                }

                if (n.DiagoH && z > Cotes.Tolerance)
                {
                    // Sens alterné d'une maille de travail à l'autre ; inversée = origine à droite.
                    _diagosH.TryGetValue(n, out int rang);
                    _diagosH[n] = rang + 1;
                    bool inverse = rang % 2 == 1;
                    Ajouter(References.DiagonaleHorizontale(_l, lm), Famille.DiagonaleHorizontale,
                        new Point3(inverse ? x0 + lm : x0, 0, z), Axe.X, Face.Aucune, num, inverse);
                }
            }
            else
            {
                PlacerAcces(m, num, x0, n, ref inverseTrappe);
            }

            if (console != null)
            {
                PlacerConsole(console, index, x0, lm, n, num);
            }
        }

        if (_p.GardeCorpsCouvreur)
        {
            double hm = m.HauteurMax(_h);
            if (!ExterieurCommun(index) && !CarreDAngleInverse(index))
            {
                Ajouter(lisse, Famille.Lisse, new Point3(x0, -_l, hm + 1.5), Axe.X, Face.Exterieure, num);
            }

            if (SecuriteInterieure(index) != TypeSecurite.Sans && (ConsoleSommet(m) == null || hm > _h + Cotes.Tolerance))
            {
                Ajouter(lisse, Famille.Lisse, new Point3(x0, 0, hm + 1.5), Axe.X, Face.Interieure, num);
            }
        }
    }

    /// <summary>
    /// Plancher d'une maille de travail plus haute que ses voisines (colonne « Ht max ») : aucune voisine n'y arrive,
    /// il faut un plateau d'accès pour y monter depuis le plancher du dessous.
    /// </summary>
    private bool AccesParTrappe(int index, double z)
    {
        var voisines = new[] { index - 1, index + 1 }.Where(i => i >= 0 && i < _projet.Mailles.Count).ToList();
        return voisines.Count > 0 && voisines.All(i => VoisineSous(i, z)) && z >= 2.0 - Cotes.Tolerance;
    }

    /// <summary>Carré d'angle d'une structure inversée : son côté extérieur est le passage vers la face précédente.</summary>
    private bool CarreDAngleInverse(int index) => index == 0 && _p.CarreDAngleEnTete && _p.CarreDAngleInverse;

    /// <summary>Maille de jonction (carré d'angle) en tête de face.</summary>
    private bool CarreDAngle(int index) => index == 0 && _p.CarreDAngleEnTete;

    /// <summary>Planchers en damier (volumique, v2 IsMailleDamier) : pas de plancher une maille sur deux, décalé par module.</summary>
    private bool SansPlancherDamier(int indexMaille) =>
        _p.PlanchersDamier && _p.ModuleVolumique is int module && (indexMaille + module) % 2 == 1;

    private static TypeSecurite GardeCorpsSurPoutre(TypeSecurite type) => type == TypeSecurite.GardeCorpsMds2 ? TypeSecurite.Lisses : type;

    private void PlacerGardeCorps(TypeSecurite type, TypePlinthe plinthe, double x0, double y, double z, double lm, Face face, int num)
    {
        switch (type)
        {
            case TypeSecurite.Sans:
                return;

            case TypeSecurite.Lisses:
                string lisse = References.LisseOuTraverse(lm);
                Ajouter(lisse, Famille.Lisse, new Point3(x0, y, z + 0.5), Axe.X, face, num);
                Ajouter(lisse, Famille.Lisse, new Point3(x0, y, z + 1.0), Axe.X, face, num);
                break;

            case TypeSecurite.GardeCorpsMds2:
                Ajouter(References.GardeCorps(type, lm)!, Famille.GardeCorps, new Point3(x0, y, z), Axe.X, face, num);
                if (lm <= 1.25)
                {
                    plinthe = TypePlinthe.Sans; // le cadre MDS 2 court intègre la protection basse
                }

                break;

            default:
                Ajouter(References.GardeCorps(type, lm)!, Famille.GardeCorps, new Point3(x0, y, z), Axe.X, face, num);
                break;
        }

        var refPlinthe = References.Plinthe(plinthe, lm);
        if (refPlinthe != null)
        {
            Ajouter(refPlinthe, Famille.Plinthe, new Point3(x0, y, z), Axe.X, face, num);
        }
    }

    /// <summary>Plateaux couvrant une largeur, posés à partir du bord <paramref name="yBord"/> vers les Y négatifs.</summary>
    private void PlacerPlateaux(double x0, double yBord, double z, double largeur, double lm, int num, int premierRang = 0, int dernierRang = int.MaxValue)
    {
        // Plateaux au niveau 0 : lisses des deux côtés (Laurent, 23/11/2026).
        if (Math.Abs(z) <= Cotes.Tolerance)
        {
            LissesDePied(num, x0, lm);
        }

        foreach (var (variante, rang) in RepartitionPlateaux(largeur, _p.Plateaux))
        {
            if (rang < premierRang || rang > dernierRang)
            {
                continue;
            }

            string reference = References.Plateau(_p.Plateaux, lm, largeur, variante, _p.QualitePlateaux);
            double y = Cotes.Arrondi(yBord - RetraitPlateau - LargeurPlateau * rang);
            Ajouter(reference, Famille.Plateau, new Point3(x0, y, z), Axe.X, Face.Aucune, num);
        }
    }

    /// <summary>Plateaux nécessaires pour une largeur : (variante 1 = courant / 2 = complément, rang depuis le bord).</summary>
    internal static IEnumerable<(int Variante, int Rang)> RepartitionPlateaux(double largeur, GammePlateaux gamme)
    {
        if (largeur <= 0.30 + Cotes.Tolerance)
        {
            yield return (2, 0);
        }
        else if (largeur <= 0.40 + Cotes.Tolerance)
        {
            yield return (1, 0);
        }
        else if (largeur <= 0.75 + Cotes.Tolerance)
        {
            yield return (1, 0);
            yield return (1, 1);
        }
        else
        {
            int nb = (int)Math.Floor((largeur + Cotes.Tolerance) / LargeurPlateau);
            for (int i = 0; i < nb; i++)
            {
                yield return (1, i);
            }
        }
    }

    private void PlacerAcces(MailleDefinition m, int num, double x0, Niveau n, ref bool inverse)
    {
        double z = n.Hauteur;
        double lm = m.Longueur;

        if (n.EstAccesSeul && z > Cotes.Tolerance)
        {
            // Maille d'accès isolée : abouts des deux côtés (portillon à gauche sous 2 m, v2).
            if (PortillonNecessaire(n))
            {
                PlacerPortillon(x0, z, num);
            }
            else
            {
                PlacerAbout(x0, z, _l, gauche: true, console: false, num);
            }

            PlacerAbout(x0 + lm, z, _l, gauche: false, console: false, num);
        }

        if (z <= Cotes.Tolerance)
        {
            // Option « lisse GC accès 1,50 m au niveau 0 » (v2) : lisse à 1,50 m au départ de l'échelle,
            // du côté où il y a un garde-corps.
            if (_p.LisseGcAccesNiveau0)
            {
                string lisseGc = References.LisseOuTraverse(lm);
                if (_p.SecuriteExterieure != TypeSecurite.Sans)
                {
                    Ajouter(lisseGc, Famille.Lisse, new Point3(x0, -_l, 1.5), Axe.X, Face.Exterieure, num);
                }

                if (_p.SecuriteInterieure != TypeSecurite.Sans)
                {
                    Ajouter(lisseGc, Famille.Lisse, new Point3(x0, 0, 1.5), Axe.X, Face.Interieure, num);
                }
            }

            return;
        }

        PlacerTremie(x0, lm, 0, _l, z, num, ref inverse, secuCoteBord: _p.SecuriteInterieure != TypeSecurite.Sans);
    }

    /// <summary>
    /// Plancher d'une colonne d'accès de largeur <paramref name="largeur"/> à partir du bord <paramref name="yBord"/> :
    /// plateaux sous 2 m ; au-dessus, plateau à trappe (ou trappe acier) + échelle en sens alterné, plateaux de départ
    /// au pied de la première échelle, lisse de sécurité au droit de la trémie.
    /// </summary>
    private void PlacerTremie(double x0, double lm, double yBord, double largeur, double z, int num, ref bool inverse, bool secuCoteBord)
    {
        double yExt = Cotes.Arrondi(yBord - largeur);
        if (z < 2.0 - Cotes.Tolerance)
        {
            PlacerPlateaux(x0, yBord, z, largeur, lm, num);
            return;
        }

        PlacerPlancherTrappe(x0, lm, yBord, largeur, z, num, inverse);

        bool premierAcces = Cotes.Egal(z, 2.0) || Cotes.Egal(z, 3.0);
        if (premierAcces)
        {
            // Plateaux de départ au pied de l'échelle.
            PlacerPlateaux(x0, yBord, z - 2.0, largeur, lm, num, premierRang: 0, dernierRang: 1);
        }
        else
        {
            // Lisse de sécurité au droit de la trémie (1,50 m au-dessus des plateaux) : lisse ordinaire
            // dans le plan des poteaux, montée sur leurs rosaces (tous les 0,50 m), sans collier (Laurent, 23/11/2026).
            string secu = References.LisseOuTraverse(lm);
            Ajouter(secu, Famille.Lisse, new Point3(x0, yExt, z - 0.5), Axe.X, Face.Exterieure, num);

            if (secuCoteBord)
            {
                Ajouter(secu, Famille.Lisse, new Point3(x0, yBord, z - 0.5), Axe.X, Face.Interieure, num);
            }
        }

        inverse = !inverse;
    }

    /// <summary>
    /// Plancher avec trappe : plateau à trappe (bande intérieure de 0,60 m) + échelle (seulement jusqu'à 2,00 m de maille),
    /// puis plateaux de complément. En sens inversé, l'origine est à droite de la maille et le bloc est symétrisé.
    /// </summary>
    private void PlacerPlancherTrappe(double x0, double lm, double yBord, double largeur, double z, int num, bool inverse)
    {
        // Plateau à trappe (bande intérieure de 0,60 m) + échelle (seulement jusqu'à 2,00 m de maille),
        // puis plateaux de complément. Sens alterné d'un niveau à l'autre : en sens inversé, l'origine
        // est à droite de la maille et le bloc est symétrisé.
        double xTrappe = inverse ? x0 + lm : x0;
        if (_p.TrappesAcier && lm >= 2.0 - Cotes.Tolerance)
        {
            // Trappes et plateaux acier (v2) : trappe de 1,00 m + échelle côté départ, traverse intermédiaire
            // à 1,00 m, plateaux sur le reste de la maille.
            const double lt = 1.0;
            double xTip = inverse ? x0 + lm - lt : x0 + lt;
            Ajouter(References.TraverseIntermediaire(largeur), Famille.Traverse, new Point3(xTip, yBord, z), Axe.Y, Face.Aucune, num);
            Ajouter(References.TrappeAcier(largeur), Famille.Trappe, new Point3(xTrappe, yBord - RetraitPlateau, z), Axe.X, Face.Aucune, num, inverse);
            Ajouter(References.Echelle, Famille.Echelle, new Point3(xTrappe, yBord - RetraitPlateau, z), Axe.X, Face.Aucune, num, inverse);
            PlacerPlateaux(inverse ? x0 + lm - lt : x0, yBord, z, largeur, lt, num, premierRang: 2);
            PlacerPlateaux(inverse ? x0 : x0 + lt, yBord, z, largeur, Cotes.Arrondi(lm - lt), num);
        }
        else
        {
            Ajouter(References.PlateauTrappe(lm, largeur), Famille.Trappe, new Point3(xTrappe, yBord - RetraitPlateau, z), Axe.X, Face.Aucune, num, inverse);
            if (lm <= 2.0 + Cotes.Tolerance)
            {
                Ajouter(References.Echelle, Famille.Echelle, new Point3(xTrappe, yBord - RetraitPlateau, z), Axe.X, Face.Aucune, num, inverse);
            }

            PlacerPlateaux(x0, yBord, z, largeur, lm, num, premierRang: 2);
        }
    }

    // ------------------------------------------------------------------------------------------
    // Pare-gravois (v2 : clJonction + AjouteLissesPargra + PlacePareGravoisToles)
    // ------------------------------------------------------------------------------------------

    /// <summary>Inclinaison des consoles de pare-gravois : 35° (lisses), tôles à 55° de l'horizontale.</summary>
    public const double AnglePareGravois = 35.0;

    /// <summary>Pas de pose des tôles (largeur utile 0,80 m).</summary>
    public const double PasTolesPareGravois = 0.80;

    /// <summary>Largeur d'une tôle HMTOG200 le long de la façade (relevée dans la bibliothèque DWG).</summary>
    public const double LargeurTolePareGravois = 0.90;

    private void PlacerPareGravois(List<Niveau> niveaux)
    {
        foreach (var n in niveaux.Where(n => n.PareGravois && n.Hauteur > Cotes.Tolerance))
        {
            double z = n.Hauteur;

            // Une console HMPARGRA à chaque file, sur la file extérieure, tournée vers l'extérieur.
            for (int f = 0; f <= DerniereFile; f++)
            {
                if (PoutreSousFile(f) is double zp && z < zp - Cotes.Tolerance)
                {
                    continue;
                }

                Ajouter(References.ConsolePareGravois, Famille.PareGravois, new Point3(_x[f], -_l, z), Axe.Y, Face.Exterieure);
            }

            // Trois lisses par maille le long des consoles (cotes v2 : 0,27 / 1,20 / 2,15 m au-dessus du plancher,
            // 0,10 m devant la file, le tout tourné de 35° autour de la file extérieure).
            foreach (var (dy, dz) in new[] { (-0.10, 0.27), (-0.10, 1.20), (-0.10, 2.15) })
            {
                var (y, zz) = Tourner(dy, dz, AnglePareGravois);
                for (int i = 0; i < _projet.Mailles.Count; i++)
                {
                    var m = _projet.Mailles[i];
                    Ajouter(References.LisseOuTraverse(m.Longueur), Famille.PareGravois, new Point3(_x[i], Cotes.Arrondi(-_l + y), Cotes.Arrondi(z + zz)), Axe.X, Face.Exterieure, i + 1);
                }
            }

            // Tôles de 0,90 m de large posées tous les 0,80 m (recouvrement 0,10 m), la dernière calée au bout ;
            // chaque tôle (2,00 m dans la pente) monte vers l'extérieur le long du bras de la console, 3 crochets chacune.
            double xMax = Math.Max(0, Cotes.Arrondi(LongueurTotale - LargeurTolePareGravois));
            for (double x = 0; ; x = Cotes.Arrondi(x + PasTolesPareGravois))
            {
                double xTole = Math.Min(x, xMax);
                Ajouter(References.TolePareGravois, Famille.PareGravois, new Point3(xTole, -_l - 0.10, z + 0.10), Axe.X, Face.Exterieure, 0, inverse: false, inclinaison: 90 + AnglePareGravois);
                Ajouter(References.CrochetPareGravois, Famille.PareGravois, new Point3(xTole, -_l, z + 0.30), Axe.X, Face.Exterieure);
                Ajouter(References.CrochetPareGravois, Famille.PareGravois, new Point3(xTole, -_l, z + 0.30), Axe.X, Face.Exterieure);
                Ajouter(References.CrochetPareGravois, Famille.PareGravois, new Point3(xTole, -_l, z + 0.075), Axe.X, Face.Exterieure);
                if (xTole >= xMax - Cotes.Tolerance)
                {
                    break;
                }
            }
        }
    }

    /// <summary>Rotation d'un point (dy, dz) autour de l'axe X de <paramref name="angle"/> degrés (sens v2 : Y vers Z).</summary>
    private static (double Y, double Z) Tourner(double dy, double dz, double angle)
    {
        double a = angle * Math.PI / 180;
        return (dy * Math.Cos(a) - dz * Math.Sin(a), dy * Math.Sin(a) + dz * Math.Cos(a));
    }

    private void PlacerConsole(ConsoleInfo c, int index, double x0, double lm, Niveau n, int num)
    {
        double z = n.Hauteur;
        double w = c.Largeur;
        bool gcDemande = _p.SecuriteInterieure != TypeSecurite.Sans;
        bool gc = c.PorteGardeCorps && gcDemande;
        bool lisse = gc || c.LisseSansPoteau(_p.Modele);
        bool sommet = Cotes.Egal(z, _h);
        string? embase = c.Embase(_p.Modele);
        double dz = c.DecalageZ(_p.Modele);

        // Console à platine : pas de poteau possible en bout (Laurent, 24/11/2026).
        if (gcDemande && !c.PorteGardeCorps)
        {
            _alertes.Add(FormattableString.Invariant(
                $"Maille {num}, niveau {z:0.00} m : console {c.Reference} à platine, pas de poteau ni de garde-corps possible en bout."));
        }

        // Une console sur chaque file bordant la maille, sans doublon avec la maille voisine.
        for (int f = index; f <= index + 1; f++)
        {
            if (f == index && MemeConsole(ConsoleDe(index - 1, n), c))
            {
                continue;
            }

            double x = _x[f];
            Ajouter(c.Reference, Famille.Console, new Point3(x, 0, z), Axe.Y, Face.Interieure, num);
            if (embase != null)
            {
                Ajouter(embase, Famille.Embase, new Point3(x, w, z), Axe.Z, Face.Interieure, num);
            }

            if (gc)
            {
                Ajouter(c.PoteauGardeCorps(_p.Modele), Famille.Poteau, new Point3(x, w, z + dz), Axe.Z, Face.Interieure, num);
                Ajouter(References.Goupille, Famille.Goupille, new Point3(x, w, z), Axe.Z, Face.Interieure, num);
                if (_p.GardeCorpsCouvreur && sommet)
                {
                    Ajouter(References.PoteauCouvreurConsole(_p.Modele), Famille.Poteau, new Point3(x, w, z + 1.0 + dz), Axe.Z, Face.Interieure, num);
                    Ajouter(References.Goupille, Famille.Goupille, new Point3(x, w, z + 1.0), Axe.Z, Face.Interieure, num);
                }
            }
        }

        // About en lisses au bout de la console quand la maille voisine n'a pas de console à ce niveau,
        // et en bout de façade (Laurent, 23/11/2026) ; il faut un point d'accroche au bout.
        if (lisse && ConsoleDe(index - 1, n) == null && (index > 0 || _p.AboutGauche))
        {
            PlacerAbout(_x[index], z, w, gauche: true, console: true, num, yOrigine: w);
        }

        if (lisse && ConsoleDe(index + 1, n) == null && (index + 1 < DerniereFile || _p.AboutDroit))
        {
            PlacerAbout(_x[index + 1], z, w, gauche: false, console: true, num, yOrigine: w);
        }

        PlacerPlateaux(x0, w, z, w, lm, num);
        if (lisse)
        {
            Ajouter("HMLIS" + Cotes.EnCm3(lm), Famille.Lisse, new Point3(x0, w, z), Axe.X, Face.Interieure, num);
        }

        if (gc)
        {
            PlacerGardeCorps(TypeSecurite.Lisses, _p.PlintheInterieure, x0, w, z, lm, Face.Interieure, num);
            if (_p.GardeCorpsCouvreur && sommet)
            {
                Ajouter(References.LisseOuTraverse(lm), Famille.Lisse, new Point3(x0, w, z + 1.5), Axe.X, Face.Interieure, num);
            }
        }
    }

    private static bool MemeConsole(ConsoleInfo? voisine, ConsoleInfo c) =>
        voisine != null && string.Equals(voisine.Reference, c.Reference, StringComparison.OrdinalIgnoreCase);

    // ------------------------------------------------------------------------------------------
    // Abouts
    // ------------------------------------------------------------------------------------------

    private void PlacerAbouts(List<Niveau> niveaux)
    {
        // Abouts de bout de face aux planchers de la maille d'extrémité (colonne « Ht max » : pas de garde-corps dans le vide).
        var premiere = _projet.Mailles[0];
        var derniere = _projet.Mailles[_projet.Mailles.Count - 1];
        bool portillonPose = false;

        // Portillon + échelle au premier plancher (Laurent, 24/11/2026) : about de départ de la face, sinon about de fin ;
        // pas au carré d'angle (passage vers la face précédente).
        bool portillonGauche = _p.AboutGauche && !_p.CarreDAngleEnTete && premiere.EstTravail;
        bool portillonDroit = !portillonGauche && _p.AboutDroit && derniere.EstTravail;

        if (_p.AboutGauche && premiere.EstTravail)
        {
            foreach (var n in NiveauxMaille(premiere, niveaux).Where(n => n.Type == TypePlancher.Complet && n.Hauteur > Cotes.Tolerance))
            {
                if (portillonGauche && PortillonNecessaire(n))
                {
                    PlacerPortillon(0, n.Hauteur, 1, gauche: true);
                    portillonPose = true;
                }
                else
                {
                    PlacerAbout(0, n.Hauteur, _l, gauche: true, console: false, 1);
                }
            }
        }

        if (_p.AboutDroit && derniere.EstTravail)
        {
            foreach (var n in NiveauxMaille(derniere, niveaux).Where(n => n.Type == TypePlancher.Complet && n.Hauteur > Cotes.Tolerance))
            {
                if (portillonDroit && PortillonNecessaire(n))
                {
                    PlacerPortillon(LongueurTotale, n.Hauteur, _projet.Mailles.Count, gauche: false);
                    portillonPose = true;
                }
                else
                {
                    PlacerAbout(LongueurTotale, n.Hauteur, _l, gauche: false, console: false, _projet.Mailles.Count);
                }
            }
        }

        var premier = PremierPlancher(niveaux);
        if (premier != null && PortillonNecessaire(premier) && !portillonPose && !_p.CarreDAngleEnTete
            && !_projet.Mailles.Any(m => m.Type == TypeMaille.Acces && NiveauxMaille(m, niveaux).Any(n => n.EstAccesSeul && Cotes.Egal(n.Hauteur, premier.Hauteur))))
        {
            _alertes.Add(FormattableString.Invariant(
                $"Premier plancher à {premier.Hauteur:0.00} m : pas d'about libre en bout de face pour le portillon et l'échelle d'accès."));
        }
    }

    /// <summary>Premier plancher au-dessus du sol (le plus bas), ou null.</summary>
    private static Niveau? PremierPlancher(List<Niveau> niveaux) => niveaux.FirstOrDefault(n => n.Hauteur > Cotes.Tolerance);

    /// <summary>
    /// Portillon d'about (Laurent, 24/11/2026) : premier plancher entre 0,50 et 1,50 m, avec ou sans maille d'accès ;
    /// l'about est remplacé par un portillon et une échelle entre lisses (HMECH050LIS / 100LIS / 150LIS) depuis le sol.
    /// </summary>
    private bool PortillonNecessaire(Niveau n) =>
        n.Hauteur >= 0.5 - Cotes.Tolerance && n.Hauteur <= 1.5 + Cotes.Tolerance
        && Cotes.Egal(n.Hauteur, PremierPlancher(_projet.Niveaux.OrderBy(v => v.Hauteur).ToList())?.Hauteur ?? -1);

    /// <summary>Portillon HMPORTILLONPLIN + échelle HMECH…LIS, à un about de maille (posés d'office).</summary>
    private void PlacerPortillon(double x, double z, int num, bool gauche = true)
    {
        double largeur = _l > 1.05 + Cotes.Tolerance ? 1.00 : _l;
        string? echelle = References.EchelleAbout(z);
        if (echelle != null)
        {
            Ajouter(echelle, Famille.Echelle, new Point3(x, 0, z), Axe.Y, Face.Aucune, num, inverse: gauche);
        }
        else
        {
            _alertes.Add(FormattableString.Invariant($"Portillon au niveau {z:0.00} m : pas d'échelle d'about de cette hauteur."));
        }

        Ajouter(References.Portillon(largeur), Famille.GardeCorps, new Point3(x, 0, z + 1.0), Axe.Y, Face.Aucune, num, inverse: gauche);
    }

    /// <summary>
    /// About en travers de la façade (port de Placeabout). Origine : (x, yOrigine, z), axe Y ;
    /// <c>Inverse</c> = about gauche (tourné vers les X négatifs).
    /// </summary>
    private void PlacerAbout(double x, double z, double largeur, bool gauche, bool console, int num, double yOrigine = 0)
    {
        string cm = Cotes.EnCm3(largeur);
        string traverse = References.LisseOuTraverse(largeur);
        var o = new Point3(x, yOrigine, z);
        TypePlinthe plinthe = console ? TypePlinthe.Sans : _p.PlintheAbouts;
        // Sur les consoles, l'about est en lisses (lisse + sous-lisse), Laurent 23/11/2026.
        var type = console ? TypeSecurite.Lisses : _p.SecuriteAbouts;

        if (largeur <= 0.40 + Cotes.Tolerance)
        {
            type = TypeSecurite.Lisses;
            plinthe = TypePlinthe.Sans;
        }

        switch (type)
        {
            case TypeSecurite.GardeCorpsMds2 when largeur > 0.5:
                Ajouter("HMGCSE" + cm + "GT", Famille.GardeCorps, o, Axe.Y, Face.Aucune, num, gauche);
                if (largeur < 1.5)
                {
                    plinthe = TypePlinthe.Sans;
                }

                break;

            case TypeSecurite.GardeCorpsAbout:
                if (largeur < 0.70 - Cotes.Tolerance)
                {
                    Ajouter("HMTRA" + cm, Famille.Traverse, o.Decale(dz: 0.5), Axe.Y, Face.Aucune, num, gauche);
                    Ajouter("HMTRA" + cm, Famille.Traverse, o.Decale(dz: 1.0), Axe.Y, Face.Aucune, num, gauche);
                }
                else
                {
                    Ajouter("HMGCA" + cm, Famille.GardeCorps, o.Decale(dz: 1.0), Axe.Y, Face.Aucune, num, gauche);
                }

                plinthe = TypePlinthe.Sans;
                break;

            case TypeSecurite.Sans:
                return;

            default:
                Ajouter(traverse, Famille.Traverse, o.Decale(dz: 0.5), Axe.Y, Face.Aucune, num, gauche);
                Ajouter(traverse, Famille.Traverse, o.Decale(dz: 1.0), Axe.Y, Face.Aucune, num, gauche);
                break;
        }

        if (_p.GardeCorpsCouvreur && Cotes.Egal(z, _h))
        {
            Ajouter(traverse, Famille.Traverse, o.Decale(dz: 1.5), Axe.Y, Face.Aucune, num, gauche);
        }

        var refPlinthe = References.Plinthe(plinthe, largeur);
        if (refPlinthe != null)
        {
            Ajouter(refPlinthe, Famille.Plinthe, o, Axe.Y, Face.Aucune, num, gauche);
        }
    }

    // ------------------------------------------------------------------------------------------
    // Diagonales verticales (extérieur, + intérieur si protection intérieure)
    // ------------------------------------------------------------------------------------------

    private void PlacerDiagonales()
    {
        // Hauteur non multiple de 2 : une diagonale en pied, puis la colonne décalée pour finir au dernier plancher.
        double debut = Cotes.Arrondi(_h % 2.0);
        foreach (var d in RegleDiagonales.Calculer(_projet))
        {
            int i = d.NumeroMaille - 1;
            double x0 = _x[i];
            var maille = _projet.Mailles[i];
            double lm = maille.Longueur;
            string refDia = References.DiagonaleVerticale(lm);

            // Sous-maille rapportée : la diagonale extérieure passe sur la file rapportée, jusqu'à son dernier plancher.
            double hs = maille.LargeurRapportee > Cotes.Tolerance ? HauteurSousMaille(maille, _projet.Niveaux) : 0;

            if (PoutreSous(i) == null)
            {
                LissesDePied(d.NumeroMaille, x0, lm);
            }

            var cotes = new List<double>();
            if (debut > Cotes.Tolerance)
            {
                cotes.Add(0);
            }

            // Toute la hauteur de la maille (colonne « Ht max »).
            double hm = maille.HauteurMax(_h);
            for (double z = debut; z <= hm - 1.0 + Cotes.Tolerance; z = Cotes.Arrondi(z + 2.0))
            {
                cotes.Add(z);
            }

            // Diagonales en travers (v2) : structure volumique de plus de 1,05 m ou socles à roues,
            // à la file gauche de la maille, et à la file droite pour la dernière maille.
            bool enTravers = (_p.ModuleVolumique != null && _l > 1.05 + Cotes.Tolerance) || CatalogueSocles.De(_p.Socle).AvecRoues;

            // Sens inversé : origine à droite de la maille, bloc symétrisé (diagonale montant vers la gauche).
            double xDia = d.Inverse ? x0 + lm : x0;
            foreach (double z in cotes.Where(z => !SousPoutre(i, z)))
            {
                // Volumique : pas de diagonale sur un côté commun à deux modules (Laurent, 24/11/2026) ;
                // côté façade d'un module au-delà de M1 hors tronçon commun : il donne dans le vide, diagonales comme à l'extérieur.
                bool exterieure = d.Exterieure && !ExterieurCommun(i) && !CarreDAngleInverse(i);
                bool interieure = _p.ModuleVolumique > 0 ? (d.Exterieure || d.Interieure) && !InterieurCommun(i) : d.Interieure;
                if (exterieure && !(maille.EstEscalier && hs > Cotes.Tolerance)) // la tour d'escalier a ses propres diagonales
                {
                    double yExt = z < hs - Cotes.Tolerance ? -_l - maille.LargeurRapportee : -_l;
                    Ajouter(refDia, Famille.DiagonaleVerticale, new Point3(xDia, Cotes.Arrondi(yExt - DemiTube), z), Axe.X, Face.Exterieure, d.NumeroMaille, d.Inverse);
                }

                if (interieure)
                {
                    Ajouter(refDia, Famille.DiagonaleVerticale, new Point3(xDia, DemiTube, z), Axe.X, Face.Interieure, d.NumeroMaille, d.Inverse);
                }

                if (enTravers)
                {
                    string diaL = References.DiagonaleVerticale(_l);
                    Ajouter(diaL, Famille.DiagonaleVerticale, new Point3(Cotes.Arrondi(x0 + DemiTube), 0, z), Axe.Y, Face.Aucune, d.NumeroMaille, d.Inverse);
                    if (i == _projet.Mailles.Count - 1)
                    {
                        Ajouter(diaL, Famille.DiagonaleVerticale, new Point3(Cotes.Arrondi(x0 + lm - DemiTube), 0, z), Axe.Y, Face.Aucune, d.NumeroMaille, !d.Inverse);
                    }
                }
            }
        }
    }

    // ------------------------------------------------------------------------------------------
    // Amarrages (placés sur le dessin : nouveauté v3)
    // ------------------------------------------------------------------------------------------

    private BilanAmarrages PlacerAmarrages()
    {
        var bilan = SansAmarragesSousPoutres(RegleAmarrages.Calculer(_projet));
        if (_projet.Mailles.Any(m => !Cotes.Egal(m.HauteurMax(_h), _h)))
        {
            // Colonne « Ht max » : pas d'amarrage au-dessus du dernier plancher atteint par la file.
            var restants = bilan.Amarrages.Where(a => a.Hauteur <= HauteurFile(a.File) + Cotes.Tolerance).ToList();
            bilan = new BilanAmarrages(restants, bilan.Surface, bilan.MinimumParSurface, bilan.NoteDeCalculObligatoire);
        }

        if (_p.ModuleVolumique > 0)
        {
            // Volumique : seul le module contre la façade est amarré.
            return new BilanAmarrages(Array.Empty<Amarrage>(), 0, 0, bilan.NoteDeCalculObligatoire);
        }

        string tube = References.TubeAmarrage(_l + _p.DistanceFacade + 0.10);
        foreach (var a in bilan.Amarrages)
        {
            Ajouter(tube, Famille.Amarrage, new Point3(a.X, 0, a.Hauteur), Axe.Y, Face.Interieure);
        }

        return bilan;
    }

    /// <summary>Lisses de pied (z = 0) côté façade et côté extérieur, posées une seule fois par maille.</summary>
    private void LissesDePied(int num, double x0, double lm)
    {
        if (!_lissesPied.Add(num))
        {
            return;
        }

        // Carré d'angle : le côté posé sur la dernière file de la face précédente (côté intérieur, côté extérieur
        // si la structure est inversée) a déjà la traverse de pied de cette file.
        string lisse = References.LisseOuTraverse(lm);
        bool carre = CarreDAngle(num - 1);
        if (!carre || _p.CarreDAngleInverse)
        {
            Ajouter(lisse, Famille.Lisse, new Point3(x0, 0, 0), Axe.X, Face.Interieure, num);
        }

        if (!carre || !_p.CarreDAngleInverse)
        {
            Ajouter(lisse, Famille.Lisse, new Point3(x0, -_l, 0), Axe.X, Face.Exterieure, num);
        }
    }

    private void Ajouter(string reference, Famille famille, Point3 origine, Axe axe, Face face = Face.Aucune, int numeroMaille = 0, bool inverse = false, double inclinaison = 0, double rotation = 0) =>
        _el.Add(new ElementPlace(reference, famille, origine, axe, face, numeroMaille, inverse, inclinaison, rotation));
}
