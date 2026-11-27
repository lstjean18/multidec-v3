using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace MultiDEC.Core.Droits;

/// <summary>
/// Mémoire locale du contrôle des droits (chiffrée par l'application avec DPAPI, donc liée à l'utilisateur et au poste) :
/// dernière liste signée acceptée, date de la dernière vérification en ligne réussie, date la plus récente vue sur ce
/// poste (détection d'une horloge reculée).
/// </summary>
public sealed class EtatDroits
{
    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };

    /// <summary>Dernière liste signée reçue du serveur (enveloppe telle que publiée, revérifiée à chaque lecture).</summary>
    public string? ListeSignee { get; set; }

    /// <summary>Dernière vérification en ligne qui a autorisé ce poste (UTC).</summary>
    public DateTime? DerniereVerification { get; set; }

    /// <summary>Date la plus récente constatée sur ce poste (UTC).</summary>
    public DateTime? DateMaxVue { get; set; }

    /// <summary>Code du poste lors de la dernière vérification (secours si les identifiants de la machine sont momentanément illisibles).</summary>
    public string? Code { get; set; }

    public string Ecrire() => JsonSerializer.Serialize(this, Options);

    public static EtatDroits Lire(string json) => JsonSerializer.Deserialize<EtatDroits>(json, Options) ?? new EtatDroits();
}

public enum MotifDroits
{
    /// <summary>Poste présent dans la liste reçue du serveur.</summary>
    AutoriseEnLigne,

    /// <summary>Serveur injoignable, dernière vérification réussie datant de moins de 30 jours.</summary>
    AutoriseHorsLigne,

    /// <summary>Contrôle désactivé (version sans clé publique : compilation de développement).</summary>
    ControleInactif,

    /// <summary>Poste absent de la liste.</summary>
    NonAutorise,

    /// <summary>Droit du poste expiré.</summary>
    Expire,

    /// <summary>Serveur injoignable et aucune vérification réussie depuis plus de 30 jours (ou jamais).</summary>
    VerificationNecessaire,

    /// <summary>Horloge du poste antérieure à une date déjà constatée ou à l'émission de la liste.</summary>
    HorlogeIncoherente,
}

/// <summary>Résultat du contrôle : autorisation, motif, échéances et nouvel état à mémoriser.</summary>
public sealed class ResultatDroits
{
    public bool Autorise { get; internal set; }

    public MotifDroits Motif { get; internal set; }

    /// <summary>Date d'expiration du droit du poste (incluse), ou null si sans limite / inconnue.</summary>
    public DateTime? Expiration { get; internal set; }

    /// <summary>Dernière vérification en ligne réussie (UTC).</summary>
    public DateTime? DerniereVerification { get; internal set; }

    /// <summary>Hors ligne : jours restants avant qu'une connexion soit exigée.</summary>
    public int? JoursHorsLigneRestants { get; internal set; }

    /// <summary>État à enregistrer sur le poste (null : rien à changer).</summary>
    public EtatDroits? NouvelEtat { get; internal set; }

    /// <summary>Explication pour l'utilisateur.</summary>
    public string Message { get; internal set; } = string.Empty;

    /// <summary>Avertissement à afficher même si le poste est autorisé (expiration ou délai hors ligne proche), ou null.</summary>
    public string? Avertissement { get; internal set; }
}

/// <summary>
/// Règles des droits d'utilisation (Laurent, 27/11/2026) : au démarrage, MultiDEC lit la liste signée des postes
/// autorisés sur retotub.com. Poste listé et non expiré : lancement. Sinon : message et fermeture. Sans connexion,
/// la dernière vérification réussie reste valable 30 jours ; une horloge reculée est refusée.
/// </summary>
public static class ControleDroits
{
    public const int JoursHorsLigne = 30;

    /// <summary>Avertir quand l'expiration ou la fin du délai hors ligne approche.</summary>
    public const int JoursPreavis = 7;

    /// <summary>Tolérance sur les horloges (fuseaux, petites dérives).</summary>
    public static readonly TimeSpan Tolerance = TimeSpan.FromHours(26);

    /// <summary>
    /// Évalue les droits du poste.
    /// </summary>
    /// <param name="code">Code du poste.</param>
    /// <param name="clePubliqueXml">Clé publique de RETOTUB ; null ou vide = contrôle inactif.</param>
    /// <param name="listeEnLigne">Liste téléchargée (enveloppe signée), ou null si le serveur n'a pas répondu.</param>
    /// <param name="dateServeur">Date donnée par le serveur web (en-tête HTTP), ou null.</param>
    /// <param name="etat">État mémorisé sur le poste, ou null.</param>
    /// <param name="maintenant">Heure du poste (UTC).</param>
    public static ResultatDroits Evaluer(string code, string? clePubliqueXml, string? listeEnLigne, DateTime? dateServeur, EtatDroits? etat, DateTime maintenant)
    {
        if (string.IsNullOrWhiteSpace(clePubliqueXml))
        {
            return new ResultatDroits { Autorise = true, Motif = MotifDroits.ControleInactif, Message = "Contrôle des droits inactif (version de développement)." };
        }

        etat ??= new EtatDroits();
        maintenant = Utc(maintenant);

        // 1. En ligne : la liste reçue, si elle est authentique et pas plus ancienne que la dernière acceptée, fait foi.
        ListePostes? liste = listeEnLigne == null ? null : LireOuNull(listeEnLigne, clePubliqueXml!);
        ListePostes? precedente = etat.ListeSignee == null ? null : LireOuNull(etat.ListeSignee, clePubliqueXml!);
        if (liste != null && precedente != null && liste.Emise < precedente.Emise)
        {
            liste = null; // Liste rejouée (copie ancienne) : ignorée, comme une absence de réponse.
        }

        if (liste != null)
        {
            // Heure de référence : celle du serveur si elle est connue (une horloge locale reculée ne prolonge pas un droit).
            DateTime reference = dateServeur.HasValue ? Utc(dateServeur.Value) : Max(maintenant, liste.Emise);
            var nouvelEtat = new EtatDroits
            {
                ListeSignee = listeEnLigne,
                DerniereVerification = etat.DerniereVerification,
                DateMaxVue = Max(etat.DateMaxVue, Max(maintenant, reference)),
                Code = code,
            };
            var resultat = Decider(code, liste, reference, enLigne: true);
            if (resultat.Autorise)
            {
                nouvelEtat.DerniereVerification = reference;
            }

            resultat.DerniereVerification = nouvelEtat.DerniereVerification;
            resultat.NouvelEtat = nouvelEtat;
            return resultat;
        }

        // 2. Hors ligne : dernière liste acceptée sur ce poste.
        if (precedente == null || etat.DerniereVerification == null)
        {
            return new ResultatDroits
            {
                Motif = MotifDroits.VerificationNecessaire,
                Message = "MultiDEC doit vérifier les droits de ce poste sur le serveur de RETOTUB, mais le serveur est injoignable. "
                          + "Vérifiez la connexion à Internet puis réessayez.",
            };
        }

        DateTime derniere = Utc(etat.DerniereVerification.Value);
        if (maintenant + Tolerance < Max(etat.DateMaxVue, Max(derniere, precedente.Emise)))
        {
            return new ResultatDroits
            {
                Motif = MotifDroits.HorlogeIncoherente,
                DerniereVerification = derniere,
                Message = "La date de ce poste est antérieure à la dernière utilisation de MultiDEC. Corrigez la date et l'heure de Windows, "
                          + "ou connectez le poste à Internet pour vérifier les droits.",
            };
        }

        var horsLigne = Decider(code, precedente, maintenant, enLigne: false);
        horsLigne.DerniereVerification = derniere;
        if (!horsLigne.Autorise)
        {
            return horsLigne;
        }

        double jours = (maintenant - derniere).TotalDays;
        if (jours > JoursHorsLigne)
        {
            return new ResultatDroits
            {
                Motif = MotifDroits.VerificationNecessaire,
                DerniereVerification = derniere,
                Expiration = horsLigne.Expiration,
                Message = $"La dernière vérification des droits date du {derniere.ToLocalTime():dd/MM/yyyy} (plus de {JoursHorsLigne} jours) "
                          + "et le serveur de RETOTUB est injoignable. Connectez le poste à Internet puis réessayez.",
            };
        }

        int restants = Math.Max(0, (int)Math.Floor(JoursHorsLigne - jours));
        horsLigne.Motif = MotifDroits.AutoriseHorsLigne;
        horsLigne.JoursHorsLigneRestants = restants;
        horsLigne.Message = $"Serveur de RETOTUB injoignable : droits vérifiés le {derniere.ToLocalTime():dd/MM/yyyy}, encore valables {restants} jour(s) sans connexion.";
        if (restants <= JoursPreavis && horsLigne.Avertissement == null)
        {
            horsLigne.Avertissement = $"MultiDEC n'a pas pu vérifier ses droits depuis le {derniere.ToLocalTime():dd/MM/yyyy}. "
                                      + $"Connectez ce poste à Internet dans les {restants} jour(s) pour continuer à l'utiliser.";
        }

        horsLigne.NouvelEtat = new EtatDroits
        {
            ListeSignee = etat.ListeSignee,
            DerniereVerification = derniere,
            DateMaxVue = Max(etat.DateMaxVue, maintenant),
            Code = etat.Code ?? code,
        };
        return horsLigne;
    }

    private static ResultatDroits Decider(string code, ListePostes liste, DateTime reference, bool enLigne)
    {
        var poste = liste.Trouver(code);
        if (poste == null)
        {
            return new ResultatDroits
            {
                Motif = MotifDroits.NonAutorise,
                Message = "Ce poste n'est pas autorisé à utiliser MultiDEC. Envoyez le code du poste à RETOTUB pour demander un droit d'utilisation.",
            };
        }

        // Expiration : jour inclus, comparé à la date locale du poste.
        DateTime aujourdhui = reference.ToLocalTime().Date;
        if (poste.Expiration.HasValue && aujourdhui > poste.Expiration.Value.Date)
        {
            return new ResultatDroits
            {
                Motif = MotifDroits.Expire,
                Expiration = poste.Expiration.Value.Date,
                Message = $"Le droit d'utilisation de ce poste a expiré le {poste.Expiration.Value:dd/MM/yyyy}. Contactez RETOTUB pour le prolonger.",
            };
        }

        var resultat = new ResultatDroits
        {
            Autorise = true,
            Motif = enLigne ? MotifDroits.AutoriseEnLigne : MotifDroits.AutoriseHorsLigne,
            Expiration = poste.Expiration?.Date,
            Message = poste.Expiration.HasValue ? $"Poste autorisé jusqu'au {poste.Expiration.Value:dd/MM/yyyy}." : "Poste autorisé (sans limite de date).",
        };
        if (poste.Expiration.HasValue && (poste.Expiration.Value.Date - aujourdhui).TotalDays < JoursPreavis)
        {
            resultat.Avertissement = $"Le droit d'utilisation de ce poste expire le {poste.Expiration.Value:dd/MM/yyyy}. Contactez RETOTUB pour le prolonger.";
        }

        return resultat;
    }

    private static ListePostes? LireOuNull(string enveloppe, string cle)
    {
        try
        {
            return ListePostes.LireSignee(enveloppe, cle);
        }
        catch (Exception ex) when (ex is InvalidDataException || ex is CryptographicException || ex is FormatException)
        {
            return null;
        }
    }

    private static DateTime Utc(DateTime d) => d.Kind == DateTimeKind.Local ? d.ToUniversalTime() : DateTime.SpecifyKind(d, DateTimeKind.Utc);

    private static DateTime Max(DateTime? a, DateTime b) => a.HasValue && Utc(a.Value) > b ? Utc(a.Value) : b;

    /// <summary>Adresse de la liste, à côté de la fiche des mises à jour (…/version.json → …/postes-autorises.json).</summary>
    public static string? AdresseListe(string? adresseFicheVersion)
    {
        if (string.IsNullOrWhiteSpace(adresseFicheVersion))
        {
            return null;
        }

        string a = adresseFicheVersion!.Trim();
        int requete = a.IndexOf('?');
        if (requete >= 0)
        {
            a = a.Substring(0, requete);
        }

        if (a.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            int fin = Math.Max(a.LastIndexOf('/'), a.LastIndexOf('\\'));
            return a.Substring(0, fin + 1) + ListePostes.NomFichier;
        }

        char separateur = a.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? '/' : '\\';
        return a.TrimEnd('/', '\\') + separateur + ListePostes.NomFichier;
    }
}
