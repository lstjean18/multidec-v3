using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using MultiDEC.Core.Catalogue;
using MultiDEC.Core.Decompte;
using MultiDEC.Core.Facade;
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

    public FacadeViewModel()
    {
        Proposer = new Commande(ProposerStructure);
        ChargerProjet(ProposeurStructure.NouveauProjet(new ParametresFacade()));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // ---- Listes de choix --------------------------------------------------------------------

    public static IReadOnlyList<double> LargeursMaille { get; } = new[] { 0.70, 0.75, 1.00, 1.05 };

    public static IReadOnlyList<double> LongueursMaille { get; } = new[] { 0.70, 0.75, 1.00, 1.05, 1.50, 2.00, 2.50, 3.00 };

    public static Array Modeles { get; } = Enum.GetValues(typeof(ModeleEchafaudage));

    public static Array Securites { get; } = Enum.GetValues(typeof(TypeSecurite));

    public static Array Plinthes { get; } = Enum.GetValues(typeof(TypePlinthe));

    public static Array TypesMaille { get; } = Enum.GetValues(typeof(TypeMaille));

    public static Array TypesPlancher { get; } = Enum.GetValues(typeof(TypePlancher));

    public static Array GammesPlateaux { get; } = Enum.GetValues(typeof(GammePlateaux));

    /// <summary>Consoles de la gamme choisie, précédées d'une entrée vide (= sans console).</summary>
    public IReadOnlyList<string> Consoles =>
        new[] { string.Empty }.Concat(CatalogueConsoles.Pour(Parametres.Modele).Select(c => c.Reference)).ToList();

    // ---- Données ----------------------------------------------------------------------------

    public ProjetFacade Projet => _projet;

    public ParametresFacade Parametres => _projet.Parametres;

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

    public StructureFacade? Structure => _structure;

    public Commande Proposer { get; }

    // ---- Actions ----------------------------------------------------------------------------

    /// <summary>Remplace les deux grilles par la proposition calculée depuis les paramètres.</summary>
    public void ProposerStructure()
    {
        Remplir(Mailles, ProposeurStructure.ProposerMailles(Parametres));
        Remplir(Niveaux, ProposeurStructure.ProposerNiveaux(Parametres));
        Notifier(nameof(Resume));
    }

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

    /// <summary>À appeler quand la gamme change (la liste des consoles en dépend).</summary>
    public void GammeChangee() => Notifier(nameof(Consoles));

    private void ChargerProjet(ProjetFacade projet)
    {
        _projet = projet;
        _structure = null;
        Remplir(Mailles, projet.Mailles);
        Remplir(Niveaux, projet.Niveaux.OrderByDescending(n => n.Hauteur));
        Decompte.Clear();
        Alertes.Clear();
        Notifier(string.Empty); // tout rafraîchir
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
