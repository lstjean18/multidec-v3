using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using MultiDEC.Core.Geometrie;

namespace MultiDEC.Core.Facade;

/// <summary>
/// Une ligne de la grille des mailles, éditable par l'utilisateur (Laurent, 23/11/2026) :
/// - case « Diagonale » : la maille porte des diagonales verticales sur toute sa hauteur ;
/// - cases « ConXm » : la maille porte la console du niveau X (la console se choisit par niveau).
/// </summary>
public sealed class MailleDefinition : INotifyPropertyChanged
{
    private double _longueur;
    private TypeMaille _type = TypeMaille.Travail;
    private bool _diagonale;
    private List<double> _consoles = new List<double>();
    private string _jonction = string.Empty;
    private double _profondeurRecette = 2.0;
    private double _stabilisation;
    private double _pignon;
    private double _poutre;

    public MailleDefinition()
    {
    }

    public MailleDefinition(double longueur, TypeMaille type = TypeMaille.Travail, bool diagonale = false)
    {
        _longueur = longueur;
        _type = type;
        _diagonale = diagonale;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Longueur de la maille en mètres (0,70 / 1,00 / 1,50 / 2,00 / 2,50 / 3,00 ; 0,75 / 1,05 possibles).</summary>
    public double Longueur
    {
        get => _longueur;
        set => Changer(ref _longueur, value);
    }

    public TypeMaille Type
    {
        get => _type;
        set
        {
            if (Changer(ref _type, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LargeurRapportee)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EstTravail)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PignonEffectif)));
            }
        }
    }

    /// <summary>Maille de travail (avec ou sans sous-maille rapportée), par opposition à une maille d'accès.</summary>
    [JsonIgnore]
    public bool EstTravail => _type != TypeMaille.Acces;

    /// <summary>
    /// Profondeur de la sous-maille rapportée côté extérieur (v2 LargeurSousMaille) : 0,70 m pour un accès
    /// rapporté, 2,00 m pour un module d'appro, la profondeur choisie pour une recette, 0 sinon.
    /// </summary>
    [JsonIgnore]
    public double LargeurRapportee => _type switch
    {
        TypeMaille.AccesRapporte => 0.70,
        TypeMaille.Appro => 2.00,
        TypeMaille.Recette => _profondeurRecette,
        _ => 0,
    };

    /// <summary>Profondeur de la recette (colonne « Prof. recette », utilisée si la maille est une recette).</summary>
    public double ProfondeurRecette
    {
        get => _profondeurRecette;
        set => Changer(ref _profondeurRecette, value);
    }

    /// <summary>
    /// Largeur de la stabilisation côté extérieur (colonne « Stab. ») : file de poteaux à cette distance,
    /// reliée par lisses et diagonales jusqu'à 2 m sous le dernier plancher (v2). 0 = pas de stabilisation.
    /// </summary>
    public double Stabilisation
    {
        get => _stabilisation;
        set => Changer(ref _stabilisation, value);
    }

    /// <summary>
    /// Pignon (colonne « Pignon (+m) », mailles d'accès seulement, v2 CanPignon) : la maille monte de cette hauteur
    /// au-dessus du dernier plancher de la face, avec un plancher tous les 2 m. 0 = pas de pignon.
    /// </summary>
    public double Pignon
    {
        get => _pignon;
        set
        {
            if (Changer(ref _pignon, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PignonEffectif)));
            }
        }
    }

    /// <summary>Pignon pris en compte : seulement sur une maille d'accès, par pas de 2 m.</summary>
    [JsonIgnore]
    public double PignonEffectif => _type == TypeMaille.Acces && _pignon > Cotes.Tolerance ? 2.0 * Math.Floor((_pignon + Cotes.Tolerance) / 2.0) : 0;

    /// <summary>Dernier plancher de cette maille pour une face dont le dernier plancher est <paramref name="hauteurFace"/> (colonne « Ht max »).</summary>
    public double HauteurMax(double hauteurFace) => Cotes.Arrondi(hauteurFace + PignonEffectif);

    /// <summary>
    /// Poutre de franchissement (colonne « Poutre ») : hauteur du plancher porté par une poutre qui couvre cette maille
    /// et la suivante ; la file entre les deux est supprimée sous ce plancher (v2 clPoutre). 0 = pas de poutre.
    /// </summary>
    public double Poutre
    {
        get => _poutre;
        set => Changer(ref _poutre, value);
    }

    /// <summary>Diagonales verticales sur toute la hauteur de la maille.</summary>
    public bool Diagonale
    {
        get => _diagonale;
        set => Changer(ref _diagonale, value);
    }

    /// <summary>
    /// Face à laquelle cette maille se raccorde (carré d'angle d'une structure en L, en U ou fermée), vide sinon.
    /// Calculé d'après la forme, en lecture seule dans la grille.
    /// </summary>
    [JsonIgnore]
    public string Jonction
    {
        get => _jonction;
        set => Changer(ref _jonction, value ?? string.Empty);
    }

    /// <summary>Hauteurs des niveaux dont la console est posée sur cette maille.</summary>
    public List<double> Consoles
    {
        get => _consoles;
        set
        {
            _consoles = value ?? new List<double>();
            NotifierConsoles();
        }
    }

    /// <summary>Ancien format (v1, console par maille) : lu pour la conversion, jamais écrit.</summary>
    [JsonPropertyName("console")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ConsoleAncienFormat { get; set; }

    /// <summary>Accès des cases « ConXm » de la grille : clé = hauteur du niveau, en invariant (ex. "4" ou "4.5").</summary>
    [JsonIgnore]
    public bool this[string hauteur]
    {
        get => ConsoleA(Lire(hauteur));
        set => DefinirConsole(Lire(hauteur), value);
    }

    public bool ConsoleA(double hauteur) => _consoles.Any(h => Cotes.Egal(h, hauteur));

    public void DefinirConsole(double hauteur, bool presente)
    {
        if (presente == ConsoleA(hauteur))
        {
            return;
        }

        if (presente)
        {
            _consoles.Add(Cotes.Arrondi(hauteur));
            _consoles.Sort();
        }
        else
        {
            _consoles.RemoveAll(h => Cotes.Egal(h, hauteur));
        }

        NotifierConsoles();
    }

    /// <summary>Le niveau a changé de hauteur : la case suit.</summary>
    public void DeplacerConsole(double ancienne, double nouvelle)
    {
        if (ConsoleA(ancienne))
        {
            DefinirConsole(ancienne, false);
            DefinirConsole(nouvelle, true);
        }
    }

    public static string Cle(double hauteur) => Cotes.Arrondi(hauteur).ToString("0.###", CultureInfo.InvariantCulture);

    public MailleDefinition Copie() => new MailleDefinition(Longueur, Type, Diagonale)
    {
        Consoles = new List<double>(_consoles),
        ProfondeurRecette = ProfondeurRecette,
        Stabilisation = Stabilisation,
        Pignon = Pignon,
        Poutre = Poutre,
    };

    public override string ToString() =>
        FormattableString.Invariant($"{Longueur:0.00} m {Type}{(Diagonale ? " + diagonale" : string.Empty)}{(LargeurRapportee > 0 ? $" ({LargeurRapportee:0.00})" : string.Empty)}{(Stabilisation > 0 ? $" + stab. {Stabilisation:0.00}" : string.Empty)}{(PignonEffectif > 0 ? $" + pignon {PignonEffectif:0}" : string.Empty)}{(Poutre > 0 ? $" + poutre à {Poutre:0.00}" : string.Empty)}{(_consoles.Count > 0 ? " + consoles " + string.Join("/", _consoles) : string.Empty)}");

    private static double Lire(string hauteur) => double.Parse(hauteur, CultureInfo.InvariantCulture);

    private void NotifierConsoles()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Consoles)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    private bool Changer<T>(ref T champ, T valeur, [CallerMemberName] string? nom = null)
    {
        if (EqualityComparer<T>.Default.Equals(champ, valeur))
        {
            return false;
        }

        champ = valeur;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nom));
        return true;
    }
}
