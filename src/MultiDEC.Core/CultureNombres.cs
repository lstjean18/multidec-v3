using System.Globalization;
using System.Text.RegularExpressions;

namespace MultiDEC.Core;

/// <summary>
/// Séparateur décimal du poste (remarque 64, Laurent 28/11/2026) : « . » ou « , » selon le réglage de Windows, à
/// l'écran comme dans les documents (plans, cartouche, liste du matériel) ; la saisie accepte les deux.
/// </summary>
public static class CultureNombres
{
    private static readonly CultureInfo Francais = CultureInfo.GetCultureInfo("fr-FR");
    private static readonly Regex VirguleDecimale = new Regex(@"(?<=\d),(?=\d)", RegexOptions.Compiled);
    private static readonly Regex PointDecimal = new Regex(@"(?<=(?:^|[^\d.])\d+)\.(?=\d+(?:[^\d.]|$))", RegexOptions.Compiled);
    private static CultureInfo? _documents;
    private static string? _separateurDocuments;

    /// <summary>Séparateur décimal du poste (culture courante, réglages personnalisés de Windows compris).</summary>
    public static string Separateur
    {
        get
        {
            string s = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
            return s == "." ? "." : ","; // tout autre réglage exotique : virgule
        }
    }

    /// <summary>
    /// Culture des textes de MultiDEC : français (dates, libellés) avec le séparateur décimal du poste et une espace
    /// ordinaire pour les milliers (lisible par les polices des PDF).
    /// </summary>
    public static CultureInfo Documents
    {
        get
        {
            string separateur = Separateur;
            var c = _documents;
            if (c == null || _separateurDocuments != separateur)
            {
                var copie = (CultureInfo)Francais.Clone();
                copie.NumberFormat.NumberDecimalSeparator = separateur;
                copie.NumberFormat.CurrencyDecimalSeparator = separateur;
                copie.NumberFormat.PercentDecimalSeparator = separateur;
                copie.NumberFormat.NumberGroupSeparator = " ";
                copie.NumberFormat.CurrencyGroupSeparator = " ";
                copie.NumberFormat.PercentGroupSeparator = " ";
                c = CultureInfo.ReadOnly(copie);
                _documents = c;
                _separateurDocuments = separateur;
            }

            return c;
        }
    }

    /// <summary>
    /// Texte écrit avec des cotes « 1,50 » ou « 1.50 » (messages, libellés, alertes) remis au séparateur du poste.
    /// Seuls les séparateurs entre deux chiffres sont touchés (« 3, 4 » ou un numéro de version « 3.0.1 » restent tels quels).
    /// </summary>
    public static string Localiser(string? texte)
    {
        if (string.IsNullOrEmpty(texte))
        {
            return texte ?? string.Empty;
        }

        return Separateur == "."
            ? VirguleDecimale.Replace(texte, ".")
            : PointDecimal.Replace(texte, ",");
    }

    /// <summary>Nombre saisi avec « . » ou « , » comme séparateur décimal (espaces de milliers tolérées).</summary>
    public static bool Lire(string? texte, out double valeur)
    {
        valeur = 0;
        if (string.IsNullOrWhiteSpace(texte))
        {
            return false;
        }

        string t = texte!.Trim().Replace(" ", string.Empty).Replace(" ", string.Empty).Replace(" ", string.Empty).Replace(',', '.');
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out valeur);
    }

    /// <summary>Remplace « . » ou « , » par le séparateur du poste (frappe dans un champ numérique).</summary>
    public static string Normaliser(string texte) => texte.Replace(".", Separateur).Replace(",", Separateur);
}
