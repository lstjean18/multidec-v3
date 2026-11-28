using System.Globalization;
using System.Runtime.CompilerServices;

namespace MultiDEC.Core.Tests;

/// <summary>
/// Les tests attendent les textes d'un poste français (virgule décimale) : culture fixée pour toute l'exécution,
/// quel que soit le réglage de la machine (runner Linux en culture invariante). Remarque 64.
/// </summary>
internal static class CultureDesTests
{
    [ModuleInitializer]
    internal static void Fixer()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
    }
}
