using System;
using MultiDEC.Core.Facade;
using MultiDEC.Core.Geometrie;

namespace MultiDEC.Core.Catalogue;

/// <summary>
/// Règles de nommage des références RETOTUB (= noms de blocs), reprises de l'ancien Facade.vb.
/// Toutes les longueurs sont en mètres.
/// </summary>
public static class References
{
    public const string CaleBois = "HMCALEBOIS";
    public const string SocleReglable = "HMSOCREG";
    public const string Embase = "HMEMB";
    public const string Goupille = "OECGOUP8S";
    public const string Echelle = "HMPLALECHELLE";

    /// <summary>
    /// Lisse (L &gt; 1,05 m) ou traverse (L ≤ 1,05 m) : HMLIS250, HMTRA070.
    /// Version renforcée : HMLISR250 pour une lisse, HMTRA100R pour une traverse (nommage du DXF).
    /// </summary>
    public static string LisseOuTraverse(double longueur, bool renforce = false)
    {
        string cm = Cotes.EnCm3(longueur);
        if (longueur > 1.05 + Cotes.Tolerance)
        {
            return renforce ? "HMLISR" + cm : "HMLIS" + cm;
        }

        return renforce ? "HMTRA" + cm + "R" : "HMTRA" + cm;
    }

    /// <summary>Diagonale verticale de 2 m de haut : HMDIA + longueur + "2" (ex. HMDIA2502).</summary>
    public static string DiagonaleVerticale(double longueurMaille) => "HMDIA" + Cotes.EnCm3(longueurMaille) + "2";

    /// <summary>Diagonale horizontale : HMDHO + largeur + longueur (ex. HMDHO070250).</summary>
    public static string DiagonaleHorizontale(double largeur, double longueur) =>
        "HMDHO" + Cotes.EnCm3(largeur) + Cotes.EnCm3(longueur);

    /// <summary>Garde-corps pour une longueur donnée, ou null si la sécurité n'est pas un cadre (Sans, Lisses).</summary>
    public static string? GardeCorps(TypeSecurite type, double longueur) => type switch
    {
        TypeSecurite.GardeCorpsMds1 => "HMGCSE" + Cotes.EnCm3(longueur),
        TypeSecurite.GardeCorpsMds2 => "HMGCSE" + Cotes.EnCm3(longueur) + "GT",
        TypeSecurite.GardeCorpsAbout => "HMGCA" + Cotes.EnCm3(longueur),
        _ => null,
    };

    public static string? Plinthe(TypePlinthe type, double longueur) => type switch
    {
        TypePlinthe.Deportees => "HMPLIM" + Cotes.EnCm3(longueur) + "DEP",
        TypePlinthe.Axees => "HMPLIM" + Cotes.EnCm3(longueur) + "AX",
        TypePlinthe.Bois => "HMPLINBO" + Cotes.EnCm3(longueur),
        _ => null,
    };

    /// <summary>Base des poteaux courants : HMPOT (Multi-SYSTEM) ou HMPOTST (Multi-RETO).</summary>
    public static string BasePoteau(ModeleEchafaudage modele) =>
        modele == ModeleEchafaudage.MultiSystem ? "HMPOT" : "HMPOTST";

    public static string Poteau(ModeleEchafaudage modele, double longueur) => BasePoteau(modele) + Cotes.EnDm2(longueur);

    /// <summary>Poteau de garde-corps en bout de console (1,00 m).</summary>
    public static string PoteauGardeCorpsConsole(ModeleEchafaudage modele) => modele switch
    {
        ModeleEchafaudage.MultiSystem => "HMPOT10",
        ModeleEchafaudage.MultiReto => "HMPOTDP10",
        _ => "HMPOTST10",
    };

    /// <summary>Rehausse couvreur en bout de console (0,50 m).</summary>
    public static string PoteauCouvreurConsole(ModeleEchafaudage modele) =>
        modele == ModeleEchafaudage.MultiSystem ? "HMPOT05" : "HMPOTST05";

    /// <summary>
    /// Plateau acier/alu (port de RefPlateau). <paramref name="variante"/> 1 = plateau courant, 2 = plateau de complément.
    /// Le code longueur est la longueur de la maille.
    /// </summary>
    public static string Plateau(GammePlateaux gamme, double longueur, double largeurMaille, int variante, int qualite = 1)
    {
        string l = Cotes.EnCm3(longueur);
        bool large = Cotes.Egal(largeurMaille, 1.05) || Cotes.Egal(largeurMaille, 0.75);
        bool l300 = Cotes.Egal(longueur, 3.0);
        switch (gamme)
        {
            case GammePlateaux.Optissimo:
                return variante == 1 ? "HMPLA" + l + "E" : "HMPLA" + l + "2CL" + (l300 ? "5" : "6");

            case GammePlateaux.Standard:
                if (variante != 1)
                {
                    return "HMPLA" + l + "2CL" + (l300 ? "5" : "6");
                }

                string baseRef = "HMPLA" + l + (large ? "3CL" : "298CL");
                string classe = "6";
                if (Cotes.Egal(longueur, 2.5))
                {
                    classe = qualite == 1 ? "6" : "5";
                }
                else if (l300)
                {
                    classe = qualite == 1 ? "5" : "4";
                }

                return baseRef + classe;

            case GammePlateaux.StandardNF:
                return variante == 1 ? "HMPLAF" + l + "3CL6" : "HMPLAF" + l + "2CL6";

            case GammePlateaux.AntiContaminationNF:
                return variante == 1 ? "HMPLAF" + l + "3CL6-AC" : "HMPLAF" + l + "2CL6-AC";

            default:
                throw new ArgumentOutOfRangeException(nameof(gamme), gamme, null);
        }
    }

    /// <summary>Plateau à trappe : HMPLAL + longueur + 602TR (largeurs « rondes ») ou 650TR (0,75 / 1,05), + EC au-delà de 2 m.</summary>
    public static string PlateauTrappe(double longueur, double largeurMaille)
    {
        bool large = Cotes.Egal(largeurMaille, 1.05) || Cotes.Egal(largeurMaille, 0.75);
        return "HMPLAL" + Cotes.EnCm3(longueur) + (large ? "650TR" : "602TR") + (longueur > 2.0 + Cotes.Tolerance ? "EC" : string.Empty);
    }

    /// <summary>
    /// Tube d'amarrage le plus court couvrant la longueur nécessaire (NT-Echafaudage p. 13) :
    /// HMTUBAMA0500, HMTUBAMA (1,20 m), HMTUBAMA1500, HMTUBAMA2000.
    /// </summary>
    public static string TubeAmarrage(double longueurNecessaire)
    {
        if (longueurNecessaire <= 0.50 + Cotes.Tolerance)
        {
            return "HMTUBAMA0500";
        }

        if (longueurNecessaire <= 1.20 + Cotes.Tolerance)
        {
            return "HMTUBAMA";
        }

        return longueurNecessaire <= 1.50 + Cotes.Tolerance ? "HMTUBAMA1500" : "HMTUBAMA2000";
    }
}
