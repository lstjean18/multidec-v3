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
}

/// <summary>
/// Moteur façade v3 : transforme un <see cref="ProjetFacade"/> en liste d'éléments positionnés.
/// Port structuré de Facade.vb (MultiRETO / MultiSystem / PlacePieds / PlacePoteaux / PlaceMailles /
/// PlaceGC / PlaceConsole / PlacePlateaux / PlaceAcces / PlaceDiago / Placeabout), sans dépendance
/// à l'interface ni à VectorDraw, plus les nouveautés v3 (consoles par maille, diagonales intérieures,
/// amarrages placés).
/// Conventions (alignées sur les blocs de Blocs-EM-3D.dxf, dessinés dans le plan XY, hauteur selon +Y) :
/// - élément d'axe X : origine à l'extrémité gauche (X mini), ou à droite si <c>Inverse</c> (bloc symétrisé) ;
/// - plateaux et trappes : origine sur le bord côté façade, la largeur part vers l'extérieur (Y négatifs) ;
/// - élément d'axe Y : origine côté file intérieure, part vers l'extérieur ; les consoles partent vers la façade.
/// Le rendu (MultiDEC.Rendering.VectorDraw) traduit ces conventions en matrices d'insertion.
/// </summary>
public sealed class GenerateurFacade
{
    /// <summary>Demi-diamètre de tube : décalage des diagonales et lisses posées contre les poteaux.</summary>
    public const double DemiTube = 0.0483;

    /// <summary>Retrait du premier plateau par rapport à la file.</summary>
    public const double RetraitPlateau = 0.025;

    /// <summary>Largeur nominale d'un plateau acier.</summary>
    public const double LargeurPlateau = 0.30;

    private readonly ProjetFacade _projet;
    private readonly ParametresFacade _p;
    private readonly List<ElementPlace> _el = new List<ElementPlace>();
    private readonly List<string> _alertes = new List<string>();
    private readonly IReadOnlyList<double> _x;
    private readonly double _l;
    private readonly double _h;

    /// <summary>Mailles qui ont déjà leurs lisses de pied (une seule paire par maille).</summary>
    private readonly HashSet<int> _lissesPied = new HashSet<int>();

    private GenerateurFacade(ProjetFacade projet)
    {
        _projet = projet;
        _p = projet.Parametres;
        _x = projet.AbscissesFiles();
        _l = _p.LargeurMaille;
        _h = _p.HauteurDernierPlancher;
    }

    private int DerniereFile => _x.Count - 1;

    private double LongueurTotale => _x[DerniereFile];

    public static StructureFacade Generer(ProjetFacade projet)
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

        PlacerFiles(niveaux);
        for (int i = 0; i < _projet.Mailles.Count; i++)
        {
            PlacerMaille(i, niveaux);
        }

        PlacerDiagonales();
        PlacerAbouts(niveaux);
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

        if (_p.SecuriteExterieure == TypeSecurite.Sans && _p.SecuriteInterieure == TypeSecurite.Sans)
        {
            _alertes.Add("Aucun garde-corps : pas de diagonale verticale posée, contreventement à prévoir.");
        }

        if (RegleDiagonales.RenforcementConseille(niveaux) && !_p.DiagonalesRenforcees)
        {
            _alertes.Add("Beaucoup de niveaux « accès seul » : diagonales renforcées conseillées.");
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

            // Côté extérieur : garde-corps en tête ; départ 3 m si option.
            Empiler(x, -_l, Face.Exterieure, _p.Depart3mExterieur, gardeCorps: true);

            // Côté intérieur : garde-corps si extrémité, ou protection intérieure sur une maille voisine sans console.
            bool gcInt = extremite || (_p.SecuriteInterieure != TypeSecurite.Sans && MaillesVoisines(f).Any(m => !m.AConsole));
            Empiler(x, 0, Face.Interieure, _p.Depart3mExterieur && extremite, gcInt);

            // Traverses à chaque niveau (renforcées aux planchers complets si demandé).
            foreach (var n in niveaux)
            {
                bool renforce = _p.Renforce && n.Hauteur > Cotes.Tolerance && !n.EstAccesSeul;
                Ajouter(References.LisseOuTraverse(_l, renforce), Famille.Traverse, new Point3(x, 0, n.Hauteur), Axe.Y);
            }

            if (!niveaux.Any(n => Cotes.Egal(n.Hauteur, 0)))
            {
                Ajouter(References.LisseOuTraverse(_l), Famille.Traverse, new Point3(x, 0, 0), Axe.Y);
            }

            if (_p.GardeCorpsCouvreur && extremite)
            {
                Ajouter(References.LisseOuTraverse(_l), Famille.Traverse, new Point3(x, 0, _h + 1.5), Axe.Y);
            }
        }
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

    private void Empiler(double x, double y, Face face, bool depart3m, bool gardeCorps)
    {
        foreach (var t in ReglePoteaux.Empiler(_p, depart3m, gardeCorps))
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

        ConsoleInfo? console = null;
        if (m.AConsole && !CatalogueConsoles.TryGet(_p.Modele, m.Console!, out console))
        {
            _alertes.Add($"Maille {num} : console {m.Console} inconnue pour la gamme {_p.Modele}, ignorée.");
            console = null;
        }

        if (_p.LissesEnPied)
        {
            LissesDePied(num, x0, lm);
        }

        foreach (var n in niveaux)
        {
            double z = n.Hauteur;
            bool sansPlancher = n.EstAccesSeul && m.Type == TypeMaille.Travail;

            if (z > Cotes.Tolerance)
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
                var plintheExt = n.EstAccesSeul ? TypePlinthe.Sans : _p.PlintheExterieure;
                PlacerGardeCorps(_p.SecuriteExterieure, plintheExt, x0, -_l, z, lm, Face.Exterieure, num);

                // v3 : même règle côté intérieur (l'ancienne version posait le GC intérieur même sans plancher).
                if (console == null)
                {
                    PlacerGardeCorps(_p.SecuriteInterieure, _p.PlintheInterieure, x0, 0, z, lm, Face.Interieure, num);
                }
            }

            if (m.Type == TypeMaille.Travail)
            {
                if (n.Type == TypePlancher.Complet && z > Cotes.Tolerance)
                {
                    PlacerPlateaux(x0, 0, z, _l, lm, num);
                }
                else if (n.Type == TypePlancher.AccesSeulDiagoH && z > Cotes.Tolerance)
                {
                    Ajouter(References.DiagonaleHorizontale(_l, lm), Famille.DiagonaleHorizontale, new Point3(x0, 0, z), Axe.X, Face.Aucune, num);
                }
            }
            else
            {
                PlacerAcces(m, num, x0, n, ref inverseTrappe);
            }

            if (console != null && n.Type == TypePlancher.Complet && z > Cotes.Tolerance)
            {
                PlacerConsole(console, index, x0, lm, z, num);
            }
        }

        if (_p.GardeCorpsCouvreur)
        {
            Ajouter(lisse, Famille.Lisse, new Point3(x0, -_l, _h + 1.5), Axe.X, Face.Exterieure, num);
            if (_p.SecuriteInterieure != TypeSecurite.Sans && console == null)
            {
                Ajouter(lisse, Famille.Lisse, new Point3(x0, 0, _h + 1.5), Axe.X, Face.Interieure, num);
            }
        }
    }

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
        bool aluBois = gamme == GammePlateaux.AluBois;
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
            if (!aluBois)
            {
                yield return (1, 1);
            }
        }
        else if (aluBois)
        {
            yield return (1, 0);
            yield return (2, 2);
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
            // Maille d'accès isolée : abouts des deux côtés.
            PlacerAbout(x0, z, _l, gauche: true, console: false, num);
            PlacerAbout(x0 + lm, z, _l, gauche: false, console: false, num);
        }

        if (z <= Cotes.Tolerance)
        {
            return;
        }

        if (z < 2.0 - Cotes.Tolerance)
        {
            PlacerPlateaux(x0, 0, z, _l, lm, num);
            return;
        }

        // Plateau à trappe (bande intérieure de 0,60 m) + échelle (seulement jusqu'à 2,00 m de maille),
        // puis plateaux de complément. Sens alterné d'un niveau à l'autre : en sens inversé, l'origine
        // est à droite de la maille et le bloc est symétrisé.
        double xTrappe = inverse ? x0 + lm : x0;
        Ajouter(References.PlateauTrappe(lm, _l), Famille.Trappe, new Point3(xTrappe, -RetraitPlateau, z), Axe.X, Face.Aucune, num, inverse);
        if (lm <= 2.0 + Cotes.Tolerance)
        {
            Ajouter(References.Echelle, Famille.Echelle, new Point3(xTrappe, -RetraitPlateau, z), Axe.X, Face.Aucune, num, inverse);
        }

        PlacerPlateaux(x0, 0, z, _l, lm, num, premierRang: 2);

        bool premierAcces = Cotes.Egal(z, 2.0) || Cotes.Egal(z, 3.0);
        if (premierAcces)
        {
            // Plateaux de départ au pied de l'échelle.
            PlacerPlateaux(x0, 0, z - 2.0, _l, lm, num, premierRang: 0, dernierRang: 1);
        }
        else
        {
            // Lisse de sécurité au droit de la trémie, côté extérieur.
            string secu = _p.SecuriteExterieure == TypeSecurite.GardeCorpsMds1 ? "HMLISSEC" + Cotes.EnCm3(lm) : References.LisseOuTraverse(lm);
            Ajouter(secu, Famille.Lisse, new Point3(x0, -_l - DemiTube, z - 0.5), Axe.X, Face.Exterieure, num);

            if (_p.SecuriteInterieure != TypeSecurite.Sans)
            {
                string secuInt = _p.SecuriteInterieure == TypeSecurite.GardeCorpsMds1 ? "HMLISSEC" + Cotes.EnCm3(lm) : References.LisseOuTraverse(lm);
                Ajouter(secuInt, Famille.Lisse, new Point3(x0, DemiTube, z - 0.5), Axe.X, Face.Interieure, num);
                Ajouter("HMCOL49ROSACE", Famille.Lisse, new Point3(x0, 0, z - 0.5), Axe.Z, Face.Interieure, num);
                Ajouter("HMCOL49ROSACE", Famille.Lisse, new Point3(x0 + lm, 0, z - 0.5), Axe.Z, Face.Interieure, num);
            }
        }

        inverse = !inverse;
    }

    private void PlacerConsole(ConsoleInfo c, int index, double x0, double lm, double z, int num)
    {
        double w = c.Largeur;
        bool gc = c.PorteGardeCorps && _p.SecuriteInterieure != TypeSecurite.Sans;
        bool sommet = Cotes.Egal(z, _h);

        // Une console sur chaque file bordant la maille, sans doublon avec la maille voisine.
        for (int f = index; f <= index + 1; f++)
        {
            if (f == index && index > 0 && MemeConsole(index - 1, c))
            {
                continue;
            }

            double x = _x[f];
            Ajouter(c.Reference, Famille.Console, new Point3(x, 0, z), Axe.Y, Face.Interieure, num);
            if (c.AccessoireBout != null)
            {
                Ajouter(c.AccessoireBout, Famille.Embase, new Point3(x, w, z), Axe.Z, Face.Interieure, num);
            }

            if (gc)
            {
                Ajouter(References.PoteauGardeCorpsConsole(_p.Modele), Famille.Poteau, new Point3(x, w, z + c.DecalageZ), Axe.Z, Face.Interieure, num);
                Ajouter(References.Goupille, Famille.Goupille, new Point3(x, w, z), Axe.Z, Face.Interieure, num);
                if (_p.GardeCorpsCouvreur && sommet)
                {
                    Ajouter(References.PoteauCouvreurConsole(_p.Modele), Famille.Poteau, new Point3(x, w, z + 1.0 + c.DecalageZ), Axe.Z, Face.Interieure, num);
                    Ajouter(References.Goupille, Famille.Goupille, new Point3(x, w, z + 1.0), Axe.Z, Face.Interieure, num);
                }
            }

            // Abouts de console aux extrémités de la façade.
            if (f == 0 || f == DerniereFile)
            {
                PlacerAboutConsole(x, z, w, gauche: f == 0, num);
            }
        }

        PlacerPlateaux(x0, w, z, w, lm, num);
        if (c.PorteGardeCorps)
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

    private bool MemeConsole(int indexMaille, ConsoleInfo c) =>
        string.Equals(_projet.Mailles[indexMaille].Console, c.Reference, StringComparison.OrdinalIgnoreCase);

    // ------------------------------------------------------------------------------------------
    // Abouts
    // ------------------------------------------------------------------------------------------

    private void PlacerAbouts(List<Niveau> niveaux)
    {
        foreach (var n in niveaux.Where(n => n.Type == TypePlancher.Complet && n.Hauteur > Cotes.Tolerance))
        {
            if (_p.AboutGauche && _projet.Mailles[0].Type == TypeMaille.Travail)
            {
                PlacerAbout(0, n.Hauteur, _l, gauche: true, console: false, 1);
            }

            if (_p.AboutDroit && _projet.Mailles[_projet.Mailles.Count - 1].Type == TypeMaille.Travail)
            {
                PlacerAbout(LongueurTotale, n.Hauteur, _l, gauche: false, console: false, _projet.Mailles.Count);
            }
        }
    }

    private void PlacerAboutConsole(double x, double z, double largeur, bool gauche, int num)
    {
        if ((gauche && _p.AboutGauche) || (!gauche && _p.AboutDroit))
        {
            PlacerAbout(x, z, largeur, gauche, console: true, num, yOrigine: largeur);
        }
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
        TypePlinthe plinthe = console ? TypePlinthe.Axees : _p.PlintheAbouts;
        var type = console ? TypeSecurite.GardeCorpsAbout : _p.SecuriteAbouts;

        if (largeur <= 0.40 + Cotes.Tolerance)
        {
            type = TypeSecurite.Lisses;
            plinthe = TypePlinthe.Sans;
        }

        switch (type)
        {
            case TypeSecurite.GardeCorpsMds1:
                Ajouter("HMGCSE" + cm, Famille.GardeCorps, o, Axe.Y, Face.Aucune, num, gauche);
                break;

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
            double lm = _projet.Mailles[i].Longueur;
            string refDia = References.DiagonaleVerticale(lm);

            LissesDePied(d.NumeroMaille, x0, lm);

            var cotes = new List<double>();
            if (debut > Cotes.Tolerance)
            {
                cotes.Add(0);
            }

            for (double z = debut; z <= _h - 1.0 + Cotes.Tolerance; z = Cotes.Arrondi(z + 2.0))
            {
                cotes.Add(z);
            }

            // Sens inversé : origine à droite de la maille, bloc symétrisé (diagonale montant vers la gauche).
            double xDia = d.Inverse ? x0 + lm : x0;
            foreach (double z in cotes)
            {
                if (d.Exterieure)
                {
                    Ajouter(refDia, Famille.DiagonaleVerticale, new Point3(xDia, -_l - DemiTube, z), Axe.X, Face.Exterieure, d.NumeroMaille, d.Inverse);
                }

                if (d.Interieure)
                {
                    Ajouter(refDia, Famille.DiagonaleVerticale, new Point3(xDia, DemiTube, z), Axe.X, Face.Interieure, d.NumeroMaille, d.Inverse);
                }
            }
        }
    }

    // ------------------------------------------------------------------------------------------
    // Amarrages (placés sur le dessin : nouveauté v3)
    // ------------------------------------------------------------------------------------------

    private BilanAmarrages PlacerAmarrages()
    {
        var bilan = RegleAmarrages.Calculer(_projet);
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

        string lisse = References.LisseOuTraverse(lm);
        Ajouter(lisse, Famille.Lisse, new Point3(x0, 0, 0), Axe.X, Face.Interieure, num);
        Ajouter(lisse, Famille.Lisse, new Point3(x0, -_l, 0), Axe.X, Face.Exterieure, num);
    }

    private void Ajouter(string reference, Famille famille, Point3 origine, Axe axe, Face face = Face.Aucune, int numeroMaille = 0, bool inverse = false) =>
        _el.Add(new ElementPlace(reference, famille, origine, axe, face, numeroMaille, inverse));
}
