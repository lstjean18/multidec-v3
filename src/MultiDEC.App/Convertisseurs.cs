using System;
using System.Globalization;
using System.Windows.Data;
using MultiDEC.App.ViewModels;

namespace MultiDEC.App;

/// <summary>Affiche le libellé français d'une valeur d'énumération (« MDS 2 », « Accès seul »…).</summary>
public sealed class ConvertisseurLibelle : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => Libelles.De(value);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Case ou bouton radio coché quand la valeur liée vaut le paramètre (nom de la valeur d'énumération).</summary>
public sealed class ConvertisseurEgal : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value != null && string.Equals(value.ToString(), parameter as string, StringComparison.Ordinal);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true && parameter is string nom ? Enum.Parse(targetType, nom) : Binding.DoNothing;
}
