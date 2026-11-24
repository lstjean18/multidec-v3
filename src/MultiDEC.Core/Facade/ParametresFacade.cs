using System.Text.Json.Serialization;

namespace MultiDEC.Core.Facade;

/// <summary>
/// Paramètres d'une face, tels que le moteur les lit : ceux propres à la face (longueur, hauteur, options…)
/// et une copie des paramètres généraux (<see cref="ParametresGeneraux"/>, marqués [JsonIgnore] ici,
/// enregistrés une seule fois au niveau de la structure et recopiés par <see cref="AppliquerGeneraux"/>).
/// Toutes les cotes sont en mètres.
/// </summary>
public sealed class ParametresFacade
{
    // ---- Structure -------------------------------------------------------------------------

    [JsonIgnore] // paramètre général : vient de ParametresGeneraux
    public ModeleEchafaudage Modele { get; set; } = ModeleEchafaudage.MultiSystem;

    /// <summary>Largeur de maille (distance entre file intérieure et file extérieure) : 0,70 / 0,75 / 1,00 / 1,05.</summary>
    public double LargeurMaille { get; set; } = 0.70;

    /// <summary>Type des traverses des files aux niveaux de plancher complets (Lisse, Renforcé, Poutrelle, Poutre).</summary>
    public TypeTraverse Traverses { get; set; } = TypeTraverse.Lisse;

    /// <summary>Ancienne case « Traverses renforcées » (fichiers v1 à v3 du 23/11) : lue, jamais écrite.</summary>
    [JsonInclude]
    [JsonPropertyName("renforce")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    internal bool? AncienRenforce
    {
        get => null;
        set
        {
            if (value == true && Traverses == TypeTraverse.Lisse)
            {
                Traverses = TypeTraverse.Renforce;
            }
        }
    }

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

    [JsonIgnore] // paramètre général : vient de ParametresGeneraux
    public GammePlateaux Plateaux { get; set; } = GammePlateaux.StandardNF;

    /// <summary>Qualité plateau (1 ou 2), utilisée par la gamme Standard pour 2,50 et 3,00 m.</summary>
    [JsonIgnore] // paramètre général : vient de ParametresGeneraux
    public int QualitePlateaux { get; set; } = 1;

    /// <summary>Socle ou vérin sous chaque poteau.</summary>
    [JsonIgnore] // paramètre général : vient de ParametresGeneraux
    public string Socle { get; set; } = Catalogue.CatalogueSocles.ParDefaut;

    /// <summary>
    /// Accès par trappes et plateaux acier (v2) : dans les mailles d'accès de 2 m et plus, trappe de 1,00 m
    /// + traverse intermédiaire HMTIP + plateaux sur le reste. Incompatible avec les garde-corps MDS,
    /// remplacés par des lisses sur la face.
    /// </summary>
    public bool TrappesAcier { get; set; }

    /// <summary>Lisse de garde-corps à 1,50 m au niveau 0 dans les mailles d'accès (option v2).</summary>
    public bool LisseGcAccesNiveau0 { get; set; }

    /// <summary>
    /// Structure grutable (accessoires de levage, v2) : support HMSUPSUNI au pied de chaque poteau et
    /// goupilles remplacées par vis VSZ10070 + écrou VEF10.
    /// </summary>
    public bool Grutable { get; set; }

    // ---- Sécurité --------------------------------------------------------------------------

    [JsonIgnore] // paramètre général : vient de ParametresGeneraux
    public TypeSecurite SecuriteExterieure { get; set; } = TypeSecurite.GardeCorpsMds2;

    [JsonIgnore] // paramètre général : vient de ParametresGeneraux
    public TypeSecurite SecuriteInterieure { get; set; } = TypeSecurite.Sans;

    [JsonIgnore] // paramètre général : vient de ParametresGeneraux
    public TypeSecurite SecuriteAbouts { get; set; } = TypeSecurite.GardeCorpsMds2;

    [JsonIgnore] // paramètre général : vient de ParametresGeneraux
    public TypePlinthe PlintheExterieure { get; set; } = TypePlinthe.Axees;

    [JsonIgnore] // paramètre général : vient de ParametresGeneraux
    public TypePlinthe PlintheInterieure { get; set; } = TypePlinthe.Sans;

    [JsonIgnore] // paramètre général : vient de ParametresGeneraux
    public TypePlinthe PlintheAbouts { get; set; } = TypePlinthe.Axees;

    [JsonIgnore] // paramètre général : vient de ParametresGeneraux
    public bool AboutGauche { get; set; } = true;

    [JsonIgnore] // paramètre général : vient de ParametresGeneraux
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
    [JsonIgnore] // paramètre général : vient de ParametresGeneraux
    public bool Bache { get; set; }

    /// <summary>Hauteur du premier amarrage.</summary>
    [JsonIgnore] // paramètre général : vient de ParametresGeneraux
    public double HauteurPremierAmarrage { get; set; } = 2.0;

    /// <summary>Espacement vertical des amarrages sur les files d'extrémité.</summary>
    [JsonIgnore] // paramètre général : vient de ParametresGeneraux
    public double EspacementAmarrageExtremite { get; set; } = 4.0;

    /// <summary>Espacement vertical des amarrages sur les files courantes, non bâché.</summary>
    [JsonIgnore] // paramètre général : vient de ParametresGeneraux
    public double EspacementAmarrageCourant { get; set; } = 8.0;

    /// <summary>Espacement vertical des amarrages sur les files courantes, bâché.</summary>
    [JsonIgnore] // paramètre général : vient de ParametresGeneraux
    public double EspacementAmarrageCourantBache { get; set; } = 4.0;

    /// <summary>Au-delà de cette hauteur, une note de calcul est obligatoire.</summary>
    [JsonIgnore] // paramètre général : vient de ParametresGeneraux
    public double SeuilNoteDeCalcul { get; set; } = 24.0;

    /// <summary>Recopie les paramètres généraux de la structure dans cette face.</summary>
    public void AppliquerGeneraux(ParametresGeneraux g)
    {
        Modele = g.Modele;
        Plateaux = g.Plateaux;
        QualitePlateaux = g.QualitePlateaux;
        Socle = g.Socle;
        SecuriteExterieure = g.SecuriteExterieure;
        SecuriteInterieure = g.SecuriteInterieure;
        SecuriteAbouts = g.SecuriteAbouts;
        PlintheExterieure = g.PlintheExterieure;
        PlintheInterieure = g.PlintheInterieure;
        PlintheAbouts = g.PlintheAbouts;
        AboutGauche = g.AboutGauche;
        AboutDroit = g.AboutDroit;
        Bache = g.Bache;
        HauteurPremierAmarrage = g.HauteurPremierAmarrage;
        EspacementAmarrageExtremite = g.EspacementAmarrageExtremite;
        EspacementAmarrageCourant = g.EspacementAmarrageCourant;
        EspacementAmarrageCourantBache = g.EspacementAmarrageCourantBache;
        SeuilNoteDeCalcul = g.SeuilNoteDeCalcul;
    }

    public ParametresFacade Copie() => (ParametresFacade)MemberwiseClone();
}
