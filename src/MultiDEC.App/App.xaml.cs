using System.Globalization;
using System.Windows;
using System.Windows.Markup;

namespace MultiDEC.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Saisie des cotes avec la virgule française dans tous les champs liés.
        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)));
        base.OnStartup(e);
    }
}
