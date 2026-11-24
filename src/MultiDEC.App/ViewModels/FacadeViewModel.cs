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
/// Écran principal : paramètres généraux, forme de la structure, une face active (S1…S4) dont les grilles
/// des mailles et des niveaux sont éditables, génération de toute la structure.
/// Ne dépend ni de WPF ni de VectorDraw : la vue appelle <see cref="Generer"/> puis dessine le résultat.
/// </summary>
public sealed class FacadeViewModel : INotifyPropertyChanged
{
    private ProjetStructure _projet = new ProjetStructure();
    private int _indexFace;
    private StructureFacade? _structure;
    private string? _cheminFichier;

    /// <summary>Hauteur connue de chaque niveau, pour que les cases « ConXm » suivent un changement de hauteur.</summary>
    private readonly Dictionary<Niveau, double> _hauteurs = new Dictionary<Niveau, double>();

    private readonly HashSet<MailleDefinition> _maillesSuivies = new HashSet<MailleDefinition>();

    public FacadeViewModel()
    {
        Proposer = new Commande(ProposerStructure);
        ProposerLesDiagonales = new Commande(ProposerDiagonales);
        CopierS1 = new Commande(CopierDepuisS1);
        Niveaux.CollectionChanged += Niveaux_CollectionChanged;
        Mailles.CollectionChanged += Mailles_CollectionChanged;
        Alertes.CollectionChanged += (_, _) => Notifier(nameof(TitreAlertes));
        ChargerProjet(ProjetStructure.Nouveau());
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

    public static Array Formes { get; } = Enum.GetValues(typeof(FormeStructure));

    /// <summary>Gammes de plateaux avec leur libellé (« Standard NF », « Anti Contamination NF »…).</summary>
    public static IReadOnlyList<KeyValuePair<GammePlateaux, string>> ChoixPlateaux { get; } =
        Enum.GetValues(typeof(GammePlateaux)).Cast<GammePlateaux>()
            .Select(g => new KeyValuePair<GammePlateaux, string>(g, LibellesPlateaux.Libelle(g))).ToList();

    /// <summary>Consoles du modèle choisi, précédées d'une entrée vide (= sans console).</summary>
    public IReadOnlyList<string> Consoles =>
        new[] { string.Empty }.Concat(CatalogueConsoles.Pour(General.Modele).Select(c => c.Reference)).ToList();

    // ---- Données ----------------------------------------------------------------------------

    /// <summary>Le projet : informations d'étude, paramètres généraux, forme et faces.</summary>
    public ProjetStructure Projet => _projet;

    /// <summary>Paramètres communs à toutes les faces (onglet « Général »).</summary>
    public ParametresGeneraux General => _projet.General;

    /// <summary>Forme de la structure : la changer crée ou retire des faces (S1 à S4).</summary>
    public FormeStructure Forme
    {
        get => _projet.Forme;
        set
        {
            if (_projet.Forme == value)
            {
                return;
            }

            SauverGrilles();
            _projet.Forme = value;
            _projet.AjusterFaces();
            Remplir(Faces, _projet.Faces);
            _structure = null;
            Notifier();
            ChargerFace(Math.Min(_indexFace, Faces.Count - 1));
        }
    }

    /// <summary>Faces de la structure (un onglet chacune).</summary>
    public ObservableCollection<FaceEchafaudage> Faces { get; } = new ObservableCollection<FaceEchafaudage>();

    /// <summary>Indice de la face dont les paramètres et les grilles sont affichés.</summary>
    public int IndexFaceActive
    {
        get => _indexFace;
        set
        {
            if (value == _indexFace || value < 0 || value >= Faces.Count)
            {
                return;
            }

            SauverGrilles();
            ChargerFace(value);
        }
    }

    public FaceEchafaudage FaceActive => _projet.Faces[_indexFace];

    public string NomFaceActive => FaceActive.Nom;

    /// <summary>« Copier depuis S1 » n'a de sens que sur S2, S3, S4.</summary>
    public bool PeutCopierS1 => _indexFace > 0;

    /// <summary>Paramètres de la face active.</summary>
    public ParametresFacade Parametres => FaceActive.Parametres;

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

    /// <summary>Amarrages placés par la dernière génération (onglet « Amarrages »).</summary>
    public ObservableCollection<Amarrage> ListeAmarrages { get; } = new ObservableCollection<Amarrage>();

    /// <summary>Titre de l'onglet des grilles : « Mailles et niveaux · S2 ».</summary>
    public string TitreGrilles => "Mailles et niveaux · " + NomFaceActive;

    public string TitreAlertes => Alertes.Count == 0 ? "Alertes" : $"Alertes ({Alertes.Count})";

    /// <summary>Résultat de la face active : longueur obtenue, hauteur, mailles et niveaux.</summary>
    public string ResumeFace => FormattableString.Invariant(
        $"Obtenue {LongueurObtenue:0.00} m · {Mailles.Count} mailles · {Niveaux.Count} niveaux · ht. {FaceActive.HauteurTotale:0.00} m").Replace('.', ',');

    public string? CheminFichier
    {
        get => _cheminFichier;
        private set => Changer(ref _cheminFichier, value);
    }

    public string Resume => _structure == null
        ? $"{NomFaceActive} : {Mailles.Count} mailles — {LongueurObtenue:0.00} m"
        : $"{NomFaceActive} : {Mailles.Count} mailles — {LongueurObtenue:0.00} m — structure : {_structure.Elements.Count} éléments — {_structure.Amarrages.Amarrages.Count} amarrages";

    public double LongueurObtenue => Mailles.Sum(m => m.Longueur);

    /// <summary>Niveaux qui ont une console : une colonne de cases « ConXm » chacun dans la grille des mailles.</summary>
    public IReadOnlyList<Niveau> NiveauxAvecConsole => Niveaux.Where(n => n.AConsole).OrderBy(n => n.Hauteur).ToList();

    public StructureFacade? Structure => _structure;

    public Commande Proposer { get; }

    public Commande ProposerLesDiagonales { get; }

    public Commande CopierS1 { get; }

    // ---- Actions ----------------------------------------------------------------------------

    /// <summary>Remplace les deux grilles de la face active par la proposition calculée depuis ses paramètres.</summary>
    public void ProposerStructure()
    {
        _projet.ProposerFace(_indexFace);
        ChargerFace(_indexFace);
    }

    /// <summary>Recopie les paramètres et les grilles de S1 dans la face active.</summary>
    public void CopierDepuisS1()
    {
        if (_indexFace == 0)
        {
            return;
        }

        SauverGrilles();
        _projet.CopierDepuisS1(_indexFace);
        Faces[_indexFace] = _projet.Faces[_indexFace];
        ChargerFace(_indexFace);
    }

    /// <summary>Recoche les cases « Diagonale » selon la règle (une maille sur 5, ou sur 2 avec l'option).</summary>
    public void ProposerDiagonales() => RegleDiagonales.Proposer(Parametres, Mailles);

    /// <summary>Recopie les grilles éditées dans la face active et génère toute la structure.</summary>
    public StructureFacade Generer()
    {
        SauverGrilles();
        _structure = GenerateurStructure.Generer(_projet);

        Remplir(Decompte, CalculDecompte.Calculer(_structure.Elements));
        Remplir(Alertes, _structure.Alertes);
        Remplir(ListeAmarrages, _structure.Amarrages.Amarrages);
        Notifier(nameof(Structure));
        Notifier(nameof(Resume));
        return _structure;
    }

    /// <summary>Ajoute une maille après <paramref name="index"/> (ou à la fin si -1), copie de la maille de référence.</summary>
    public int AjouterMaille(int index)
    {
        int position = index < 0 || index >= Mailles.Count ? Mailles.Count : index + 1;
        Mailles.Insert(position, NouvelleMaille(position - 1));
        return position;
    }

    /// <summary>Insère une maille avant <paramref name="index"/> (ou au début si -1).</summary>
    public int InsererMailleAvant(int index)
    {
        int position = index < 0 || index >= Mailles.Count ? 0 : index;
        Mailles.Insert(position, NouvelleMaille(position));
        return position;
    }

    public void SupprimerMaille(int index)
    {
        if (index >= 0 && index < Mailles.Count)
        {
            Mailles.RemoveAt(index);
        }
    }

    /// <summary>Nouvelle maille : longueur préférée de la face, consoles comme sa voisine, sans diagonale.</summary>
    private MailleDefinition NouvelleMaille(int voisine)
    {
        var m = new MailleDefinition(Parametres.LongueurMaillePreferee);
        if (voisine >= 0 && voisine < Mailles.Count)
        {
            m.Consoles = new List<double>(Mailles[voisine].Consoles);
        }

        return m;
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
        ChargerProjet(ProjetStructure.Nouveau());
        CheminFichier = null;
    }

    public void Ouvrir(string chemin)
    {
        ChargerProjet(FichierProjet.Ouvrir(chemin));
        CheminFichier = chemin;
    }

    public void Enregistrer(string chemin)
    {
        SauverGrilles();
        FichierProjet.Enregistrer(_projet, chemin);
        CheminFichier = chemin;
    }

    /// <summary>À appeler quand le modèle d'échafaudage change (la liste des consoles en dépend).</summary>
    public void ModeleChange()
    {
        _projet.AppliquerGeneraux();
        Notifier(nameof(Consoles));
    }

    private void ChargerProjet(ProjetStructure projet)
    {
        _projet = projet;
        _projet.AppliquerGeneraux();
        _structure = null;
        Remplir(Faces, projet.Faces);
        Decompte.Clear();
        Alertes.Clear();
        ListeAmarrages.Clear();
        Notifier(string.Empty); // tout rafraîchir
        ChargerFace(0);
    }

    /// <summary>Recopie les grilles affichées dans la face active.</summary>
    private void SauverGrilles()
    {
        if (_indexFace < _projet.Faces.Count)
        {
            FaceActive.Mailles = Mailles.ToList();
            FaceActive.Niveaux = Niveaux.ToList();
        }
    }

    /// <summary>Affiche les paramètres et les grilles d'une face.</summary>
    private void ChargerFace(int index)
    {
        _indexFace = index;
        var face = FaceActive;
        face.Parametres.AppliquerGeneraux(General);

        // Niveaux d'abord : retirer un niveau à console décoche les mailles en place, pas les nouvelles.
        Remplir(Niveaux, face.Niveaux.OrderByDescending(n => n.Hauteur));
        Remplir(Mailles, face.Mailles);
        MarquerJonctions();
        Notifier(nameof(IndexFaceActive));
        Notifier(nameof(FaceActive));
        Notifier(nameof(NomFaceActive));
        Notifier(nameof(PeutCopierS1));
        Notifier(nameof(Parametres));
        Notifier(nameof(LongueurFacade));
        Notifier(nameof(NombreAcces));
        Notifier(nameof(LongueurObtenue));
        Notifier(nameof(Resume));
        Notifier(nameof(TitreGrilles));
        Notifier(nameof(ResumeFace));
        ColonnesConsolesChangees?.Invoke(this, EventArgs.Empty);
    }

    private void Mailles_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Abonnements suivis à part : Clear() (changement de face) ne donne pas les anciennes mailles.
        foreach (var m in _maillesSuivies.Except(Mailles).ToList())
        {
            m.PropertyChanged -= Maille_PropertyChanged;
            _maillesSuivies.Remove(m);
        }

        foreach (var m in Mailles.Where(_maillesSuivies.Add))
        {
            m.PropertyChanged += Maille_PropertyChanged;
        }

        MarquerJonctions();
        NotifierLongueur();
    }

    private void Maille_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MailleDefinition.Longueur))
        {
            NotifierLongueur();
        }
    }

    private void NotifierLongueur()
    {
        Notifier(nameof(LongueurObtenue));
        Notifier(nameof(ResumeFace));
        Notifier(nameof(Resume));
    }

    /// <summary>Colonne « Jonction » : carré d'angle en tête de face, raccordé à la face précédente.</summary>
    private void MarquerJonctions()
    {
        foreach (var m in Mailles)
        {
            m.Jonction = string.Empty;
        }

        if (Mailles.Count > 0 && _projet.CommenceParCarreDAngle(_indexFace))
        {
            int precedente = _indexFace == 0 ? _projet.Faces.Count - 1 : _indexFace - 1;
            Mailles[0].Jonction = _projet.Faces[precedente].Nom;
        }
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
        Notifier(nameof(ResumeFace));
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
