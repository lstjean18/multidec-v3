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
    private bool _diagoH;
    private bool _pareGravois;
    private bool _appro = true;
    private bool _recette;

    public Niveau()
    {
    }

    public Niveau(double hauteur, TypePlancher type, string? console = null, bool diagoH = false)
    {
        _hauteur = hauteur;
        _console = console;
        _diagoH = diagoH;
        Type = type;
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
        set
        {
            // Ancien type « accès seul + diagonale H » : remplacé par la case Diago H (maquette du 24/11/2026).
            if (value == TypePlancher.AccesSeulDiagoH)
            {
                value = TypePlancher.AccesSeul;
                DiagoH = true;
            }

            Changer(ref _type, value);
        }
    }

    /// <summary>Pare-gravois à ce niveau, sur toute la longueur de la face, côté extérieur (v2).</summary>
    public bool PareGravois
    {
        get => _pareGravois;
        set => Changer(ref _pareGravois, value);
    }

    /// <summary>Niveau desservi par les modules d'approvisionnement (v2 : coché par défaut).</summary>
    public bool Appro
    {
        get => _appro;
        set => Changer(ref _appro, value);
    }

    /// <summary>Niveau desservi par les recettes (plancher et garde-corps dans la recette à ce niveau).</summary>
    public bool Recette
    {
        get => _recette;
        set => Changer(ref _recette, value);
    }

    /// <summary>
    /// Diagonales horizontales à ce niveau, dans toutes les mailles de travail (comme la v2),
    /// sens alterné d'une maille à l'autre.
    /// </summary>
    public bool DiagoH
    {
        get => _diagoH;
        set => Changer(ref _diagoH, value);
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
        FormattableString.Invariant($"{Hauteur:0.00} m {Type}{(DiagoH ? " + diago H" : string.Empty)}{(PareGravois ? " + pare-gravois" : string.Empty)}{(AConsole ? " + " + Console : string.Empty)}");

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
