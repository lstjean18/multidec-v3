using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using MultiDEC.Core.Geometrie;

namespace MultiDEC.Core.Facade;

/// <summary>
/// Un niveau de plancher (hauteur en mètres au-dessus du départ), son type et sa console éventuelle.
/// La console se choisit par niveau ; chaque maille dit ensuite si elle la porte (colonnes « ConXm »
/// de la grille des mailles, Laurent 23/11/2026).
/// </summary>
public sealed class Niveau : INotifyPropertyChanged
{
    private double _hauteur;
    private TypePlancher _type = TypePlancher.Complet;
    private string? _console;

    public Niveau()
    {
    }

    public Niveau(double hauteur, TypePlancher type, string? console = null)
    {
        _hauteur = hauteur;
        _type = type;
        _console = console;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public double Hauteur
    {
        get => _hauteur;
        set => Changer(ref _hauteur, value, nameof(NomColonneConsole));
    }

    public TypePlancher Type
    {
        get => _type;
        set => Changer(ref _type, value);
    }

    /// <summary>Référence de la console de ce niveau (ex. HMCON700E), ou null si aucune.</summary>
    public string? Console
    {
        get => _console;
        set => Changer(ref _console, string.IsNullOrWhiteSpace(value) ? null : value, nameof(AConsole));
    }

    [JsonIgnore]
    public bool AConsole => _console != null;

    [JsonIgnore]
    public bool EstAccesSeul => Type != TypePlancher.Complet;

    /// <summary>Titre de la colonne de la grille des mailles : « Con » + hauteur (ex. « Con4m », « Con4,5m »).</summary>
    [JsonIgnore]
    public string NomColonneConsole => NomColonne(Hauteur);

    public static string NomColonne(double hauteur) =>
        "Con" + Cotes.Arrondi(hauteur).ToString("0.##", CultureInfo.InvariantCulture).Replace('.', ',') + "m";

    public override string ToString() =>
        FormattableString.Invariant($"{Hauteur:0.00} m {Type}{(AConsole ? " + " + Console : string.Empty)}");

    private void Changer<T>(ref T champ, T valeur, string? autre = null, [CallerMemberName] string? nom = null)
    {
        if (Equals(champ, valeur))
        {
            return;
        }

        champ = valeur;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nom));
        if (autre != null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(autre));
        }
    }
}
