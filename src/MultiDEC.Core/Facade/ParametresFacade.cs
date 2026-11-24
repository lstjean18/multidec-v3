namespace MultiDEC.Core.Facade;

/// <summary>
/// Paramètres saisis par l'utilisateur pour un échafaudage de façade.
/// Voir le document « Paramètres utilisateur – module échafaudage de façade (v3) ».
/// Toutes les cotes sont en mètres.
/// </summary>
public sealed class ParametresFacade
{
    // ---- Structure -------------------------------------------------------------------------

    public ModeleEchafaudage Modele { get; set; } = ModeleEchafaudage.MultiSystem;

    /// <summary>Largeur de maille (distance entre file intérieure et file extérieure) : 0,70 / 0,75 / 1,00 / 1,05.</summary>
    public double LargeurMaille { get; set; } = 0.70;

    /// <summary>Traverses renforcées (suffixe R) aux niveaux de plancher complets.</summary>
    public bool Renforce { get; set; }

    /// <summary>Longueur de façade souhaitée, utilisée pour proposer la grille des mailles.</summary>
    public double LongueurTotaleSouhaitee { get; set; } = 20.0;

    /// <summary>Longueur de maille préférée pour la proposition de grille.</summary>
    public double LongueurMaillePreferee { get; set; } = 2.50;

    /// <summary>Nombre de mailles d'accès (trappes + échelles) à répartir.</summary>
    public int NombreAcces { get; set; } = 1;

    /// <summary>Hauteur du dernier plancher (multiple de 0,50 m).</summary>
    public double HauteurDernierPlancher { get; set; } = 10.0;

    /// <summary>Départ des poteaux de 3 m côté extérieur (et aux extrémités) au lieu de 2 m. Sans effet en Multi-RETO à embase.</summary>
    public bool Depart3mExterieur { get; set; }

    /// <summary>Distance entre la file intérieure et la façade (0 à 0,20 m).</summary>
    public double DistanceFacade { get; set; } = 0.20;

    /// <summary>Lisses en pied (niveau 0) sur toute la longueur.</summary>
    public bool LissesEnPied { get; set; }

    // ---- Planchers -------------------------------------------------------------------------

    public GammePlateaux Plateaux { get; set; } = GammePlateaux.StandardNF;

    /// <summary>Qualité plateau (1 ou 2), utilisée par la gamme Standard pour 2,50 et 3,00 m.</summary>
    public int QualitePlateaux { get; set; } = 1;

    // ---- Sécurité --------------------------------------------------------------------------

    public TypeSecurite SecuriteExterieure { get; set; } = TypeSecurite.GardeCorpsMds2;

    public TypeSecurite SecuriteInterieure { get; set; } = TypeSecurite.Sans;

    public TypeSecurite SecuriteAbouts { get; set; } = TypeSecurite.GardeCorpsMds2;

    public TypePlinthe PlintheExterieure { get; set; } = TypePlinthe.Axees;

    public TypePlinthe PlintheInterieure { get; set; } = TypePlinthe.Sans;

    public TypePlinthe PlintheAbouts { get; set; } = TypePlinthe.Axees;

    public bool AboutGauche { get; set; } = true;

    public bool AboutDroit { get; set; } = true;

    /// <summary>Garde-corps couvreur : poteaux et lisses prolongés à 1,50 m au-dessus du dernier plancher.</summary>
    public bool GardeCorpsCouvreur { get; set; }

    // ---- Diagonales ------------------------------------------------------------------------

    /// <summary>Option « Diagonales 1 maille sur 2 » (ancien DIAGOPLUS) ; sinon une maille sur 5.</summary>
    public bool DiagonalesRenforcees { get; set; }

    /// <summary>Force une diagonale dans la première maille, même quand il y a peu de mailles.</summary>
    public bool DiagonalePremiereMaille { get; set; } = true;

    // ---- Amarrages (NT-Echafaudage p. 8-13, validé par Laurent le 23/11/2026) ---------------

    /// <summary>Échafaudage bâché (porosité &gt; 50 %) : amarrages tous les 4 m sur toutes les files.</summary>
    public bool Bache { get; set; }

    /// <summary>Hauteur du premier amarrage.</summary>
    public double HauteurPremierAmarrage { get; set; } = 2.0;

    /// <summary>Espacement vertical des amarrages sur les files d'extrémité.</summary>
    public double EspacementAmarrageExtremite { get; set; } = 4.0;

    /// <summary>Espacement vertical des amarrages sur les files courantes, non bâché.</summary>
    public double EspacementAmarrageCourant { get; set; } = 8.0;

    /// <summary>Espacement vertical des amarrages sur les files courantes, bâché.</summary>
    public double EspacementAmarrageCourantBache { get; set; } = 4.0;

    /// <summary>Au-delà de cette hauteur, une note de calcul est obligatoire.</summary>
    public double SeuilNoteDeCalcul { get; set; } = 24.0;

    public ParametresFacade Copie() => (ParametresFacade)MemberwiseClone();
}
