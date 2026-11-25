using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
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
        AjouterModule = new Commande(() =>
        {
            if (NombreModules < ProjetStructure.ModulesMaxi)
            {
                NombreModules++;
                IndexFaceActive = Faces.Count - 1;
            }
        });
        Niveaux.CollectionChanged += Niveaux_CollectionChanged;
        Mailles.CollectionChanged += Mailles_CollectionChanged;
        Alertes.CollectionChanged += (_, _) => Notifier(nameof(TitreAlertes));
        ChargerProjet(_defauts.NouveauProjet());
        _dessinerFacade = _defauts.DessinerFacade;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Les colonnes « ConXm » de la grille des mailles sont à reconstruire (console ajoutée, retirée, hauteur changée).</summary>
    public event EventHandler? ColonnesConsolesChangees;

    // ---- Listes de choix --------------------------------------------------------------------

    public static IReadOnlyList<double> LargeursMaille { get; } = new[] { 0.70, 0.75, 1.00, 1.05 };

    /// <summary>Largeurs au-delà de 1,05 m : seulement pour une structure volumique (Laurent, 24/11/2026).</summary>
    public static IReadOnlyList<double> LargeursMailleVolumique { get; } = new[] { 0.70, 0.75, 1.00, 1.05, 1.50, 2.00, 2.50, 3.00 };

    /// <summary>Largeurs proposées pour la face ou le module actif.</summary>
    public IReadOnlyList<double> LargeursProposees =>
        Proposees(ListesDeChoix.LargeurMaille, EstVolumique ? LargeursMailleVolumique : LargeursMaille, new[] { Parametres.LargeurMaille });

    public static IReadOnlyList<double> LongueursMaille { get; } = new[] { 0.70, 0.75, 1.00, 1.05, 1.50, 2.00, 2.50, 3.00 };

    /// <summary>Largeurs de stabilisation proposées (0 = sans).</summary>
    public static IReadOnlyList<double> LargeursStabilisation { get; } = new[] { 0.0, 0.70, 1.00, 1.50, 2.00, 2.50, 3.00 };


    /// <summary>Profondeurs de recette proposées.</summary>
    public static IReadOnlyList<double> ProfondeursRecette { get; } = new[] { 0.70, 1.00, 1.50, 2.00, 2.50, 3.00 };

    public static Array Modeles { get; } = Enum.GetValues(typeof(ModeleEchafaudage));

    public static Array Securites { get; } = Enum.GetValues(typeof(TypeSecurite));

    public static Array Plinthes { get; } = Enum.GetValues(typeof(TypePlinthe));

    public static Array TypesMaille { get; } = Enum.GetValues(typeof(TypeMaille));

    /// <summary>« Accès seul + diago H » n'est plus un type : c'est la case Diago H du niveau.</summary>
    public static IReadOnlyList<TypePlancher> TypesPlancher { get; } = new[] { TypePlancher.Complet, TypePlancher.AccesSeul };

    public static Array Formes { get; } = Enum.GetValues(typeof(FormeStructure));

    public static Array TypesTraverse { get; } = Enum.GetValues(typeof(TypeTraverse));

    public static IReadOnlyList<SocleInfo> Socles => CatalogueSocles.Tous;

    /// <summary>Gammes de plateaux avec leur libellé (« Standard NF », « Anti Contamination NF »…).</summary>
    public static IReadOnlyList<KeyValuePair<GammePlateaux, string>> ChoixPlateaux { get; } =
        Enum.GetValues(typeof(GammePlateaux)).Cast<GammePlateaux>()
            .Select(g => new KeyValuePair<GammePlateaux, string>(g, LibellesPlateaux.Libelle(g))).ToList();

    /// <summary>Consoles du modèle choisi, précédées d'une entrée vide (= sans console), sans celles retirées par l'utilisateur.</summary>
    public IReadOnlyList<string> Consoles =>
        new[] { string.Empty }.Concat(Proposees(ListesDeChoix.Console, CatalogueConsoles.Pour(General.Modele).Select(c => c.Reference),
                                                Niveaux.Select(n => n.Console ?? string.Empty))).ToList();

    // ---- Paramètres par défaut : listes réduites aux valeurs gardées par l'utilisateur ------------

    /// <summary>Paramètres par défaut de l'utilisateur : valeurs du « Nouveau » et valeurs retirées des listes.</summary>
    public ParametresParDefaut Defauts
    {
        get => _defauts;
        set
        {
            _defauts = value ?? ParametresParDefaut.Usine();
            NotifierListes();
        }
    }

    public IReadOnlyList<ModeleEchafaudage> ChoixModeles =>
        Proposees(ListesDeChoix.Modele, Modeles.Cast<ModeleEchafaudage>(), new[] { General.Modele });

    public IReadOnlyList<KeyValuePair<GammePlateaux, string>> ChoixGammes =>
        Proposees(ListesDeChoix.Plateaux, ChoixPlateaux, new[] { ChoixPlateaux.First(k => k.Key == General.Plateaux) }, k => k.Key);

    public IReadOnlyList<SocleInfo> ChoixSocles =>
        Proposees(ListesDeChoix.Socle, Socles, Socles.Where(x => x.Reference == General.Socle), x => x.Reference);

    public IReadOnlyList<TypeSecurite> ChoixSecurites =>
        Proposees(ListesDeChoix.Securite, Securites.Cast<TypeSecurite>(), new[] { General.SecuriteExterieure, General.SecuriteInterieure, General.SecuriteAbouts });

    public IReadOnlyList<TypePlinthe> ChoixPlinthes =>
        Proposees(ListesDeChoix.Plinthe, Plinthes.Cast<TypePlinthe>(), new[] { General.PlintheExterieure, General.PlintheInterieure, General.PlintheAbouts });

    public IReadOnlyList<double> ChoixLongueursMaille =>
        Proposees(ListesDeChoix.LongueurMaille, LongueursMaille, new[] { Parametres.LongueurMaillePreferee });

    public IReadOnlyList<TypeTraverse> ChoixTraverses =>
        Proposees(ListesDeChoix.Traverses, TypesTraverse.Cast<TypeTraverse>(), new[] { Parametres.Traverses });

    public IReadOnlyList<TypeMaille> ChoixTypesMaille =>
        Proposees(ListesDeChoix.TypeMaille, TypesMaille.Cast<TypeMaille>(), Mailles.Select(m => m.Type));

    public IReadOnlyList<TypePlancher> ChoixTypesPlancher =>
        Proposees(ListesDeChoix.TypePlancher, TypesPlancher, Niveaux.Select(n => n.Type));

    public IReadOnlyList<double> ChoixStabilisation =>
        Proposees(ListesDeChoix.Stabilisation, LargeursStabilisation, Mailles.Select(m => m.Stabilisation));

    public IReadOnlyList<double> ChoixProfondeursRecette =>
        Proposees(ListesDeChoix.ProfondeurRecette, ProfondeursRecette, Mailles.Select(m => m.ProfondeurRecette));

    /// <summary>Valeurs non retirées, plus celles en cours d'utilisation ; liste complète si tout est retiré.</summary>
    private IReadOnlyList<T> Proposees<T>(string liste, IEnumerable<T> toutes, IEnumerable<T> enCours, Func<T, object?>? cle = null)
    {
        cle ??= v => v;
        var utilisees = new HashSet<string>(enCours.Select(v => ParametresParDefaut.Cle(cle(v))));
        var tout = toutes.ToList();
        var gardees = tout.Where(v => !_defauts.EstMasque(liste, cle(v)) || utilisees.Contains(ParametresParDefaut.Cle(cle(v)))).ToList();
        return gardees.Count == 0 ? tout : gardees;
    }

    private void NotifierListes()
    {
        foreach (var nom in new[]
        {
            nameof(Consoles), nameof(ChoixModeles), nameof(ChoixGammes), nameof(ChoixSocles), nameof(ChoixSecurites), nameof(ChoixPlinthes),
            nameof(LargeursProposees), nameof(ChoixLongueursMaille), nameof(ChoixTraverses), nameof(ChoixTypesMaille), nameof(ChoixTypesPlancher),
            nameof(ChoixStabilisation), nameof(ChoixProfondeursRecette),
        })
        {
            Notifier(nom);
        }
    }

    // ---- Données ----------------------------------------------------------------------------

    /// <summary>Le projet : informations d'étude, paramètres généraux, forme et faces.</summary>
    public ProjetStructure Projet => _projet;

    /// <summary>Paramètres communs à toutes les faces (onglet « Général »).</summary>
    public ParametresGeneraux General => _projet.General;

    // Protections : choisir une protection bascule la plinthe du même côté (ReglePlinthes).

    public TypeSecurite SecuriteExterieure
    {
        get => General.SecuriteExterieure;
        set
        {
            General.SecuriteExterieure = value;
            General.PlintheExterieure = ReglePlinthes.Proposer(value) ?? General.PlintheExterieure;
            NotifierProtections();
        }
    }

    public TypeSecurite SecuriteInterieure
    {
        get => General.SecuriteInterieure;
        set
        {
            General.SecuriteInterieure = value;
            General.PlintheInterieure = ReglePlinthes.Proposer(value) ?? General.PlintheInterieure;
            NotifierProtections();
        }
    }

    public TypeSecurite SecuriteAbouts
    {
        get => General.SecuriteAbouts;
        set
        {
            General.SecuriteAbouts = value;
            General.PlintheAbouts = ReglePlinthes.Proposer(value) ?? General.PlintheAbouts;
            NotifierProtections();
        }
    }

    public TypePlinthe PlintheExterieure
    {
        get => General.PlintheExterieure;
        set { General.PlintheExterieure = value; NotifierProtections(); }
    }

    public TypePlinthe PlintheInterieure
    {
        get => General.PlintheInterieure;
        set { General.PlintheInterieure = value; NotifierProtections(); }
    }

    public TypePlinthe PlintheAbouts
    {
        get => General.PlintheAbouts;
        set { General.PlintheAbouts = value; NotifierProtections(); }
    }

    private void NotifierProtections()
    {
        foreach (var nom in new[] { nameof(SecuriteExterieure), nameof(SecuriteInterieure), nameof(SecuriteAbouts),
                                    nameof(PlintheExterieure), nameof(PlintheInterieure), nameof(PlintheAbouts) })
        {
            Notifier(nom);
        }

        Notifier(nameof(General));
        Notifier(nameof(ChoixSecurites));
        Notifier(nameof(ChoixPlinthes));
    }

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
            Notifier(nameof(EstVolumique));
            Notifier(nameof(InversionPossible));
            Notifier(nameof(LargeursProposees));
            ChargerFace(Math.Min(_indexFace, Faces.Count - 1));
            NotifierValidation();
        }
    }

    public bool EstVolumique => _projet.EstVolumique;

    public static IReadOnlyList<int> ChoixModules { get; } = Enumerable.Range(2, ProjetStructure.ModulesMaxi - 1).ToArray();

    /// <summary>Ajoute un module derrière le dernier (bouton « + Module »).</summary>
    public Commande AjouterModule { get; }

    /// <summary>Structure volumique : nombre de modules M1… (crée ou retire des onglets).</summary>
    public int NombreModules
    {
        get => _projet.NombreModules;
        set
        {
            if (_projet.NombreModules == value)
            {
                return;
            }

            SauverGrilles();
            _projet.NombreModules = value;
            _projet.AjusterFaces();
            Remplir(Faces, _projet.Faces);
            _structure = null;
            Notifier();
            ChargerFace(Math.Min(_indexFace, Faces.Count - 1));
        }
    }

    /// <summary>Structure en L, en U ou fermée inversée : bâtiment de l'autre côté (angle rentrant).</summary>
    public bool Inversee
    {
        get => _projet.Inversee;
        set
        {
            _projet.Inversee = value;
            _structure = null;
            Notifier();
        }
    }

    public bool InversionPossible => _projet.InversionPossible;

    /// <summary>Structure volumique : planchers en damier.</summary>
    public bool PlanchersDamier
    {
        get => _projet.PlanchersDamier;
        set
        {
            _projet.PlanchersDamier = value;
            _structure = null;
            Notifier();
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
            NotifierValidation();
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
                NotifierValidation();
            }
        }
    }

    /// <summary>Largeur de maille de la face active (change aussi le carré d'angle de la face suivante).</summary>
    public double LargeurMaille
    {
        get => Parametres.LargeurMaille;
        set { Parametres.LargeurMaille = value; Notifier(); NotifierValidation(); }
    }

    public double LongueurMaillePreferee
    {
        get => Parametres.LongueurMaillePreferee;
        set { Parametres.LongueurMaillePreferee = value; Notifier(); NotifierValidation(); }
    }

    public double HauteurDernierPlancher
    {
        get => Parametres.HauteurDernierPlancher;
        set { Parametres.HauteurDernierPlancher = value; Notifier(); Notifier(nameof(ChoixHauteursDepart)); NotifierValidation(); }
    }

    /// <summary>Case « Dessiner façade » : murs du bâtiment affichés dans le dessin.</summary>
    public bool DessinerFacade
    {
        get => _dessinerFacade;
        set { _dessinerFacade = value; Notifier(); }
    }

    /// <summary>Murs du bâtiment devant chaque face, à la distance à la façade (grilles actuelles).</summary>
    public IReadOnlyList<MurFacade> MursDuBatiment()
    {
        SauverGrilles();
        return MursFacade.Calculer(_projet);
    }

    // ---- « Valider la structure » : rouge si un paramètre a changé depuis la dernière validation ----

    /// <summary>La face active doit être revalidée (bouton rouge), sinon le bouton est vert.</summary>
    public bool FaceActiveAValider => _projet.FaceAValider(_indexFace);

    /// <summary>Première face à valider (-1 si aucune) : « Dessiner » ouvre son onglet.</summary>
    public int PremiereFaceAValider() => _projet.PremiereFaceAValider();

    private void NotifierValidation() => Notifier(nameof(FaceActiveAValider));

    /// <summary>
    /// Trappes et plateaux acier : avec des garde-corps MDS à l'extérieur, l'option « diagonales 1 maille sur 2 »
    /// est cochée et les cases Diago recochées (comme la v2, où les MDS passent en lisses).
    /// </summary>
    public bool TrappesAcier
    {
        get => Parametres.TrappesAcier;
        set
        {
            if (Parametres.TrappesAcier == value)
            {
                return;
            }

            Parametres.TrappesAcier = value;
            if (value && General.SecuriteExterieure == TypeSecurite.GardeCorpsMds2 && !Parametres.DiagonalesRenforcees)
            {
                Parametres.DiagonalesRenforcees = true;
                ProposerDiagonales();
                Notifier(nameof(Parametres));
            }

            Notifier();
        }
    }

    private CatalogueArticles _articles = CatalogueArticles.Vide;

    private bool _dessinerFacade;

    private ParametresParDefaut _defauts = ParametresParDefaut.Usine();

    public ObservableCollection<MailleDefinition> Mailles { get; } = new ObservableCollection<MailleDefinition>();

    public ObservableCollection<Niveau> Niveaux { get; } = new ObservableCollection<Niveau>();

    public ObservableCollection<LigneDecompte> Decompte { get; } = new ObservableCollection<LigneDecompte>();

    /// <summary>Articles de multidec.bib (désignations, poids, classement) utilisés par le décompte.</summary>
    public CatalogueArticles Articles
    {
        get => _articles;
        set
        {
            _articles = value ?? CatalogueArticles.Vide;
            if (_structure != null)
            {
                CalculerDecompte();
            }

            Notifier(nameof(ResumeDecompte));
        }
    }

    /// <summary>Bas de l'onglet « Décompte » : nombre de pièces, poids total, références sans poids.</summary>
    public string ResumeDecompte
    {
        get
        {
            if (_articles.Nombre == 0)
            {
                return "Désignations et poids indisponibles : fichier multidec.bib introuvable à côté de la bibliothèque de blocs.";
            }

            if (Decompte.Count == 0)
            {
                return $"multidec.bib : {_articles.Nombre} articles.";
            }

            int pieces = Decompte.Sum(l => l.Quantite);
            var sansPoids = CalculDecompte.SansPoids(Decompte).ToList();
            string texte = $"{Decompte.Count} références, {pieces} pièces, poids total {CalculDecompte.PoidsTotal(Decompte):N1} kg";
            return sansPoids.Count == 0 ? texte : texte + $" (poids inconnu pour : {string.Join(", ", sansPoids)})";
        }
    }

    public ObservableCollection<string> Alertes { get; } = new ObservableCollection<string>();

    /// <summary>Amarrages placés par la dernière génération (onglet « Amarrages »).</summary>
    public ObservableCollection<Amarrage> ListeAmarrages { get; } = new ObservableCollection<Amarrage>();

    /// <summary>Titre de l'onglet des grilles : « Mailles et niveaux · S2 ».</summary>
    public string TitreGrilles => "Mailles et niveaux · " + NomFaceActive;

    /// <summary>Couleur pastel de chaque face (S1, S2, S3, S4), sur son onglet et sur celui des grilles.</summary>
    public static IReadOnlyList<string> CouleursFaces { get; } = new[] { "#CFE2F3", "#D9EAD3", "#FCE5CD", "#E4D7F0" };

    public static string CouleurFace(int index) => CouleursFaces[((index % CouleursFaces.Count) + CouleursFaces.Count) % CouleursFaces.Count];

    /// <summary>Couleur de la face active (onglet « Mailles et niveaux »).</summary>
    public string CouleurFaceActive => CouleurFace(_indexFace);

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

    /// <summary>Colonne « Départ » (sol en pente) : de 0 au dernier plancher de la face moins 0,50 m, par pas de 0,50 m.</summary>
    public IReadOnlyList<double> ChoixHauteursDepart
    {
        get
        {
            var choix = new List<double>();
            for (double z = 0; z <= Parametres.HauteurDernierPlancher - 0.5 + Cotes.Tolerance; z = Cotes.Arrondi(z + 0.5))
            {
                choix.Add(z);
            }

            return choix;
        }
    }

    /// <summary>
    /// Colonne « Ht max » : hauteurs proposées pour une maille, à un multiple de 2 m du dernier plancher de la face
    /// (Laurent, 24/11/2026), de 2 m (ou du premier plancher) jusqu'à 10 m au-dessus. Clé = écart, valeur = hauteur affichée.
    /// </summary>
    public IReadOnlyList<KeyValuePair<double, string>> ChoixHauteursMaille
    {
        get
        {
            double h = Parametres.HauteurDernierPlancher;
            var choix = new List<KeyValuePair<double, string>>();
            for (double ecart = -2.0 * Math.Floor((h - 1.0 + Cotes.Tolerance) / 2.0); ecart <= 10.0 + Cotes.Tolerance; ecart += 2.0)
            {
                double ht = Cotes.Arrondi(h + ecart);
                if (ht > Cotes.Tolerance)
                {
                    choix.Add(new KeyValuePair<double, string>(Cotes.Arrondi(ecart), ht.ToString("0.00", CultureInfo.CurrentCulture) + (Cotes.Egal(ecart, 0) ? " (face)" : string.Empty)));
                }
            }

            return choix;
        }
    }

    /// <summary>Hauteurs proposées pour une poutre de franchissement : niveaux de 2 m et plus (0 = sans).</summary>
    public IReadOnlyList<double> HauteursPoutre =>
        new[] { 0.0 }.Concat(Niveaux.Where(n => n.Hauteur >= 2.0 - Cotes.Tolerance).Select(n => n.Hauteur).Distinct().OrderBy(h => h)).ToList();

    /// <summary>Niveaux qui ont une console : une colonne de cases « ConXm » chacun dans la grille des mailles.</summary>
    public IReadOnlyList<Niveau> NiveauxAvecConsole => Niveaux.Where(n => n.AConsole).OrderBy(n => n.Hauteur).ToList();

    /// <summary>Niveaux avec pare-gravois : une colonne « PareGravXm » chacun dans la grille des mailles (Laurent, 24/11/2026).</summary>
    public IReadOnlyList<Niveau> NiveauxAvecPareGravois => Niveaux.Where(n => n.PareGravois && n.Hauteur > Cotes.Tolerance).OrderBy(n => n.Hauteur).ToList();

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

        CalculerDecompte();
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

    /// <summary>Nouvelle maille : longueur préférée de la face, consoles et pare-gravois comme sa voisine, sans diagonale.</summary>
    private MailleDefinition NouvelleMaille(int voisine)
    {
        var m = new MailleDefinition(Parametres.LongueurMaillePreferee);
        if (voisine >= 0 && voisine < Mailles.Count)
        {
            m.Consoles = new List<double>(Mailles[voisine].Consoles);
            m.PareGravois = new List<double>(Mailles[voisine].PareGravois);
        }

        return m;
    }

    private void CalculerDecompte()
    {
        Remplir(Decompte, _structure == null ? new List<LigneDecompte>() : CalculDecompte.Calculer(_structure.Elements, _articles));
        Notifier(nameof(ResumeDecompte));
    }

    /// <summary>Retire de la structure les pièces supprimées dans le dessin et recalcule le décompte.</summary>
    public void RetirerElements(IReadOnlyCollection<ElementPlace> retires)
    {
        if (_structure == null || retires.Count == 0)
        {
            return;
        }

        _structure = _structure.SansElements(retires);
        CalculerDecompte();
        Notifier(nameof(Structure));
        Notifier(nameof(Resume));
    }

    /// <summary>Nouveau projet avec les paramètres par défaut de l'utilisateur.</summary>
    public void Nouveau()
    {
        ChargerProjet(_defauts.NouveauProjet());
        DessinerFacade = _defauts.DessinerFacade;
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
        _projet.MarquerFacesSansSignature();
        _structure = null;
        Remplir(Faces, projet.Faces);
        Decompte.Clear();
        Notifier(nameof(ResumeDecompte));
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
        Notifier(nameof(TrappesAcier));
        Notifier(nameof(Parametres));
        Notifier(nameof(LongueurFacade));
        Notifier(nameof(NombreAcces));
        Notifier(nameof(LargeurMaille));
        Notifier(nameof(LongueurMaillePreferee));
        Notifier(nameof(HauteurDernierPlancher));
        Notifier(nameof(ChoixHauteursDepart));
        NotifierListes();
        Notifier(nameof(LongueurObtenue));
        Notifier(nameof(Resume));
        Notifier(nameof(TitreGrilles));
        Notifier(nameof(CouleurFaceActive));
        NotifierValidation();
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
        NotifierColonnesNiveaux();
    }

    /// <summary>Colonne « Appro » de la grille des niveaux : seulement si la face a une maille d'appro (Laurent, 24/11/2026).</summary>
    public bool ColonneApproVisible => Mailles.Any(m => m.Type == TypeMaille.Appro);

    /// <summary>Colonne « Recette » de la grille des niveaux : seulement si la face a une maille de recette.</summary>
    public bool ColonneRecetteVisible => Mailles.Any(m => m.Type == TypeMaille.Recette);

    private void NotifierColonnesNiveaux()
    {
        Notifier(nameof(ColonneApproVisible));
        Notifier(nameof(ColonneRecetteVisible));
    }

    private void Maille_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MailleDefinition.Longueur))
        {
            NotifierLongueur();
        }
        else if (e.PropertyName == nameof(MailleDefinition.Type))
        {
            CocherDernierNiveau((MailleDefinition)sender!);
            NotifierColonnesNiveaux();
        }
    }

    /// <summary>
    /// Maille passée en recette (ou appro) : si aucun niveau n'est coché, la case du dernier niveau l'est
    /// (Laurent, 24/11/2026). Décocher ensuite reste possible : le dessin le signale par une alerte.
    /// </summary>
    private void CocherDernierNiveau(MailleDefinition m)
    {
        var dernier = Niveaux.Where(n => n.Type == TypePlancher.Complet && n.Hauteur > Cotes.Tolerance)
                             .OrderByDescending(n => n.Hauteur).FirstOrDefault();
        if (dernier == null)
        {
            return;
        }

        if (m.Type == TypeMaille.Recette && !Niveaux.Any(n => n.Recette))
        {
            dernier.Recette = true;
        }
        else if (m.Type == TypeMaille.Appro && !Niveaux.Any(n => n.Appro))
        {
            dernier.Appro = true;
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

            if (n.PareGravois && !Niveaux.Any(autre => autre.PareGravois && Cotes.Egal(autre.Hauteur, n.Hauteur)))
            {
                CocherPareGravois(n.Hauteur, false);
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
        else if (e.PropertyName == nameof(Niveau.PareGravois))
        {
            // Pare-gravois coché au niveau : colonne « PareGravXm » créée, toutes les mailles cochées (Laurent, 24/11/2026).
            CocherPareGravois(n.Hauteur, n.PareGravois);
            ColonnesConsolesChangees?.Invoke(this, EventArgs.Empty);
        }
        else if (e.PropertyName == nameof(Niveau.Hauteur))
        {
            double ancienne = _hauteurs[n];
            _hauteurs[n] = n.Hauteur;
            if (n.AConsole || n.PareGravois)
            {
                foreach (var m in Mailles)
                {
                    if (n.AConsole)
                    {
                        m.DeplacerConsole(ancienne, n.Hauteur);
                    }

                    if (n.PareGravois)
                    {
                        m.DeplacerPareGravois(ancienne, n.Hauteur);
                    }
                }

                ColonnesConsolesChangees?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private void CocherPareGravois(double hauteur, bool coche)
    {
        foreach (var m in Mailles)
        {
            m.DefinirPareGravois(hauteur, coche);
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
