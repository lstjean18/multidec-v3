using System.Collections.Generic;
using MultiDEC.Core.Facade;

namespace MultiDEC.App.ViewModels;

/// <summary>Libellés affichés des valeurs d'énumération (listes de choix et grilles).</summary>
public static class Libelles
{
    private static readonly Dictionary<object, string> Table = new Dictionary<object, string>
    {
        [ModeleEchafaudage.MultiReto] = "Multi-RETO",
        [ModeleEchafaudage.MultiRetoEmbase] = "Multi-RETO à embase",
        [ModeleEchafaudage.MultiSystem] = "Multi-SYSTEM",
        [TypeSecurite.Sans] = "Sans",
        [TypeSecurite.GardeCorpsMds2] = "MDS 2",
        [TypeSecurite.Lisses] = "Lisses",
        [TypeSecurite.GardeCorpsAbout] = "Garde-corps d'about",
        [TypePlinthe.Sans] = "Sans",
        [TypePlinthe.Axees] = "Axée",
        [TypePlinthe.Deportees] = "Déportée",
        [TypePlinthe.Bois] = "Bois",
        [TypeMaille.Travail] = "Travail",
        [TypeMaille.Acces] = "Accès",
        [TypePlancher.Complet] = "Complet",
        [TypePlancher.AccesSeul] = "Accès seul",
        [TypePlancher.AccesSeulDiagoH] = "Accès seul + diago H",
        [TypeTraverse.Lisse] = "Lisse",
        [TypeTraverse.Renforce] = "Renforcé",
        [TypeTraverse.Poutrelle] = "Poutrelle",
        [TypeTraverse.Poutre] = "Poutre",
        [FormeStructure.Droite] = "Droite",
        [FormeStructure.EnL] = "En L",
        [FormeStructure.EnU] = "En U",
        [FormeStructure.Fermee] = "Fermée",
    };

    public static string De(object? valeur)
    {
        if (valeur == null)
        {
            return string.Empty;
        }

        if (valeur is GammePlateaux g)
        {
            return LibellesPlateaux.Libelle(g);
        }

        return Table.TryGetValue(valeur, out var libelle) ? libelle : valeur.ToString() ?? string.Empty;
    }
}
