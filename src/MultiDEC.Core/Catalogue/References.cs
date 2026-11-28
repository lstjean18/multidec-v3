using System;
using System.Linq;
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

    /// <summary>Structure grutable : support au pied, vis et écrou à la place des goupilles (v2).</summary>
    public const string SupportGrutable = "HMSUPSUNI";

    /// <summary>Pare-gravois (v2) : console à chaque file, tôles de 2,00 m posées tous les 0,80 m, crochets.</summary>
    public const string ConsolePareGravois = "HMPARGRA";
    public const string TolePareGravois = "HMTOG200";
    public const string CrochetPareGravois = "HMCROCHE";
    public const string VisGrutable = "VSZ10070";
    public const string EcrouGrutable = "VEF10";
    public const string Echelle = "HMPLALECHELLE";

    /// <summary>
    /// Lisse (L &gt; 1,05 m) ou traverse (L ≤ 1,05 m) : HMLIS250, HMTRA070.
    /// Version renforcée : HMLISR250 pour une lisse, HMTRA100R pour une traverse (nommage du DXF).
    /// </summary>
    public static string LisseOuTraverse(double longueur, bool renforce = false) =>
        LisseOuTraverse(longueur, renforce ? TypeTraverse.Renforce : TypeTraverse.Lisse);

    /// <summary>
    /// Lisse ou traverse selon le type (GetRefLisse de la v2) : jusqu'à 1,05 m HMTRA, suffixe R au-delà de 0,75 m
    /// si elle n'est pas simple ; au-delà, HMLIS / HMLISR / HMPTRL (renforcé jusqu'à 1,50 m) / HMPOUT.
    /// </summary>
    public static string LisseOuTraverse(double longueur, TypeTraverse type)
    {
        string cm = Cotes.EnCm3(longueur);
        if (longueur <= 1.05 + Cotes.Tolerance)
        {
            return "HMTRA" + cm + (longueur > 0.75 + Cotes.Tolerance && type != TypeTraverse.Lisse ? "R" : string.Empty);
        }

        if (type == TypeTraverse.Poutrelle && longueur <= 1.50 + Cotes.Tolerance)
        {
            type = TypeTraverse.Renforce;
        }

        return type switch
        {
            TypeTraverse.Renforce => "HMLISR" + cm,
            TypeTraverse.Poutrelle => "HMPTRL" + cm,
            TypeTraverse.Poutre => "HMPOUT" + cm,
            _ => "HMLIS" + cm,
        };
    }

    /// <summary>Support du garde-corps d'approvisionnement, fixé sur les lisses latérales (NT-GC_Approvisionnement).</summary>
    public const string SupportAppro = "HMGCAPPROSUP";

    /// <summary>Cadre basculeur du garde-corps d'approvisionnement.</summary>
    public const string CadreAppro = "HMGCAPPRO-C";

    /// <summary>Garde-corps d'approvisionnement entre les cadres : longueur de maille - 0,50 m (HMGCAPPROGC150 / 200 / 250).</summary>
    public static string GardeCorpsAppro(double longueur) => "HMGCAPPROGC" + Cotes.EnCm3(longueur);

    /// <summary>Poutre de franchissement (v2 GetRefPoutre) : HMPOUT + longueur, suffixe E en Multi-SYSTEM.</summary>
    public static string PoutreFranchissement(ModeleEchafaudage modele, double longueur) =>
        "HMPOUT" + Cotes.EnCm3(longueur) + (modele == ModeleEchafaudage.MultiSystem ? "E" : string.Empty);

    /// <summary>Tubes acier disponibles (HMTUB + longueur en cm).</summary>
    public static readonly double[] LongueursTubes = { 0.70, 1.00, 1.20, 1.50, 2.00, 2.50, 3.00, 4.00, 5.00, 6.00 };

    /// <summary>Longueur du tube le plus court couvrant une longueur, ou null au-delà de 6,00 m.</summary>
    public static double? LongueurTube(double longueur) =>
        LongueursTubes.Where(l => l >= longueur - Cotes.Tolerance).Select(l => (double?)l).FirstOrDefault();

    /// <summary>Tube acier : HMTUB + longueur en cm (ex. HMTUB300).</summary>
    public static string Tube(double longueur) => "HMTUB" + Cotes.EnCm3(longueur);

    /// <summary>Collier rosace sur poteau (marche intermédiaire d'une tour à départ de 0,50 m).</summary>
    public const string CollierRosace = "HMCOL49ROSACE";

    /// <summary>Console de 0,30 m de la marche intermédiaire d'une tour à départ de 0,50 m (Laurent, 28/11/2026).</summary>
    public const string ConsoleMarche = "HMCON300";

    /// <summary>Collier fixe pour tube de 49.</summary>
    public const string CollierFixe = "HMCOLF49";

    /// <summary>Piton d'amarrage (1 par amarrage, comme la v2 ; compté au décompte, non dessiné).</summary>
    public const string PitonAmarrage = "EZPITON12";

    /// <summary>Cheville d'amarrage (1 par amarrage, comme la v2 ; comptée au décompte, non dessinée).</summary>
    public const string ChevilleAmarrage = "EZCHEVILLE14";

    /// <summary>Quincaillerie comptée au décompte mais sans bloc à dessiner (pitons et chevilles d'amarrage).</summary>
    public static bool NonDessinee(string reference) =>
        reference == PitonAmarrage || reference == ChevilleAmarrage;

    /// <summary>
    /// Volée d'escalier alu (v2) : HMESCALU + longueur de maille en cm + montée en cm (+ "88D" pour la volée large
    /// des demi-escaliers de 1,00 m). Montée de 1,00 m : volée courte de 1,75 m (HMESCALU175100).
    /// </summary>
    public static string Volee(double longueurMaille, double montee, bool large) =>
        "HMESCALU" + (montee < 1.5 ? "175" : Cotes.EnCm3(longueurMaille)) + Cotes.EnCm3(montee) + (large ? "88D" : string.Empty);

    /// <summary>
    /// Garde-corps de volée : HMGCESCALU100 (volée de 1 m), HMGCESCALU200 (maille de 2,50 m), HMGCESCALU300 (maille de 3,00 m,
    /// bibliothèque du 26/11/2026) ; pas d'autre longueur dans la bibliothèque.
    /// </summary>
    public static string? GardeCorpsVolee(double longueurMaille, double montee) =>
        montee < 1.5 ? "HMGCESCALU100"
        : Cotes.Egal(longueurMaille, 2.5) ? "HMGCESCALU200"
        : Cotes.Egal(longueurMaille, 3.0) ? "HMGCESCALU300"
        : null;

    /// <summary>Extrémité de garde-corps de volée (pied de la première volée, haut de la dernière).</summary>
    public const string GardeCorpsVoleeExtremite = "HMGCESCALUEXTR";

    /// <summary>Montant de garde-corps sur lisse (ouverture sur l'escalier) : HMMONTGCE en MS, HMMONTGC sinon.</summary>
    public static string MontantGardeCorps(ModeleEchafaudage modele) =>
        modele == ModeleEchafaudage.MultiSystem ? "HMMONTGCE" : "HMMONTGC";

    /// <summary>Piquage sur lisse (poteau de 1 m du garde-corps central des demi-escaliers) : HMPIQLIS-E en MS.</summary>
    public static string PiquageLisse(ModeleEchafaudage modele) =>
        modele == ModeleEchafaudage.MultiSystem ? "HMPIQLIS-E" : "HMPIQLIS";

    /// <summary>Diagonale verticale de 2 m de haut : HMDIA + longueur + "2" (ex. HMDIA2502).</summary>
    public static string DiagonaleVerticale(double longueurMaille) => "HMDIA" + Cotes.EnCm3(longueurMaille) + "2";

    /// <summary>Diagonale horizontale : HMDHO + largeur + longueur (ex. HMDHO070250).</summary>
    public static string DiagonaleHorizontale(double largeur, double longueur) =>
        "HMDHO" + Cotes.EnCm3(largeur) + Cotes.EnCm3(longueur);

    /// <summary>Garde-corps pour une longueur donnée, ou null si la sécurité n'est pas un cadre (Sans, Lisses).</summary>
    public static string? GardeCorps(TypeSecurite type, double longueur) => type switch
    {
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
                // Bibliothèque du 24/11/2026 : HMPLAF…3NF (ex-…3CL6) ; compléments de 0,20 m HMPLAF…2CL6 dans les deux gammes NF.
                return variante == 1 ? "HMPLAF" + l + "3NF" : "HMPLAF" + l + "2CL6";

            case GammePlateaux.AntiContaminationNF:
                // Bibliothèque du 26/11/2026 : HMPLAF…3NF-AC (ex-…3CL6-AC), compléments HMPLAF…2CL6-AC.
                return variante == 1 ? "HMPLAF" + l + "3NF-AC" : "HMPLAF" + l + "2CL6-AC";

            default:
                throw new ArgumentOutOfRangeException(nameof(gamme), gamme, null);
        }
    }

    /// <summary>
    /// Plateau acier de complément (HMPLA + longueur + 3CL6 : 0,75 / 1,00 / 1,05 / 1,50 / 1,75 m dans la bibliothèque),
    /// au pied d'une volée de 1 m sortie de la tour d'escalier.
    /// </summary>
    public static string PlateauAcierComplement(double longueur) => "HMPLA" + Cotes.EnCm3(longueur) + "3CL6";

    /// <summary>Plateau à trappe : HMPLAL + longueur + 602TR (largeurs « rondes ») ou 650TR (0,75 / 1,05), + EC au-delà de 2 m.</summary>
    /// <summary>
    /// Trappe de 1,00 m de l'option « trappes et plateaux acier » (v2) : HMPLAL100602TR, ou HMPLAL100650TR pour les mailles
    /// de 0,75 / 1,05 m (bibliothèque du 26/11/2026).
    /// </summary>
    public static string TrappeAcier(double largeurMaille) =>
        Cotes.Egal(largeurMaille, 1.05) || Cotes.Egal(largeurMaille, 0.75) ? "HMPLAL100650TR" : "HMPLAL100602TR";

    /// <summary>Portillon d'about (v2) : HMPORTILLONPLIN + largeur en cm (070, 075, 100, 105).</summary>
    public static string Portillon(double largeur) => "HMPORTILLONPLIN" + Cotes.EnCm3(largeur);

    /// <summary>Échelle d'about montant au plancher de hauteur z (HMECH050LIS, HMECH100LIS, HMECH150LIS), ou null.</summary>
    public static string? EchelleAbout(double z) =>
        Cotes.Egal(z, 0.5) || Cotes.Egal(z, 1.0) || Cotes.Egal(z, 1.5) ? "HMECH" + Cotes.EnCm3(z) + "LIS" : null;

    /// <summary>Traverse intermédiaire de plancher (HMTIP + largeur de maille en cm).</summary>
    public static string TraverseIntermediaire(double largeurMaille) => "HMTIP" + Cotes.EnCm3(largeurMaille);

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
