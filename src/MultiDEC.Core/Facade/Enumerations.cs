namespace MultiDEC.Core.Facade;

/// <summary>Modèles d'échafaudage repris en v3 (Multi-FACADE abandonné, obsolète).</summary>
public enum ModeleEchafaudage
{
    /// <summary>Multi-RETO (MR) : départ HMPOTDP20/30 sur socle, poteaux HMPOTST.</summary>
    MultiReto,

    /// <summary>Multi-RETO à embase (MRE) : HMEMB + HMPOTDP10E, poteaux HMPOTST.</summary>
    MultiRetoEmbase,

    /// <summary>Multi-SYSTEM (MS) : HMEMB, poteaux HMPOT + goupilles OECGOUP8S.</summary>
    MultiSystem,
}

/// <summary>Protection d'un côté (extérieur, intérieur ou about).</summary>
public enum TypeSecurite
{
    Sans,

    // « Garde-corps MDS 1 » retiré des choix (Laurent, 23/11/2026) ; les anciens fichiers passent en MDS 2.

    /// <summary>Garde-corps MDS 2 : HMGCSE + longueur + GT.</summary>
    GardeCorpsMds2,

    /// <summary>Lisse + sous-lisse (à 0,50 et 1,00 m au-dessus du plancher).</summary>
    Lisses,

    /// <summary>Garde-corps d'about HMGCA (abouts uniquement).</summary>
    GardeCorpsAbout,
}

public enum TypePlinthe
{
    Sans,
    Deportees,
    Axees,
    Bois,
}

/// <summary>Rôle d'une maille dans la grille.</summary>
public enum TypeMaille
{
    Travail,
    Acces,

    /// <summary>Maille de travail + accès rapporté de 0,70 m côté extérieur (trappes et échelles), v2.</summary>
    AccesRapporte,

    /// <summary>Maille de travail + module d'approvisionnement de 2,00 m côté extérieur (garde-corps basculant), v2.</summary>
    Appro,

    /// <summary>Maille de travail + recette de profondeur variable côté extérieur (plancher et garde-corps), v2.</summary>
    Recette,

    /// <summary>Maille de travail + tour d'escalier de 1,50 m côté extérieur : deux volées par niveau en sens alterné (v2 Esca).</summary>
    Escalier,

    /// <summary>Maille de travail + demi-escalier de 0,70 m côté extérieur : une volée par niveau, toujours dans le même sens (v2 DemiEsca).</summary>
    DemiEscalier070,

    /// <summary>Maille de travail + demi-escalier de 1,00 m côté extérieur (volées larges 88D), v2 DemiEsca.</summary>
    DemiEscalier100,
}

/// <summary>Type de plancher à un niveau (reprend les choix de l'ancienne grille des niveaux).</summary>
public enum TypePlancher
{
    /// <summary>Plancher complet sur les mailles de travail.</summary>
    Complet,

    /// <summary>Plancher seulement dans les mailles d'accès.</summary>
    AccesSeul,

    /// <summary>Ancien type (fichiers v1/v2) : lu comme « accès seul » avec la case Diago H cochée.</summary>
    AccesSeulDiagoH,
}

/// <summary>
/// Gamme de plateaux (ancien typePlateauxAcier : O, S, F, FAC ; « A » alu/bois supprimé le 23/11/2026).
/// Libellés affichés : <see cref="LibellesPlateaux"/>.
/// </summary>
public enum GammePlateaux
{
    Optissimo,
    Standard,

    /// <summary>« Standard NF » (ancien « Portugais », HMPLAF…3NF).</summary>
    StandardNF,

    /// <summary>« Anti Contamination NF » (ancien « Portugais antidérapant », HMPLAF…NF-AC).</summary>
    AntiContaminationNF,
}

/// <summary>Libellés des gammes de plateaux pour l'interface.</summary>
public static class LibellesPlateaux
{
    public static string Libelle(GammePlateaux gamme)
    {
        switch (gamme)
        {
            case GammePlateaux.StandardNF:
                return "Standard NF";
            case GammePlateaux.AntiContaminationNF:
                return "Anti Contamination NF";
            default:
                return gamme.ToString();
        }
    }
}

/// <summary>Côté d'un élément par rapport à la maille.</summary>
public enum Face
{
    /// <summary>Élément sans côté (traverse, plateau, poteau isolé...).</summary>
    Aucune,
    Interieure,
    Exterieure,
}

/// <summary>
/// Type des traverses des files aux niveaux de plancher complets (liste « Traverses » de la v2).
/// Poutrelle et poutre ne servent qu'au-delà de 1,05 m de largeur (volumique) ; en dessous, renforcé.
/// </summary>
public enum TypeTraverse
{
    Lisse,
    Renforce,
    Poutrelle,
    Poutre,
}
