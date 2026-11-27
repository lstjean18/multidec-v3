using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace MultiDEC.Core.Droits;

/// <summary>
/// Code unique du poste (droits d'utilisation, Laurent 27/11/2026) : 16 caractères « XXXX-XXXX-XXXX-XXXX » calculés à
/// partir d'identifiants stables de la machine (identifiant Windows, UUID de la carte mère). Le dernier caractère est une
/// clé de contrôle qui détecte une faute de frappe à la recopie. Alphabet sans I, O, 0 ni 1 (lecture sans ambiguïté).
/// </summary>
public static class CodePoste
{
    public const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int NombreCaracteres = 15;

    /// <summary>Code calculé à partir des identifiants de la machine (ordre significatif, casse et espaces ignorés).</summary>
    public static string Calculer(IEnumerable<string?> identifiants)
    {
        var elements = identifiants.Select(i => (i ?? string.Empty).Trim().ToUpperInvariant()).ToList();
        if (elements.All(e => e.Length == 0))
        {
            throw new ArgumentException("Aucun identifiant de poste disponible.", nameof(identifiants));
        }

        byte[] empreinte;
        using (var sha = SHA256.Create())
        {
            empreinte = sha.ComputeHash(Encoding.UTF8.GetBytes("MultiDEC-v3|" + string.Join("|", elements)));
        }

        var valeurs = new int[NombreCaracteres];
        for (int i = 0; i < NombreCaracteres; i++)
        {
            // 5 bits par caractère, lus dans l'empreinte.
            int bit = i * 5;
            int mot = (empreinte[bit / 8] << 8) | empreinte[bit / 8 + 1];
            valeurs[i] = (mot >> (11 - bit % 8)) & 31;
        }

        var texte = new string(valeurs.Select(v => Alphabet[v]).ToArray()) + Alphabet[Controle(valeurs)];
        return Formater(texte);
    }

    /// <summary>Code mis en forme (majuscules, sans espaces, tirets tous les 4 caractères), ou null s'il est mal formé ou si sa clé de contrôle est fausse.</summary>
    public static string? Normaliser(string? code)
    {
        if (code == null)
        {
            return null;
        }

        var texte = new string(code.ToUpperInvariant().Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());
        if (texte.Length != NombreCaracteres + 1 || texte.Any(c => Alphabet.IndexOf(c) < 0))
        {
            return null;
        }

        var valeurs = texte.Take(NombreCaracteres).Select(c => Alphabet.IndexOf(c)).ToArray();
        return Alphabet[Controle(valeurs)] == texte[NombreCaracteres] ? Formater(texte) : null;
    }

    public static bool EstValide(string? code) => Normaliser(code) != null;

    // Poids impairs (inversibles modulo 32) : toute erreur sur un seul caractère change la clé.
    private static int Controle(IReadOnlyList<int> valeurs)
    {
        int somme = 0;
        for (int i = 0; i < valeurs.Count; i++)
        {
            somme += valeurs[i] * (2 * i + 1);
        }

        return somme % 32;
    }

    private static string Formater(string texte) =>
        string.Join("-", Enumerable.Range(0, texte.Length / 4).Select(i => texte.Substring(i * 4, 4)));
}
