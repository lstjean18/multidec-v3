using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using MultiDEC.Core.Catalogue;
using MultiDEC.Core.Facade;
using MultiDEC.Core.Persistance;

namespace MultiDEC.App.ViewModels;

/// <summary>Base des lignes de l'écran « Paramètres par défaut ».</summary>
public abstract class ElementParDefaut : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Notifier([CallerMemberName] string? nom = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nom));
}

/// <summary>Une valeur d'une liste : case « proposée » décochée = retirée de la liste dans l'application.</summary>
public sealed class ValeurDeListe : ElementParDefaut
{
    private readonly ListeParDefaut _liste;
    private bool _proposee;

    internal ValeurDeListe(ListeParDefaut liste, object valeur, string libelle, bool proposee)
    {
        _liste = liste;
        Valeur = valeur;
        Libelle = libelle;
        _proposee = proposee;
    }

    public object Valeur { get; }

    public string Libelle { get; }

    public bool Proposee
    {
        get => _proposee;
        set
        {
            if (_proposee == value)
            {
                return;
            }

            // Au moins une valeur reste proposée.
            if (!value && _liste.Valeurs.Count(v => v.Proposee) <= 1)
            {
                Notifier();
                return;
            }

            _proposee = value;
            Notifier();
            _liste.ValeurChangee(this);
        }
    }

    public override string ToString() => Libelle;
}

/// <summary>Valeur par défaut d'une liste (une liste peut en avoir plusieurs : protection extérieure, intérieure, abouts).</summary>
public sealed class DefautDeListe : ElementParDefaut
{
    private readonly ListeParDefaut _liste;
    private readonly Func<object> _lire;
    private readonly Action<object> _ecrire;

    internal DefautDeListe(ListeParDefaut liste, string titre, Func<object> lire, Action<object> ecrire)
    {
        _liste = liste;
        Titre = titre;
        _lire = lire;
        _ecrire = ecrire;
    }

    public string Titre { get; }

    public ValeurDeListe? Choix
    {
        get
        {
            string cle = ParametresParDefaut.Cle(_lire());
            return _liste.Valeurs.FirstOrDefault(v => ParametresParDefaut.Cle(v.Valeur) == cle);
        }
        set
        {
            if (value != null)
            {
                _ecrire(value.Valeur);
                Notifier();
            }
        }
    }

    public IEnumerable<ValeurDeListe> Proposees => _liste.Valeurs.Where(v => v.Proposee);

    internal void Rafraichir()
    {
        if (Choix is { Proposee: false })
        {
            // La valeur par défaut vient d'être retirée : on prend la première valeur gardée.
            Choix = _liste.Valeurs.First(v => v.Proposee);
        }

        Notifier(nameof(Proposees));
        Notifier(nameof(Choix));
    }
}

/// <summary>Une liste de choix de l'application : valeurs gardées et valeur(s) par défaut.</summary>
public sealed class ListeParDefaut : ElementParDefaut
{
    private readonly ParametresParDefaut _p;

    internal ListeParDefaut(ParametresParDefaut p, string cle, string titre, bool masquable, IEnumerable<(object Valeur, string Libelle)> valeurs)
    {
        _p = p;
        Cle = cle;
        Titre = titre;
        Masquable = masquable;
        foreach (var (v, libelle) in valeurs)
        {
            Valeurs.Add(new ValeurDeListe(this, v, libelle, !masquable || !p.EstMasque(cle, v)));
        }
    }

    public string Cle { get; }

    public string Titre { get; }

    /// <summary>Les valeurs peuvent être retirées (cases « proposée »).</summary>
    public bool Masquable { get; }

    public ObservableCollection<ValeurDeListe> Valeurs { get; } = new ObservableCollection<ValeurDeListe>();

    public ObservableCollection<DefautDeListe> Defauts { get; } = new ObservableCollection<DefautDeListe>();

    internal ListeParDefaut AvecDefaut(string titre, Func<object> lire, Action<object> ecrire)
    {
        Defauts.Add(new DefautDeListe(this, titre, lire, ecrire));
        return this;
    }

    internal void ValeurChangee(ValeurDeListe v)
    {
        _p.Masquer(Cle, v.Valeur, !v.Proposee);
        foreach (var d in Defauts)
        {
            d.Rafraichir();
        }
    }
}

/// <summary>Case à cocher de l'application et sa valeur par défaut.</summary>
public sealed class CaseParDefaut : ElementParDefaut
{
    private readonly Func<bool> _lire;
    private readonly Action<bool> _ecrire;

    internal CaseParDefaut(string titre, Func<bool> lire, Action<bool> ecrire)
    {
        Titre = titre;
        _lire = lire;
        _ecrire = ecrire;
    }

    public string Titre { get; }

    public bool Valeur
    {
        get => _lire();
        set { _ecrire(value); Notifier(); }
    }
}

/// <summary>Cote par défaut (m).</summary>
public sealed class CoteParDefaut : ElementParDefaut
{
    private readonly Func<double> _lire;
    private readonly Action<double> _ecrire;

    internal CoteParDefaut(string titre, Func<double> lire, Action<double> ecrire)
    {
        Titre = titre;
        _lire = lire;
        _ecrire = ecrire;
    }

    public string Titre { get; }

    public double Valeur
    {
        get => _lire();
        set { _ecrire(value); Notifier(); }
    }
}

/// <summary>Fichier de référence (bibliothèque, trame de devis, références clients, logo).</summary>
public sealed class FichierParDefaut : ElementParDefaut
{
    private readonly Func<string?> _lire;
    private readonly Action<string?> _ecrire;

    internal FichierParDefaut(string titre, string filtre, Func<string?> lire, Action<string?> ecrire)
    {
        Titre = titre;
        Filtre = filtre;
        _lire = lire;
        _ecrire = ecrire;
    }

    public string Titre { get; }

    /// <summary>Filtre de la boîte d'ouverture de fichier.</summary>
    public string Filtre { get; }

    public string? Chemin
    {
        get => _lire();
        set { _ecrire(string.IsNullOrWhiteSpace(value) ? null : value); Notifier(); }
    }
}

/// <summary>
/// Écran « Paramètres par défaut » (menu Outils) : travaille sur une copie, rendue par <see cref="Resultat"/> à l'enregistrement.
/// </summary>
public sealed class ParametresParDefautViewModel : ElementParDefaut
{
    /// <summary>Modes de rendu du dessin (noms de ModeRendu) et leur libellé.</summary>
    public static IReadOnlyList<(string Nom, string Libelle)> ModesRendu { get; } = new[]
    {
        ("Filaire", "Filaire"), ("LignesCachees", "Lignes cachées"), ("Ombre", "Ombré"), ("OmbreAretes", "Ombré + arêtes"), ("Realiste", "Réaliste"),
    };

    private ParametresParDefaut _p;

    public ParametresParDefautViewModel(ParametresParDefaut source)
    {
        _p = source.Copie();
        Construire();
    }

    public ParametresParDefaut Resultat => _p;

    public ObservableCollection<ListeParDefaut> Listes { get; } = new ObservableCollection<ListeParDefaut>();

    public ObservableCollection<CaseParDefaut> Cases { get; } = new ObservableCollection<CaseParDefaut>();

    public ObservableCollection<CoteParDefaut> Cotes { get; } = new ObservableCollection<CoteParDefaut>();

    public ObservableCollection<FichierParDefaut> Fichiers { get; } = new ObservableCollection<FichierParDefaut>();

    /// <summary>Remplace les paramètres affichés (import d'un fichier, retour aux paramètres d'usine).</summary>
    public void Remplacer(ParametresParDefaut p)
    {
        _p = p.Copie();
        Construire();
    }

    private void Construire()
    {
        var p = _p;
        var g = p.General;
        var f = p.Face;
        Listes.Clear();
        Cases.Clear();
        Cotes.Clear();
        Fichiers.Clear();

        Listes.Add(Liste(ListesDeChoix.Modele, "Modèle d'échafaudage", Enum.GetValues(typeof(ModeleEchafaudage)).Cast<object>())
            .AvecDefaut("Par défaut", () => g.Modele, v => g.Modele = (ModeleEchafaudage)v));
        Listes.Add(Liste(ListesDeChoix.Plateaux, "Plateaux", Enum.GetValues(typeof(GammePlateaux)).Cast<object>())
            .AvecDefaut("Par défaut", () => g.Plateaux, v => g.Plateaux = (GammePlateaux)v));
        Listes.Add(new ListeParDefaut(p, ListesDeChoix.Socle, "Socle", true, CatalogueSocles.Tous.Select(s => ((object)s.Reference, s.Libelle)))
            .AvecDefaut("Par défaut", () => g.Socle, v => g.Socle = (string)v));
        Listes.Add(Liste(ListesDeChoix.Securite, "Protections", Enum.GetValues(typeof(TypeSecurite)).Cast<object>())
            .AvecDefaut("Extérieur", () => g.SecuriteExterieure, v => g.SecuriteExterieure = (TypeSecurite)v)
            .AvecDefaut("Intérieur", () => g.SecuriteInterieure, v => g.SecuriteInterieure = (TypeSecurite)v)
            .AvecDefaut("Abouts", () => g.SecuriteAbouts, v => g.SecuriteAbouts = (TypeSecurite)v));
        Listes.Add(Liste(ListesDeChoix.Plinthe, "Plinthes", Enum.GetValues(typeof(TypePlinthe)).Cast<object>())
            .AvecDefaut("Extérieur", () => g.PlintheExterieure, v => g.PlintheExterieure = (TypePlinthe)v)
            .AvecDefaut("Intérieur", () => g.PlintheInterieure, v => g.PlintheInterieure = (TypePlinthe)v)
            .AvecDefaut("Abouts", () => g.PlintheAbouts, v => g.PlintheAbouts = (TypePlinthe)v));
        Listes.Add(Liste(ListesDeChoix.LargeurMaille, "Largeur de maille (m)", FacadeViewModel.LargeursMailleVolumique.Cast<object>())
            .AvecDefaut("Par défaut", () => f.LargeurMaille, v => f.LargeurMaille = (double)v));
        Listes.Add(Liste(ListesDeChoix.LongueurMaille, "Maille préférée (m)", FacadeViewModel.LongueursMaille.Cast<object>())
            .AvecDefaut("Par défaut", () => f.LongueurMaillePreferee, v => f.LongueurMaillePreferee = (double)v));
        Listes.Add(Liste(ListesDeChoix.Traverses, "Traverses", Enum.GetValues(typeof(TypeTraverse)).Cast<object>())
            .AvecDefaut("Par défaut", () => f.Traverses, v => f.Traverses = (TypeTraverse)v));
        Listes.Add(new ListeParDefaut(p, "Forme", "Forme de la structure", false,
                Enum.GetValues(typeof(FormeStructure)).Cast<object>().Select(v => (v, Libelles.De(v))))
            .AvecDefaut("Par défaut", () => p.Forme, v => p.Forme = (FormeStructure)v));
        Listes.Add(new ListeParDefaut(p, ListesDeChoix.ModeRendu, "Rendu du dessin", true, ModesRendu.Select(m => ((object)m.Nom, m.Libelle)))
            .AvecDefaut("Par défaut", () => p.ModeRendu, v => p.ModeRendu = (string)v));
        Listes.Add(new ListeParDefaut(p, "ReferencesAffichees", "Références du décompte", false,
                FacadeViewModel.ChoixReferences.Select(c => ((object)c.Key, c.Value)))
            .AvecDefaut("Par défaut", () => p.ReferencesAffichees, v => p.ReferencesAffichees = (string)v));
        Listes.Add(Liste(ListesDeChoix.TypeMaille, "Types de maille (grille)", Enum.GetValues(typeof(TypeMaille)).Cast<object>()));
        Listes.Add(Liste(ListesDeChoix.TypePlancher, "Types de plancher (grille)", FacadeViewModel.TypesPlancher.Cast<object>()));
        Listes.Add(new ListeParDefaut(p, ListesDeChoix.Console, "Consoles", true, CatalogueConsoles.Liste.Select(c => ((object)c.Reference, c.Reference))));
        Listes.Add(Liste(ListesDeChoix.Stabilisation, "Stabilisation (m)", FacadeViewModel.LargeursStabilisation.Cast<object>()));
        Listes.Add(Liste(ListesDeChoix.ProfondeurRecette, "Profondeur de recette (m)", FacadeViewModel.ProfondeursRecette.Cast<object>()));

        Cases.Add(new CaseParDefaut("Lisses en pied", () => f.LissesEnPied, v => f.LissesEnPied = v));
        Cases.Add(new CaseParDefaut("Départ poteaux 3 m côté extérieur", () => f.Depart3mExterieur, v => f.Depart3mExterieur = v));
        Cases.Add(new CaseParDefaut("Garde-corps 1,50 m au dernier niveau", () => f.GardeCorpsCouvreur, v => f.GardeCorpsCouvreur = v));
        Cases.Add(new CaseParDefaut("Grutable (accessoires de levage)", () => f.Grutable, v => f.Grutable = v));
        Cases.Add(new CaseParDefaut("Diagonales 1 maille sur 2", () => f.DiagonalesRenforcees, v => f.DiagonalesRenforcees = v));
        Cases.Add(new CaseParDefaut("Diagonale dans la 1re maille", () => f.DiagonalePremiereMaille, v => f.DiagonalePremiereMaille = v));
        Cases.Add(new CaseParDefaut("Trappes et plateaux acier", () => f.TrappesAcier, v => f.TrappesAcier = v));
        Cases.Add(new CaseParDefaut("Lisse GC accès 1,50 m au niveau 0", () => f.LisseGcAccesNiveau0, v => f.LisseGcAccesNiveau0 = v));
        Cases.Add(new CaseParDefaut("About gauche", () => g.AboutGauche, v => g.AboutGauche = v));
        Cases.Add(new CaseParDefaut("About droit", () => g.AboutDroit, v => g.AboutDroit = v));
        Cases.Add(new CaseParDefaut("Échafaudage bâché", () => g.Bache, v => g.Bache = v));
        Cases.Add(new CaseParDefaut("Dessiner façade", () => p.DessinerFacade, v => p.DessinerFacade = v));

        Cotes.Add(new CoteParDefaut("Longueur souhaitée (m)", () => f.LongueurTotaleSouhaitee, v => f.LongueurTotaleSouhaitee = v));
        Cotes.Add(new CoteParDefaut("Dernier plancher (m)", () => f.HauteurDernierPlancher, v => f.HauteurDernierPlancher = v));
        Cotes.Add(new CoteParDefaut("Distance à la façade (m)", () => f.DistanceFacade, v => f.DistanceFacade = v));

        Fichiers.Add(new FichierParDefaut("Bibliothèque de blocs", "Bibliothèque de blocs (*.dwg;*.dxf)|*.dwg;*.dxf", () => p.Bibliotheque, v => p.Bibliotheque = v));
        Fichiers.Add(new FichierParDefaut("Trame Excel des devis", "Classeurs Excel (*.xlsx;*.xlsm;*.xltx;*.xls)|*.xlsx;*.xlsm;*.xltx;*.xls", () => p.ModeleDevis, v => p.ModeleDevis = v));
        Fichiers.Add(new FichierParDefaut("Références clients", "Références clients (*.xlsx;*.xls;*.csv;*.txt;*.bib)|*.xlsx;*.xls;*.csv;*.txt;*.bib|Tous les fichiers|*.*", () => p.ReferencesClients, v => p.ReferencesClients = v));
        Fichiers.Add(new FichierParDefaut("Logo client", "Images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp", () => p.LogoClient, v => p.LogoClient = v));
        Notifier(string.Empty);
    }

    private ListeParDefaut Liste(string cle, string titre, IEnumerable<object> valeurs) =>
        new ListeParDefaut(_p, cle, titre, true, valeurs.Select(v => (v, v is double d ? d.ToString("0.00") : Libelles.De(v))));
}
