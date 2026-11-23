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
        set => Changer(ref _type, value);
    }

    /// <summary>Diagonales verticales sur toute la hauteur de la maille.</summary>
    public bool Diagonale
    {
        get => _diagonale;
        set => Changer(ref _diagonale, value);
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

    public MailleDefinition Copie() => new MailleDefinition(Longueur, Type, Diagonale) { Consoles = new List<double>(_consoles) };

    public override string ToString() =>
        FormattableString.Invariant($"{Longueur:0.00} m {Type}{(Diagonale ? " + diagonale" : string.Empty)}{(_consoles.Count > 0 ? " + consoles " + string.Join("/", _consoles) : string.Empty)}");

    private static double Lire(string hauteur) => double.Parse(hauteur, CultureInfo.InvariantCulture);

    private void NotifierConsoles()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Consoles)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    private void Changer<T>(ref T champ, T valeur, [CallerMemberName] string? nom = null)
    {
        if (!EqualityComparer<T>.Default.Equals(champ, valeur))
        {
            champ = valeur;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nom));
        }
    }
}
