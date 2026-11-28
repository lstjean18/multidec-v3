using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using MultiDEC.Core.Geometrie;

namespace MultiDEC.Core.TourEscalier;

/// <summary>Configuration des paliers d'une tour escalier alu (v2 clTESCALU.eTypeTour).</summary>
public enum ConfigurationTour
{
    /// <summary>Paliers tous les 2,00 m (le premier à 1,00 m si la hauteur est impaire).</summary>
    Niveaux2m,

    /// <summary>Paliers alternés 1,00 m / 2,00 m : 1, 3, 4, 6, 7… (hauteur multiple de 3 + 2 impossible).</summary>
    Alternes1m2m,
}

/// <summary>Côté de la tour. Repère local : X sur la longueur (0 → 2,50), Y de 0 (côté A) à -largeur (côté C).</summary>
public enum CoteTour
{
    /// <summary>Grand côté Y = 0 : côté bâtiment, ou côté façade quand la tour est accolée.</summary>
    A,

    /// <summary>Bout X = 2,50 m.</summary>
    B,

    /// <summary>Grand côté Y = -largeur : côté extérieur.</summary>
    C,

    /// <summary>Bout X = 0.</summary>
    D,
}

/// <summary>
/// Noms des côtés pour l'utilisateur (Laurent 28/11/2026 : « A » et « C » ne parlent pas) : gauche et droite vus de
/// l'extérieur, face au bâtiment.
/// </summary>
public static class NomsCotesTour
{
    public static string Nom(CoteTour cote) => cote switch
    {
        CoteTour.A => "côté bâtiment",
        CoteTour.B => "bout droit",
        CoteTour.C => "côté extérieur",
        _ => "bout gauche",
    };

    /// <summary>Nom avec une majuscule, pour un en-tête ou une liste.</summary>
    public static string Titre(CoteTour cote)
    {
        string n = Nom(cote);
        return char.ToUpperInvariant(n[0]) + n.Substring(1);
    }
}

/// <summary>Tenue de la tour quand elle n'est pas accolée à une façade.</summary>
public enum StabiliteTour
{
    /// <summary>Amarrée au bâtiment par le côté A (tubes d'amarrage tous les 4 m, comme les extrémités de façade).</summary>
    Amarree,

    /// <summary>Autostable lestée : rien n'est dessiné, une alerte rappelle de faire calculer le lest.</summary>
    Lestee,

    /// <summary>
    /// Stabilisée par une structure de base côté C (Laurent, 28/11/2026, plan LV Tec O240610) : modules de 2,50 m contre le
    /// côté C, vers le bout opposé à l'entrée, diagonales verticales et horizontales ; autostable pour un vent &lt; 85 km/h.
    /// </summary>
    Stabilisee,
}

/// <summary>Une sortie de la tour : ouverture d'un côté à un palier.</summary>
public sealed class SortieTour
{
    public SortieTour()
    {
    }

    public SortieTour(double hauteur, CoteTour cote, double largeur = 0)
    {
        Hauteur = hauteur;
        Cote = cote;
        Largeur = largeur;
    }

    /// <summary>Cote du palier, en mètres.</summary>
    public double Hauteur { get; set; }

    public CoteTour Cote { get; set; }

    /// <summary>
    /// Largeur de l'ouverture : 0,50 ou 1,00 m sur un grand côté (v2) ; sur un bout, imposée par la largeur de la tour
    /// (0,80 m pour 1,50 m, 1,00 m pour 2,00 m). 0 = valeur par défaut du côté.
    /// </summary>
    public double Largeur { get; set; }

    /// <summary>
    /// Sortie acrotère (v2 « Ouvrir + Sortie Acrotère ») : ouverture de 1,00 m, pont de franchissement de l'acrotère, puis
    /// structure de sortie avec une volée de 1 m jusqu'au toit, 1,00 m sous le palier.
    /// </summary>
    public bool Acrotere { get; set; }

    /// <summary>Longueur du pont de franchissement de l'acrotère : 0,70 / 1,00 / 1,50 / 2,00 m (v2).</summary>
    public double LongueurPont { get; set; } = 1.50;

    /// <summary>Structure de sortie tournée à 90° (v2 « T ») plutôt que droite dans le prolongement du pont (« D »).</summary>
    public bool Tournee { get; set; }

    public override string ToString() => FormattableString.Invariant($"{Hauteur:0.00} {Cote} {Largeur:0.00}{(Acrotere ? $" acrotère {LongueurPont:0.00}{(Tournee ? " T" : " D")}" : string.Empty)}");
}

/// <summary>
/// Tour escalier à volées aluminium (port de clTESCALU, branche version2024) : 2,50 m de long, 1,50 ou 2,00 m de large,
/// une volée par palier dans chaque moitié de la largeur, en sens alternés.
/// La tour est un ouvrage du projet (T1, T2…), seule ou accolée à une face de la façade.
/// </summary>
public sealed class TourEscalierAlu
{
    /// <summary>Longueur de la tour : volées de 2,50 m seulement (Laurent, 28/11/2026 ; 3,00 m plus tard).</summary>
    public const double Longueur = 2.50;

    /// <summary>Largeurs proposées (v2 eLargeurTour).</summary>
    public static IReadOnlyList<double> Largeurs { get; } = new[] { 1.50, 2.00 };

    /// <summary>Hauteur maximale proposée pour le dernier palier.</summary>
    public const double HauteurMaxi = 60.0;

    /// <summary>Nom de l'ouvrage : T1, T2…</summary>
    public string Nom { get; set; } = "T1";

    public double Largeur { get; set; } = 1.50;

    public ConfigurationTour Configuration { get; set; } = ConfigurationTour.Niveaux2m;

    /// <summary>Cote du dernier palier (multiple de 0,50 m). En xx,50 m, la tour part d'une structure de départ de 0,50 m.</summary>
    public double Hauteur { get; set; } = 6.0;

    /// <summary>Départ poteaux 3 m (v2 Pot3m).</summary>
    public bool Depart3m { get; set; }

    /// <summary>Bout par lequel on entre au sol (pied de la première volée) : D (X = 0) ou B (X = 2,50).</summary>
    public CoteTour Entree { get; set; } = CoteTour.D;

    public StabiliteTour Stabilite { get; set; } = StabiliteTour.Amarree;

    /// <summary>Distance entre le côté A et le mur (tubes d'amarrage), comme la distance à la façade d'un échafaudage.</summary>
    public double DistanceBatiment { get; set; } = 0.20;

    /// <summary>Hauteur de la structure de stabilisation (3,00 m par défaut, règle selon la hauteur de la tour à venir).</summary>
    public double HauteurStabilisation { get; set; } = 3.00;

    /// <summary>Sorties (ouvertures) choisies par l'utilisateur, palier par palier.</summary>
    public List<SortieTour> Sorties { get; set; } = new List<SortieTour>();

    // ---- Position dans le projet ----------------------------------------------------------

    /// <summary>Tour seule : origine (coin X = 0, Y = 0 de la tour) dans le repère du projet.</summary>
    public double PositionX { get; set; } = -6.0;

    public double PositionY { get; set; }

    /// <summary>Tour seule : rotation autour de Z, en degrés (0, 90, 180, 270).</summary>
    public double Angle { get; set; }

    /// <summary>Face de la façade à laquelle la tour est accolée (S1…S4), null si la tour est seule.</summary>
    public string? FaceAccolee { get; set; }

    /// <summary>Tour accolée : abscisse du bout D de la tour le long de la face, en mètres depuis le début de la face.</summary>
    public double Abscisse { get; set; }

    /// <summary>Tour accolée : écart entre la file extérieure de la façade et les poteaux du côté A de la tour.</summary>
    public double Ecart { get; set; } = 0.10;

    [JsonIgnore]
    public bool EstAccolee => !string.IsNullOrEmpty(FaceAccolee);

    /// <summary>Hauteur de la structure de départ : 0,50 m quand le dernier palier est en xx,50 m (Laurent, 28/11/2026).</summary>
    [JsonIgnore]
    public double HauteurDepart => Cotes.Egal(Math.Round(Hauteur * 2) % 2, 1) ? 0.5 : 0.0;

    /// <summary>Largeur d'une volée : 0,65 m (HMESCALU250200) dans une tour de 1,50 m, 0,88 m (…88D) dans une tour de 2,00 m.</summary>
    [JsonIgnore]
    public double LargeurVolee => VoleeLarge ? 0.88 : 0.65;

    [JsonIgnore]
    public bool VoleeLarge => Largeur > 1.5 + Cotes.Tolerance;

    /// <summary>Copie indépendante (sorties comprises).</summary>
    public TourEscalierAlu Copie()
    {
        var t = (TourEscalierAlu)MemberwiseClone();
        t.Sorties = Sorties.Select(s => new SortieTour(s.Hauteur, s.Cote, s.Largeur) { Acrotere = s.Acrotere, LongueurPont = s.LongueurPont, Tournee = s.Tournee }).ToList();
        return t;
    }

    /// <summary>
    /// Message si la hauteur ne convient pas (null sinon) : multiple de 0,50 m, au moins 1,00 m au-dessus du départ,
    /// et en paliers alternés, pas de hauteur « multiple de 3 + 2 » (v2 TextBoxHauteur_LostFocus).
    /// </summary>
    public string? ErreurHauteur()
    {
        if (Hauteur <= 0 || !Cotes.Egal(Hauteur * 2, Math.Round(Hauteur * 2)))
        {
            return "La hauteur du dernier palier doit être un multiple de 0,50 m.";
        }

        if (Hauteur > HauteurMaxi + Cotes.Tolerance)
        {
            return FormattableString.Invariant($"Hauteur limitée à {HauteurMaxi:0} m.");
        }

        double utile = Cotes.Arrondi(Hauteur - HauteurDepart);
        if (utile < 1.0 - Cotes.Tolerance)
        {
            return "La tour doit monter d'au moins 1,00 m au-dessus de son départ.";
        }

        if (Configuration == ConfigurationTour.Alternes1m2m && (int)Math.Round(utile) % 3 == 2)
        {
            return string.Format(CultureNombres.Documents,
                "Avec des paliers alternés 1 m / 2 m, on ne peut pas atteindre {0:0.00} m : choisissez une hauteur qui ne soit pas « multiple de 3 + 2 » au-dessus du départ.",
                Hauteur);
        }

        return null;
    }

    /// <summary>Cotes des paliers, du départ (0 ou 0,50 m) au dernier palier compris.</summary>
    public IReadOnlyList<double> Paliers()
    {
        double zb = HauteurDepart;
        int utile = (int)Math.Round(Hauteur - zb);
        var montees = new List<int>();
        if (Configuration == ConfigurationTour.Niveaux2m)
        {
            for (int i = utile % 2; i < utile; i += 2)
            {
                montees.Add(2);
            }

            if (utile % 2 == 1)
            {
                // Hauteur impaire : volée de 1 m en haut de la tour (Laurent, 28/11/2026, inverse de la v2 qui la mettait
                // au pied) ; au pied, ses plateaux couperaient la traverse intermédiaire.
                montees.Add(1);
            }
        }
        else
        {
            int z = 0;
            int m = 1;
            while (z + m <= utile)
            {
                montees.Add(m);
                z += m;
                m = m == 1 ? 2 : 1;
            }

            if (z < utile)
            {
                montees.Add(utile - z); // hauteur impossible : signalée par ErreurHauteur, on termine quand même
            }
        }

        var paliers = new List<double> { zb };
        double cote = zb;
        foreach (int m in montees)
        {
            cote = Cotes.Arrondi(cote + m);
            paliers.Add(cote);
        }

        return paliers;
    }

    /// <summary>Largeur de l'ouverture d'une sortie : celle demandée sur un grand côté (0,50 / 1,00), imposée sur un bout.</summary>
    /// <summary>Longueurs de pont d'acrotère proposées (v2).</summary>
    public static readonly double[] LongueursPont = { 0.70, 1.00, 1.50, 2.00 };

    /// <summary>Hauteur du toit sous le palier de sortie acrotère (v2 : 1,00 m, structure de sortie posée sur le toit).</summary>
    public const double HauteurAcrotere = 1.00;

    public double LargeurOuverture(SortieTour s)
    {
        if (s.Acrotere)
        {
            return 1.00; // v2 : ConstruitFaceOuverte(1) pour une sortie acrotère
        }

        if (s.Cote == CoteTour.B || s.Cote == CoteTour.D)
        {
            return VoleeLarge ? 1.00 : 0.80;
        }

        return Cotes.Egal(s.Largeur, 0.5) ? 0.50 : 1.00;
    }

    /// <summary>Sortie par défaut d'une nouvelle tour : au dernier palier, sur le bout où arrive la dernière volée.</summary>
    public void SortieParDefaut()
    {
        Sorties.Clear();
        var paliers = Paliers();
        Sorties.Add(new SortieTour(paliers[paliers.Count - 1], BoutArrivee(paliers.Count - 1)));
    }

    /// <summary>Bout (B ou D) vers lequel monte la volée qui arrive au palier d'indice <paramref name="indexPalier"/> (≥ 1).</summary>
    public CoteTour BoutArrivee(int indexPalier)
    {
        // La première volée monte vers le bout opposé à l'entrée, puis le sens alterne à chaque volée.
        bool versB = Entree == CoteTour.D;
        if (indexPalier % 2 == 0)
        {
            versB = !versB;
        }

        return versB ? CoteTour.B : CoteTour.D;
    }

    public override string ToString() =>
        FormattableString.Invariant($"{Nom} {Longueur:0.00} x {Largeur:0.00} ht {Hauteur:0.00} {Configuration}");
}
