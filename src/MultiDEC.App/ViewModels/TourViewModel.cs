using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using MultiDEC.Core;
using System.Runtime.CompilerServices;
using MultiDEC.Core.Geometrie;
using MultiDEC.Core.TourEscalier;

namespace MultiDEC.App.ViewModels;

/// <summary>
/// Onglet d'une tour escalier alu (T1, T2…) : paramètres de la tour, position (seule ou accolée à une face)
/// et grille des paliers avec leurs sorties. Les valeurs sont écrites directement dans la tour du projet.
/// </summary>
public sealed class TourViewModel : INotifyPropertyChanged
{
    private readonly TourEscalierAlu _t;
    private readonly FacadeViewModel _parent;

    public TourViewModel(TourEscalierAlu tour, FacadeViewModel parent)
    {
        _t = tour;
        _parent = parent;
        SortiesCoteFacade = new Commande(ProposerSortiesCoteFacade, () => Accolee);
        RecalculerPaliers();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // ---- Listes de choix --------------------------------------------------------------------

    public static IReadOnlyList<double> Largeurs => TourEscalierAlu.Largeurs;

    public static IReadOnlyList<KeyValuePair<ConfigurationTour, string>> Configurations { get; } = new[]
    {
        new KeyValuePair<ConfigurationTour, string>(ConfigurationTour.Niveaux2m, "Paliers tous les 2 m"),
        new KeyValuePair<ConfigurationTour, string>(ConfigurationTour.Alternes1m2m, "Paliers alternés 1 m / 2 m"),
    };

    public static IReadOnlyList<KeyValuePair<CoteTour, string>> Entrees { get; } = new[]
    {
        new KeyValuePair<CoteTour, string>(CoteTour.D, "Bout D (gauche, X = 0)"),
        new KeyValuePair<CoteTour, string>(CoteTour.B, "Bout B (droite, X = 2,50)"),
    };

    public static IReadOnlyList<KeyValuePair<StabiliteTour, string>> Stabilites { get; } = new[]
    {
        new KeyValuePair<StabiliteTour, string>(StabiliteTour.Amarree, "Amarrée au bâtiment (côté A)"),
        new KeyValuePair<StabiliteTour, string>(StabiliteTour.Lestee, "Autostable lestée (note de calcul)"),
    };

    public static IReadOnlyList<double> Angles { get; } = new[] { 0.0, 90.0, 180.0, 270.0 };

    public static IReadOnlyList<double> LargeursOuverture { get; } = new[] { 0.50, 1.00 };

    /// <summary>Faces auxquelles la tour peut être accolée (aucune sans façade ou en structure volumique).</summary>
    public IReadOnlyList<string> FacesPossibles =>
        _parent.Projet.AvecFacade && !_parent.Projet.EstVolumique ? _parent.Projet.Faces.Select(f => f.Nom).ToList() : new List<string>();

    // ---- Paramètres -------------------------------------------------------------------------

    public TourEscalierAlu Tour => _t;

    public string Nom => _t.Nom;

    public double Largeur
    {
        get => _t.Largeur;
        set => Changer(() => _t.Largeur = value, paliers: false);
    }

    public ConfigurationTour Configuration
    {
        get => _t.Configuration;
        set => Changer(() => _t.Configuration = value, paliers: true);
    }

    public double Hauteur
    {
        get => _t.Hauteur;
        set => Changer(() => _t.Hauteur = Cotes.Arrondi(value), paliers: true);
    }

    public bool Depart3m
    {
        get => _t.Depart3m;
        set => Changer(() => _t.Depart3m = value, paliers: false);
    }

    public CoteTour Entree
    {
        get => _t.Entree;
        set => Changer(() => _t.Entree = value, paliers: true);
    }

    public StabiliteTour Stabilite
    {
        get => _t.Stabilite;
        set => Changer(() => _t.Stabilite = value, paliers: false);
    }

    public double DistanceBatiment
    {
        get => _t.DistanceBatiment;
        set => Changer(() => _t.DistanceBatiment = value, paliers: false);
    }

    public double PositionX
    {
        get => _t.PositionX;
        set => Changer(() => _t.PositionX = value, paliers: false);
    }

    public double PositionY
    {
        get => _t.PositionY;
        set => Changer(() => _t.PositionY = value, paliers: false);
    }

    public double Angle
    {
        get => _t.Angle;
        set => Changer(() => _t.Angle = value, paliers: false);
    }

    /// <summary>Tour accolée à une face de la façade : à l'accolement, sorties côté A proposées aux planchers communs.</summary>
    public bool Accolee
    {
        get => _t.EstAccolee;
        set
        {
            if (value == Accolee)
            {
                return;
            }

            if (value && FacesPossibles.Count == 0)
            {
                Notifier();
                return;
            }

            _t.FaceAccolee = value ? FacesPossibles[0] : null;
            if (value)
            {
                ProposerSortiesCoteFacade();
            }

            Notifier(string.Empty);
            SortiesCoteFacade.Rafraichir();
        }
    }

    /// <summary>Tour seule : position libre et stabilité propre.</summary>
    public bool Seule => !Accolee;

    public string? FaceAccolee
    {
        get => _t.FaceAccolee;
        set => Changer(() => _t.FaceAccolee = string.IsNullOrEmpty(value) ? null : value, paliers: false);
    }

    public double Abscisse
    {
        get => _t.Abscisse;
        set => Changer(() => _t.Abscisse = value, paliers: false);
    }

    public double Ecart
    {
        get => _t.Ecart;
        set => Changer(() => _t.Ecart = value, paliers: false);
    }

    /// <summary>Message d'erreur sur la hauteur (vide si elle convient).</summary>
    public string ErreurHauteur => _t.ErreurHauteur() ?? string.Empty;

    public bool HauteurCorrecte => _t.ErreurHauteur() == null;

    public bool HauteurIncorrecte => !HauteurCorrecte;

    /// <summary>Résumé affiché sous les paramètres.</summary>
    public string Resume
    {
        get
        {
            if (!HauteurCorrecte)
            {
                return ErreurHauteur;
            }

            var paliers = _t.Paliers();
            var fr = CultureNombres.Documents;
            string texte = string.Format(fr, "Tour {0:0.00} x {1:0.00} m, {2} volée(s), dernier palier à {3:0.00} m", TourEscalierAlu.Longueur, _t.Largeur, paliers.Count - 1, _t.Hauteur);
            if (_t.HauteurDepart > Cotes.Tolerance)
            {
                texte += string.Format(fr, ", départ à {0:0.00} m (plancher de départ)", _t.HauteurDepart);
            }

            return texte + (Accolee ? " · accolée à " + _t.FaceAccolee : string.Empty) + ".";
        }
    }

    // ---- Paliers et sorties -----------------------------------------------------------------

    /// <summary>Paliers du plus haut au plus bas (comme la grille des niveaux de la façade), départ exclu.</summary>
    public ObservableCollection<PalierTour> Paliers { get; } = new ObservableCollection<PalierTour>();

    public Commande SortiesCoteFacade { get; }

    /// <summary>Ajoute une sortie côté A (1,00 m) à chaque palier qui tombe sur un plancher de la face accolée.</summary>
    public void ProposerSortiesCoteFacade()
    {
        foreach (double z in ToursDansLaStructure.PaliersCommuns(_parent.Projet, _t))
        {
            if (!_t.Sorties.Any(s => s.Cote == CoteTour.A && Cotes.Egal(s.Hauteur, z)))
            {
                _t.Sorties.Add(new SortieTour(z, CoteTour.A, 1.0));
            }
        }

        RecalculerPaliers();
    }

    /// <summary>Ouvre ou ferme un côté à un palier (clic droit dans le dessin). Renvoie le nouvel état (true = ouvert).</summary>
    public bool BasculerSortie(CoteTour cote, double palier)
    {
        var existante = _t.Sorties.FirstOrDefault(s => s.Cote == cote && Cotes.Egal(s.Hauteur, palier));
        if (existante != null)
        {
            _t.Sorties.Remove(existante);
        }
        else
        {
            _t.Sorties.Add(new SortieTour(palier, cote, 1.0));
        }

        RecalculerPaliers();
        return existante == null;
    }

    public bool SortieOuverte(CoteTour cote, double palier) => _t.Sorties.Any(s => s.Cote == cote && Cotes.Egal(s.Hauteur, palier));

    /// <summary>Reconstruit la grille des paliers ; les sorties à une hauteur qui n'est plus un palier sont retirées.</summary>
    private void RecalculerPaliers()
    {
        Paliers.Clear();
        if (!HauteurCorrecte)
        {
            return;
        }

        var paliers = _t.Paliers();
        _t.Sorties.RemoveAll(s => !paliers.Skip(1).Any(z => Cotes.Egal(z, s.Hauteur)));
        for (int k = paliers.Count - 1; k >= 1; k--)
        {
            Paliers.Add(new PalierTour(_t, paliers[k], _t.BoutArrivee(k)));
        }
    }

    private void Changer(Action modifier, bool paliers, [CallerMemberName] string? nom = null)
    {
        // Les sorties du dernier palier suivent le dernier palier (hauteur, configuration ou entrée modifiée).
        double? hautAvant = HauteurCorrecte ? _t.Paliers().Last() : (double?)null;
        var sortiesDuHaut = hautAvant is double h ? _t.Sorties.Where(s => Cotes.Egal(s.Hauteur, h)).ToList() : new List<SortieTour>();
        modifier();
        if (paliers)
        {
            if (HauteurCorrecte && sortiesDuHaut.Count > 0)
            {
                var nouveaux = _t.Paliers();
                double haut = nouveaux[nouveaux.Count - 1];
                foreach (var s in sortiesDuHaut)
                {
                    s.Hauteur = haut;
                    if (s.Cote == CoteTour.B || s.Cote == CoteTour.D)
                    {
                        s.Cote = _t.BoutArrivee(nouveaux.Count - 1);
                    }
                }
            }

            RecalculerPaliers();
        }

        Notifier(nom);
        Notifier(nameof(ErreurHauteur));
        Notifier(nameof(HauteurCorrecte));
        Notifier(nameof(HauteurIncorrecte));
        Notifier(nameof(Resume));
    }

    private void Notifier([CallerMemberName] string? nom = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nom));
}

/// <summary>Une ligne de la grille des paliers d'une tour : sorties côté A, côté C et au bout d'arrivée.</summary>
public sealed class PalierTour : INotifyPropertyChanged
{
    private readonly TourEscalierAlu _t;

    public PalierTour(TourEscalierAlu tour, double hauteur, CoteTour bout)
    {
        _t = tour;
        Hauteur = hauteur;
        Bout = bout;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public double Hauteur { get; }

    /// <summary>Bout (B ou D) où arrive la volée : seul ce bout peut recevoir une sortie.</summary>
    public CoteTour Bout { get; }

    public string TexteBout => "Bout " + Bout;

    public bool SortieA
    {
        get => Sortie(CoteTour.A) != null;
        set => Basculer(CoteTour.A, value);
    }

    public double LargeurA
    {
        get => Sortie(CoteTour.A)?.Largeur is double l && Cotes.Egal(l, 0.5) ? 0.5 : 1.0;
        set => Largeur(CoteTour.A, value);
    }

    public bool SortieC
    {
        get => Sortie(CoteTour.C) != null;
        set => Basculer(CoteTour.C, value);
    }

    public double LargeurC
    {
        get => Sortie(CoteTour.C)?.Largeur is double l && Cotes.Egal(l, 0.5) ? 0.5 : 1.0;
        set => Largeur(CoteTour.C, value);
    }

    public bool SortieBout
    {
        get => Sortie(Bout) != null;
        set => Basculer(Bout, value);
    }

    private SortieTour? Sortie(CoteTour cote) => _t.Sorties.FirstOrDefault(s => s.Cote == cote && Cotes.Egal(s.Hauteur, Hauteur));

    private void Basculer(CoteTour cote, bool ouverte)
    {
        var s = Sortie(cote);
        if (ouverte && s == null)
        {
            _t.Sorties.Add(new SortieTour(Hauteur, cote, 1.0));
        }
        else if (!ouverte && s != null)
        {
            _t.Sorties.Remove(s);
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    private void Largeur(CoteTour cote, double largeur)
    {
        var s = Sortie(cote);
        if (s == null)
        {
            _t.Sorties.Add(s = new SortieTour(Hauteur, cote, largeur));
        }

        s.Largeur = largeur;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }
}
