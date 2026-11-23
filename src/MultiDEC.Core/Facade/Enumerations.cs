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

    /// <summary>Garde-corps MDS 1 : HMGCSE + longueur.</summary>
    GardeCorpsMds1,

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
}

/// <summary>Type de plancher à un niveau (reprend les choix de l'ancienne grille des niveaux).</summary>
public enum TypePlancher
{
    /// <summary>Plancher complet sur les mailles de travail.</summary>
    Complet,

    /// <summary>Plancher seulement dans les mailles d'accès.</summary>
    AccesSeul,

    /// <summary>Accès seul + diagonale horizontale HMDHO dans les mailles de travail.</summary>
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

    /// <summary>« Standard NF » (ancien « Portugais », HMPLAF…CL6).</summary>
    StandardNF,

    /// <summary>« Anti Contamination NF » (ancien « Portugais antidérapant », HMPLAF…CL6-AC).</summary>
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
