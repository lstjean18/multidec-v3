using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MultiDEC.Core.Droits;

/// <summary>Licence d'un poste (MultiDEC Admin) : code du poste, demandeur, échéance.</summary>
public sealed class Licence
{
    public string Code { get; set; } = string.Empty;

    public string Demandeur { get; set; } = string.Empty;

    public string Entreprise { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    /// <summary>Dernier jour d'utilisation (inclus), ou null = sans limite.</summary>
    public DateTime? Expiration { get; set; }

    /// <summary>Licence suspendue : conservée dans le registre mais absente de la liste publiée.</summary>
    public bool Suspendue { get; set; }

    public string Notes { get; set; } = string.Empty;

    public DateTime Creation { get; set; }

    public DateTime Modification { get; set; }

    public Licence Copie() => (Licence)MemberwiseClone();

    /// <summary>Licence publiée : active et non expirée à la date donnée.</summary>
    public bool EstValideLe(DateTime jour) => !Suspendue && (!Expiration.HasValue || jour.Date <= Expiration.Value.Date);
}

/// <summary>
/// Registre des licences tenu par RETOTUB dans MultiDEC Admin (licences.json, dans le dossier de données partagé).
/// Contient les coordonnées des demandeurs ; seule la liste des codes et échéances est publiée (signée).
/// </summary>
public sealed class RegistreLicences
{
    public const string NomFichier = "licences.json";

    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public int Format { get; set; } = 1;

    public List<Licence> Licences { get; set; } = new List<Licence>();

    /// <summary>Date de la dernière publication de la liste (UTC).</summary>
    public DateTime? DernierePublication { get; set; }

    /// <summary>Dernier ajout, modification ou suppression de licence (UTC).</summary>
    public DateTime? DerniereModification { get; set; }

    /// <summary>Vrai si des licences ont changé depuis la dernière publication de la liste.</summary>
    public bool APublier => DerniereModification.HasValue && (!DernierePublication.HasValue || DerniereModification.Value > DernierePublication.Value);

    public static RegistreLicences Lire(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<RegistreLicences>(json.TrimStart('﻿'), Options) ?? new RegistreLicences();
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Registre des licences illisible : " + ex.Message, ex);
        }
    }

    public string Ecrire() => JsonSerializer.Serialize(this, Options);

    public Licence? Trouver(string code)
    {
        string? n = CodePoste.Normaliser(code);
        return n == null ? null : Licences.FirstOrDefault(l => l.Code == n);
    }

    /// <summary>
    /// Ajoute ou met à jour une licence (même code). Le code est contrôlé (clé de contrôle) et normalisé.
    /// Lève <see cref="ArgumentException"/> si le code est invalide.
    /// </summary>
    public Licence Enregistrer(Licence licence, DateTime maintenant)
    {
        string code = CodePoste.Normaliser(licence.Code)
                      ?? throw new ArgumentException($"Code de poste invalide : « {licence.Code} » (16 caractères, faute de frappe ?).", nameof(licence));
        var existante = Trouver(code);
        var copie = licence.Copie();
        copie.Code = code;
        copie.Demandeur = copie.Demandeur.Trim();
        copie.Entreprise = copie.Entreprise.Trim();
        copie.Email = copie.Email.Trim();
        copie.Creation = existante?.Creation ?? maintenant;
        copie.Modification = maintenant;
        if (existante != null)
        {
            Licences[Licences.IndexOf(existante)] = copie;
        }
        else
        {
            Licences.Add(copie);
        }

        DerniereModification = maintenant;
        return copie;
    }

    public bool Supprimer(string code, DateTime maintenant)
    {
        var l = Trouver(code);
        if (l == null || !Licences.Remove(l))
        {
            return false;
        }

        DerniereModification = maintenant;
        return true;
    }

    /// <summary>
    /// Contenu de la liste à signer : codes des licences valides (non suspendues, non expirées) et leurs échéances,
    /// sans aucune coordonnée.
    /// </summary>
    public string ContenuPublic(DateTime emise)
    {
        var postes = Licences
            .Where(l => l.EstValideLe(emise.ToLocalTime()) && CodePoste.EstValide(l.Code))
            .OrderBy(l => l.Code, StringComparer.Ordinal)
            .Select(l => new Dictionary<string, object?>
            {
                ["code"] = l.Code,
                ["expiration"] = l.Expiration?.ToString("yyyy-MM-dd"),
            })
            .ToList();
        var contenu = new Dictionary<string, object?>
        {
            ["format"] = ListePostes.FormatActuel,
            ["emise"] = DateTime.SpecifyKind(emise.ToUniversalTime(), DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["postes"] = postes,
        };
        return JsonSerializer.Serialize(contenu);
    }

    /// <summary>
    /// Code de poste trouvé dans un texte collé (e-mail de demande) : premier groupe « XXXX-XXXX-XXXX-XXXX » valide, ou null.
    /// </summary>
    public static string? ExtraireCode(string? texte)
    {
        if (string.IsNullOrWhiteSpace(texte))
        {
            return null;
        }

        string? direct = CodePoste.Normaliser(texte);
        if (direct != null)
        {
            return direct;
        }

        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(texte!.ToUpperInvariant(), @"[A-Z2-9]{4}-[A-Z2-9]{4}-[A-Z2-9]{4}-[A-Z2-9]{4}"))
        {
            string? n = CodePoste.Normaliser(m.Value);
            if (n != null)
            {
                return n;
            }
        }

        return null;
    }
}
