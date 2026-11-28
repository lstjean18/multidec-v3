using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace MultiDEC.Core.Droits;

/// <summary>Poste autorisé : code et date d'expiration (incluse ; null = sans limite).</summary>
public sealed class PosteAutorise
{
    public string Code { get; set; } = string.Empty;

    /// <summary>Dernier jour d'utilisation autorisé (format aaaa-mm-jj dans la liste), ou null.</summary>
    public DateTime? Expiration { get; set; }
}

/// <summary>
/// Liste des postes autorisés publiée sur retotub.com (postes-autorises.json), signée par RETOTUB (RSA, SHA-256).
/// L'enveloppe publiée contient le contenu en base 64 et sa signature : on vérifie exactement les octets signés,
/// sans dépendre de la mise en forme du JSON. Seuls les codes et les dates d'expiration sont publiés : les noms des
/// clients restent dans le fichier de gestion de RETOTUB.
/// </summary>
public sealed class ListePostes
{
    public const string NomFichier = "postes-autorises.json";
    public const int FormatActuel = 1;

    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Date (UTC) à laquelle RETOTUB a signé la liste : une liste plus ancienne que la dernière acceptée est refusée.</summary>
    public DateTime Emise { get; private set; }

    public IReadOnlyList<PosteAutorise> Postes { get; private set; } = Array.Empty<PosteAutorise>();

    /// <summary>Poste de la liste correspondant au code (comparaison sur le code normalisé), ou null.</summary>
    public PosteAutorise? Trouver(string code)
    {
        string? cherche = CodePoste.Normaliser(code);
        return cherche == null ? null : Postes.FirstOrDefault(p => CodePoste.Normaliser(p.Code) == cherche);
    }

    /// <summary>
    /// Lit l'enveloppe signée et vérifie sa signature avec la clé publique de RETOTUB (XML « RSAKeyValue »).
    /// Lève <see cref="InvalidDataException"/> si l'enveloppe est illisible ou <see cref="CryptographicException"/>
    /// si la signature est fausse (liste modifiée, ou signée avec une autre clé).
    /// </summary>
    public static ListePostes LireSignee(string enveloppe, string clePubliqueXml)
    {
        Enveloppe? e;
        try
        {
            e = JsonSerializer.Deserialize<Enveloppe>(enveloppe.TrimStart('﻿'), Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Liste des postes illisible.", ex);
        }

        if (e == null || string.IsNullOrEmpty(e.Contenu) || string.IsNullOrEmpty(e.Signature))
        {
            throw new InvalidDataException("Liste des postes incomplète.");
        }

        byte[] contenu, signature;
        try
        {
            contenu = Convert.FromBase64String(e.Contenu);
            signature = Convert.FromBase64String(e.Signature);
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException("Liste des postes illisible (base 64).", ex);
        }

        using (var rsa = CreerCle(clePubliqueXml))
        {
            if (!rsa.VerifyData(contenu, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            {
                throw new CryptographicException("Signature de la liste des postes invalide.");
            }
        }

        return LireContenu(Encoding.UTF8.GetString(contenu));
    }

    /// <summary>Contenu (JSON non signé) : à n'utiliser qu'après vérification de la signature.</summary>
    internal static ListePostes LireContenu(string json)
    {
        Contenu? c;
        try
        {
            c = JsonSerializer.Deserialize<Contenu>(json.TrimStart('﻿'), Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Contenu de la liste des postes illisible.", ex);
        }

        if (c == null || c.Format < 1 || c.Format > FormatActuel || c.Emise == null)
        {
            throw new InvalidDataException("Contenu de la liste des postes non reconnu (format ou date d'émission).");
        }

        return new ListePostes
        {
            Emise = DateTime.SpecifyKind(c.Emise.Value.ToUniversalTime(), DateTimeKind.Utc),
            Postes = (c.Postes ?? new List<PosteAutorise>()).Where(p => !string.IsNullOrWhiteSpace(p.Code)).ToList(),
        };
    }

    /// <summary>Signe un contenu (outil de RETOTUB et tests) : enveloppe JSON prête à publier.</summary>
    public static string Signer(string contenuJson, RSA clePrivee)
    {
        byte[] contenu = Encoding.UTF8.GetBytes(contenuJson);
        byte[] signature = clePrivee.SignData(contenu, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return JsonSerializer.Serialize(new Enveloppe { Format = FormatActuel, Contenu = Convert.ToBase64String(contenu), Signature = Convert.ToBase64String(signature) }, Options);
    }

    /// <summary>Clé publique RSA lue depuis le XML « &lt;RSAKeyValue&gt;&lt;Modulus&gt;…&lt;/Modulus&gt;&lt;Exponent&gt;…&lt;/Exponent&gt;&lt;/RSAKeyValue&gt; ».</summary>
    public static RSA CreerCle(string clePubliqueXml)
    {
        XElement racine;
        try
        {
            racine = XElement.Parse(clePubliqueXml.Trim().TrimStart('﻿'));
        }
        catch (System.Xml.XmlException ex)
        {
            throw new InvalidDataException("Clé publique illisible.", ex);
        }

        string? modulus = racine.Element("Modulus")?.Value, exposant = racine.Element("Exponent")?.Value;
        if (racine.Name.LocalName != "RSAKeyValue" || string.IsNullOrWhiteSpace(modulus) || string.IsNullOrWhiteSpace(exposant))
        {
            throw new InvalidDataException("Clé publique illisible (RSAKeyValue, Modulus, Exponent attendus).");
        }

        var rsa = RSA.Create();
        rsa.ImportParameters(new RSAParameters { Modulus = Convert.FromBase64String(modulus!.Trim()), Exponent = Convert.FromBase64String(exposant!.Trim()) });
        return rsa;
    }

    private sealed class Enveloppe
    {
        public int Format { get; set; }
        public string? Contenu { get; set; }
        public string? Signature { get; set; }
    }

    private sealed class Contenu
    {
        public int Format { get; set; }
        public DateTime? Emise { get; set; }
        public List<PosteAutorise>? Postes { get; set; }
    }
}
