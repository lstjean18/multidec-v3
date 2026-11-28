using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace MultiDEC.Core.Decompte;

/// <summary>
/// Correspondance des références RETOTUB en références d'un client (travail « en références client », Laurent 25/11/2026).
/// LV Tec est intégré au programme ; un autre client peut être chargé depuis un fichier CSV (RefRETOTUB;RefClient, comme en v2).
/// </summary>
public sealed class ReferencesClient
{
    /// <summary>Valeur du paramètre « Références affichées » : références RETOTUB.</summary>
    public const string Retotub = "RETOTUB";

    /// <summary>Valeur du paramètre « Références affichées » : références LV Tec intégrées.</summary>
    public const string CodeLvTec = "LVTEC";

    /// <summary>Valeur du paramètre « Références affichées » : fichier de correspondance des Paramètres par défaut.</summary>
    public const string CodeFichier = "FICHIER";

    private static readonly Lazy<ReferencesClient> _lvTec = new Lazy<ReferencesClient>(() =>
    {
        using var flux = typeof(ReferencesClient).Assembly.GetManifestResourceStream("MultiDEC.Core.ReferencesClient.LvTec.csv")
            ?? throw new InvalidOperationException("Table LV Tec absente de l'assemblage.");
        using var lecteur = new StreamReader(flux, Encoding.UTF8);
        return Lire(lecteur, "LV Tec");
    });

    private readonly Dictionary<string, string> _table;
    private readonly Dictionary<string, string> _inverse;

    public ReferencesClient(string nom, IEnumerable<KeyValuePair<string, string>> correspondances)
    {
        Nom = nom;
        _table = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in correspondances)
        {
            if (!_table.ContainsKey(kv.Key))
            {
                _table[kv.Key] = kv.Value;
            }
        }

        _inverse = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in _table)
        {
            if (!_inverse.ContainsKey(kv.Value))
            {
                _inverse[kv.Value] = kv.Key; // une référence client pour plusieurs RETOTUB : la première l'emporte
            }
        }
    }

    /// <summary>Nom du client (affiché en tête de colonne).</summary>
    public string Nom { get; }

    public int Nombre => _table.Count;

    /// <summary>Références LV Tec intégrées.</summary>
    public static ReferencesClient LvTec => _lvTec.Value;

    public bool TryGet(string referenceRetotub, out string referenceClient) => _table.TryGetValue(referenceRetotub, out referenceClient!);

    /// <summary>Référence RETOTUB d'une référence client (saisie d'une référence dans le décompte, remarque 61).</summary>
    public bool TryGetRetotub(string referenceClient, out string referenceRetotub) => _inverse.TryGetValue(referenceClient, out referenceRetotub!);

    /// <summary>
    /// CSV « RefRETOTUB;RefClient » (séparateur point-virgule, tabulation ou virgule) ; ligne d'en-tête facultative ;
    /// les lignes sans référence client sont ignorées, la première correspondance d'une référence l'emporte.
    /// </summary>
    public static ReferencesClient Lire(TextReader lecteur, string nom)
    {
        var lignes = new List<KeyValuePair<string, string>>();
        string? ligne;
        while ((ligne = lecteur.ReadLine()) != null)
        {
            var champs = ligne.Split(';', '\t', ',').Select(c => c.Trim().Trim('"')).ToArray();
            if (champs.Length < 2 || champs[0].Length == 0 || champs[1].Length == 0 || champs[1] == "-")
            {
                continue;
            }

            if (champs[0].Equals("RefRETOTUB", StringComparison.OrdinalIgnoreCase) || champs[0].StartsWith("Réf", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            lignes.Add(new KeyValuePair<string, string>(champs[0], champs[1]));
        }

        return new ReferencesClient(nom, lignes);
    }

    public static ReferencesClient Charger(string chemin)
    {
        using var lecteur = new StreamReader(chemin, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return Lire(lecteur, Path.GetFileNameWithoutExtension(chemin));
    }

    /// <summary>
    /// Table correspondant au paramètre « Références affichées » (null = références RETOTUB). Fichier illisible ou absent :
    /// null, et <paramref name="erreur"/> explique pourquoi.
    /// </summary>
    public static ReferencesClient? Selon(string? code, string? fichier, out string? erreur)
    {
        erreur = null;
        switch (code)
        {
            case CodeLvTec:
                return LvTec;
            case CodeFichier:
                if (string.IsNullOrWhiteSpace(fichier) || !File.Exists(fichier))
                {
                    erreur = "Fichier de références client introuvable (Paramètres par défaut) : références RETOTUB affichées.";
                    return null;
                }

                try
                {
                    return Charger(fichier!);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    erreur = "Fichier de références client illisible : " + ex.Message;
                    return null;
                }

            default:
                return null;
        }
    }
}
