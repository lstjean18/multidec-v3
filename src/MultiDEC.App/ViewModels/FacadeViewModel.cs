using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using MultiDEC.Core.Catalogue;
using MultiDEC.Core.Decompte;
using MultiDEC.Core.Facade;
using MultiDEC.Core.Facade.Regles;
using MultiDEC.Core.Geometrie;
using MultiDEC.Core.Persistance;

namespace MultiDEC.App.ViewModels;

/// <summary>
/// Écran du module façade : paramètres, grille des mailles et des niveaux (éditables), génération.
/// Ne dépend ni de WPF ni de VectorDraw : la vue appelle <see cref="Generer"/> puis dessine le résultat.
/// </summary>
public sealed class FacadeViewModel : INotifyPropertyChanged
{
    private ProjetFacade _projet = new ProjetFacade();
    private StructureFacade? _structure;
    private string? _cheminFichier;

    /// <summary>Hauteur connue de chaque niveau, pour que les cases « ConXm » suivent un changement de hauteur.</summary>
    private readonly Dictionary<Niveau, double> _hauteurs = new Dictionary<Niveau, double>();

    public FacadeViewModel()
    {
        Proposer = new Commande(ProposerStructure);
        ProposerLesDiagonales = new Commande(ProposerDiagonales);
        Niveaux.CollectionChanged += Niveaux_CollectionChanged;
        ChargerProjet(ProposeurStructure.NouveauProjet(new ParametresFacade()));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Les colonnes « ConXm » de la grille des mailles sont à reconstruire (console ajoutée, retirée, hauteur changée).</summary>
    public event EventHandler? ColonnesConsolesChangees;

    // ---- Listes de choix --------------------------------------------------------------------

    public static IReadOnlyList<double> LargeursMaille { get; } = new[] { 0.70, 0.75, 1.00, 1.05 };

    public static IReadOnlyList<double> LongueursMaille { get; } = new[] { 0.70, 0.75, 1.00, 1.05, 1.50, 2.00, 2.50, 3.00 };

    public static Array Modeles { get; } = Enum.GetValues(typeof(ModeleEchafaudage));

    public static Array Securites { get; } = Enum.GetValues(typeof(TypeSecurite));

    public static Array Plinthes { get; } = Enum.GetValues(typeof(TypePlinthe));

    public static Array TypesMaille { get; } = Enum.GetValues(typeof(TypeMaille));

    public static Array TypesPlancher { get; } = Enum.GetValues(typeof(TypePlancher));

    /// <summary>Gammes de plateaux avec leur libellé (« Standard NF », « Anti Contamination NF »…).</summary>
    public static IReadOnlyList<KeyValuePair<GammePlateaux, string>> ChoixPlateaux { get; } =
        Enum.GetValues(typeof(GammePlateaux)).Cast<GammePlateaux>()
            .Select(g => new KeyValuePair<GammePlateaux, string>(g, LibellesPlateaux.Libelle(g))).ToList();

    /// <summary>Consoles du modèle choisi, précédées d'une entrée vide (= sans console).</summary>
    public IReadOnlyList<string> Consoles =>
        new[] { string.Empty }.Concat(CatalogueConsoles.Pour(Parametres.Modele).Select(c => c.Reference)).ToList();

    // ---- Données ----------------------------------------------------------------------------

    public ProjetFacade Projet => _projet;

    public ParametresFacade Parametres => _projet.Parametres;

    /// <summary>Longueur de façade souhaitée : la modifier recalcule le nombre d'accès proposé.</summary>
    public double LongueurFacade
    {
        get => Parametres.LongueurTotaleSouhaitee;
        set
        {
            if (Math.Abs(Parametres.LongueurTotaleSouhaitee - value) < 1e-9)
            {
                return;
            }

            Parametres.LongueurTotaleSouhaitee = value;
            Notifier();
            NombreAcces = RegleAcces.NombreConseille(value);
        }
    }

    /// <summary>Nombre d'accès : proposé d'après la longueur, modifiable par l'utilisateur.</summary>
    public int NombreAcces
    {
        get => Parametres.NombreAcces;
        set
        {
            if (Parametres.NombreAcces != value)
            {
                Parametres.NombreAcces = value;
                Notifier();
            }
        }
    }

    public ObservableCollection<MailleDefinition> Mailles { get; } = new ObservableCollection<MailleDefinition>();

    public ObservableCollection<Niveau> Niveaux { get; } = new ObservableCollection<Niveau>();

    public ObservableCollection<LigneDecompte> Decompte { get; } = new ObservableCollection<LigneDecompte>();

    public ObservableCollection<string> Alertes { get; } = new ObservableCollection<string>();

    public string? CheminFichier
    {
        get => _cheminFichier;
        private set => Changer(ref _cheminFichier, value);
    }

    public string Resume => _structure == null
        ? $"{Mailles.Count} mailles — {LongueurObtenue:0.00} m"
        : $"{Mailles.Count} mailles — {LongueurObtenue:0.00} m — {_structure.Elements.Count} éléments — {_structure.Amarrages.Amarrages.Count} amarrages";

    public double LongueurObtenue => Mailles.Sum(m => m.Longueur);

    /// <summary>Niveaux qui ont une console : une colonne de cases « ConXm » chacun dans la grille des mailles.</summary>
    public IReadOnlyList<Niveau> NiveauxAvecConsole => Niveaux.Where(n => n.AConsole).OrderBy(n => n.Hauteur).ToList();

    public StructureFacade? Structure => _structure;

    public Commande Proposer { get; }

    public Commande ProposerLesDiagonales { get; }

    // ---- Actions ----------------------------------------------------------------------------

    /// <summary>Remplace les deux grilles par la proposition calculée depuis les paramètres.</summary>
    public void ProposerStructure()
    {
        // Niveaux d'abord : retirer un niveau à console décoche les mailles en place, pas les nouvelles.
        Remplir(Niveaux, ProposeurStructure.ProposerNiveaux(Parametres));
        Remplir(Mailles, ProposeurStructure.ProposerMailles(Parametres));
        Notifier(nameof(Resume));
    }

    /// <summary>Recoche les cases « Diagonale » selon la règle (une maille sur 5, ou sur 2 avec l'option).</summary>
    public void ProposerDiagonales() => RegleDiagonales.Proposer(Parametres, Mailles);

    /// <summary>Recopie les grilles éditées dans le projet et génère la structure.</summary>
    public StructureFacade Generer()
    {
        _projet.Mailles = Mailles.ToList();
        _projet.Niveaux = Niveaux.ToList();
        _structure = GenerateurFacade.Generer(_projet);

        Remplir(Decompte, CalculDecompte.Calculer(_structure.Elements));
        Remplir(Alertes, _structure.Alertes);
        Notifier(nameof(Structure));
        Notifier(nameof(Resume));
        return _structure;
    }

    /// <summary>Retire de la structure les pièces supprimées dans le dessin et recalcule le décompte.</summary>
    public void RetirerElements(IReadOnlyCollection<ElementPlace> retires)
    {
        if (_structure == null || retires.Count == 0)
        {
            return;
        }

        _structure = _structure.SansElements(retires);
        Remplir(Decompte, CalculDecompte.Calculer(_structure.Elements));
        Notifier(nameof(Structure));
        Notifier(nameof(Resume));
    }

    public void Nouveau()
    {
        ChargerProjet(ProposeurStructure.NouveauProjet(new ParametresFacade()));
        CheminFichier = null;
    }

    public void Ouvrir(string chemin)
    {
        ChargerProjet(FichierProjet.Ouvrir(chemin));
        CheminFichier = chemin;
    }

    public void Enregistrer(string chemin)
    {
        _projet.Mailles = Mailles.ToList();
        _projet.Niveaux = Niveaux.ToList();
        FichierProjet.Enregistrer(_projet, chemin);
        CheminFichier = chemin;
    }

    /// <summary>À appeler quand le modèle d'échafaudage change (la liste des consoles en dépend).</summary>
    public void ModeleChange() => Notifier(nameof(Consoles));

    private void ChargerProjet(ProjetFacade projet)
    {
        _projet = projet;
        _structure = null;
        Remplir(Niveaux, projet.Niveaux.OrderByDescending(n => n.Hauteur));
        Remplir(Mailles, projet.Mailles);
        Decompte.Clear();
        Alertes.Clear();
        Notifier(string.Empty); // tout rafraîchir
    }

    private void Niveaux_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var n in _hauteurs.Keys.Except(Niveaux).ToList())
        {
            n.PropertyChanged -= Niveau_PropertyChanged;
            if (n.AConsole && !Niveaux.Any(autre => autre.AConsole && Cotes.Egal(autre.Hauteur, n.Hauteur)))
            {
                CocherConsoles(n.Hauteur, false);
            }

            _hauteurs.Remove(n);
        }

        foreach (var n in Niveaux.Where(n => !_hauteurs.ContainsKey(n)))
        {
            n.PropertyChanged += Niveau_PropertyChanged;
            _hauteurs[n] = n.Hauteur;
        }

        ColonnesConsolesChangees?.Invoke(this, EventArgs.Empty);
    }

    private void Niveau_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var n = (Niveau)sender!;
        if (e.PropertyName == nameof(Niveau.AConsole))
        {
            // Console ajoutée au niveau : cochée sur toutes les mailles (à décocher là où il n'y en a pas) ;
            // console retirée : cases effacées.
            CocherConsoles(n.Hauteur, n.AConsole);
            ColonnesConsolesChangees?.Invoke(this, EventArgs.Empty);
        }
        else if (e.PropertyName == nameof(Niveau.Hauteur))
        {
            double ancienne = _hauteurs[n];
            _hauteurs[n] = n.Hauteur;
            if (n.AConsole)
            {
                foreach (var m in Mailles)
                {
                    m.DeplacerConsole(ancienne, n.Hauteur);
                }

                ColonnesConsolesChangees?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private void CocherConsoles(double hauteur, bool coche)
    {
        foreach (var m in Mailles)
        {
            m.DefinirConsole(hauteur, coche);
        }
    }

    private static void Remplir<T>(ObservableCollection<T> cible, IEnumerable<T> source)
    {
        cible.Clear();
        foreach (var item in source)
        {
            cible.Add(item);
        }
    }

    private void Changer<T>(ref T champ, T valeur, [CallerMemberName] string? nom = null)
    {
        if (!EqualityComparer<T>.Default.Equals(champ, valeur))
        {
            champ = valeur;
            Notifier(nom);
        }
    }

    private void Notifier([CallerMemberName] string? nom = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nom));
}
