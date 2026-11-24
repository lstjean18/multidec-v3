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

/// <summary>Cote optionnelle en mètres : « – » pour 0 (stabilisation absente, etc.), sinon 0,00.</summary>
public sealed class ConvertisseurCoteOptionnelle : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double d && Math.Abs(d) > 1e-9 ? d.ToString("0.00", CultureInfo.CurrentCulture) : "–";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Colonne « Ht max » : dernier plancher de la face + écart de la maille.</summary>
public sealed class ConvertisseurHauteurMax : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length == 2 && values[0] is double ecart && values[1] is double h
            ? (h + ecart).ToString("0.00", CultureInfo.CurrentCulture)
            : string.Empty;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
