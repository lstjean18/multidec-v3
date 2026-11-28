using System.Linq;
using System.Reflection;

namespace MultiDEC.App;

/// <summary>
/// Version affichée (écran de démarrage, titre) : libellé de version du csproj (« v3.0 bêta 1 ») et numéro de build,
/// celui du run de compilation du CI (github.run_number), « local » pour une compilation sur un poste.
/// </summary>
public static class VersionApplication
{
    public static string Libelle => Metadonnee("Libelle") ?? "v3";

    public static string Build => Metadonnee("Build") ?? "local";

    /// <summary>Texte complet, ex. « Version 3.0 bêta 1 – build 128 ».</summary>
    public static string Texte => $"Version {Libelle.TrimStart('v', 'V')} – build {Build}";

    private static string? Metadonnee(string cle) =>
        typeof(VersionApplication).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == cle)?.Value is { Length: > 0 } v ? v : null;
}
