using System.Collections.Generic;
using MultiDEC.Core.Catalogue;
using MultiDEC.Core.Geometrie;

namespace MultiDEC.Core.Facade.Regles;

/// <summary>Un élément d'un empilage de poteau : référence et cote du bas.</summary>
public sealed class TronconPoteau
{
    public TronconPoteau(string reference, Famille famille, double z)
    {
        Reference = reference;
        Famille = famille;
        Z = z;
    }

    public string Reference { get; }

    public Famille Famille { get; }

    public double Z { get; }

    public override string ToString() => $"{Reference}@{Z:0.###}";
}

/// <summary>
/// Empilage d'un poteau, du calage au sommet (port de PlacePieds + PlacePoteaux) :
/// HMCALEBOIS / HMSOCREG / départ selon le modèle / poteaux de 2 m / poteau de tête.
/// Cotes de pied relevées dans Blocs-EM-3D.dxf (et non reprises de l'ancien code, dont la bibliothèque
/// avait d'autres points de base) : le point de base de HMSOCREG est le haut du socle (plaque à -0,25 m),
/// HMCALEBOIS fait 0,08 m d'épaisseur.
/// Le sommet visé = dernier plancher, + 1,00 m s'il porte un garde-corps, + 0,50 m en couvreur.
/// </summary>
public static class ReglePoteaux
{
    /// <summary>Décalage vertical propre au Multi-SYSTEM (hauteur de l'embase sous le 1er poteau).</summary>
    public const double DecalageEmbaseMs = 0.046;

    /// <summary>Cote de la cale bois : sous la plaque du socle (-0,25) moins son épaisseur (0,08).</summary>
    public const double ZCaleBois = -0.33;

    /// <summary>Cote du socle réglable : son point de base est le haut de la tige.</summary>
    public const double ZSocle = 0.0;

    /// <param name="depart3m">Départ 3 m (côté extérieur ou file d'extrémité, si l'option est cochée et la hauteur ≥ 3 m).</param>
    /// <param name="gardeCorps">Le poteau porte un garde-corps au dernier niveau.</param>
    public static List<TronconPoteau> Empiler(ParametresFacade p, bool depart3m, bool gardeCorps)
    {
        var socle = CatalogueSocles.De(p.Socle);
        var t = new List<TronconPoteau>();
        if (socle.ZCale is double zCale)
        {
            t.Add(new TronconPoteau(References.CaleBois, Famille.Calage, zCale));
        }

        t.Add(new TronconPoteau(socle.Reference, Famille.Socle, socle.ZSocle));
        if (p.Grutable)
        {
            t.Add(new TronconPoteau(References.SupportGrutable, Famille.Socle, 0.13)); // v2 : +0,20 avec HMSOCREG à +0,07
        }

        double h = p.HauteurDernierPlancher;
        double sommet = h + (gardeCorps ? 1.0 : 0.0) + (gardeCorps && p.GardeCorpsCouvreur ? 0.5 : 0.0);
        depart3m = depart3m && p.Modele != ModeleEchafaudage.MultiRetoEmbase && h + 1.0 >= 3.0 - Cotes.Tolerance;

        double z;            // cote nominale atteinte (sans le décalage MS)
        double decalage = 0; // décalage vertical réel des poteaux
        bool goupilles = false;
        switch (p.Modele)
        {
            case ModeleEchafaudage.MultiReto:
                t.Add(new TronconPoteau(depart3m ? "HMPOTDP30" : "HMPOTDP20", Famille.Poteau, 0));
                z = depart3m ? 3.0 : 2.0;
                break;

            case ModeleEchafaudage.MultiRetoEmbase:
                t.Add(new TronconPoteau(References.Embase, Famille.Embase, 0));
                t.Add(new TronconPoteau("HMPOTDP10E", Famille.Poteau, 0.04));
                z = 1.0;
                break;

            default:
                t.Add(new TronconPoteau(References.Embase, Famille.Embase, 0));
                if (depart3m)
                {
                    t.Add(new TronconPoteau(References.Poteau(p.Modele, 3.0), Famille.Poteau, 0.04));
                    z = 3.0;
                }
                else
                {
                    z = 0.0;
                }

                decalage = DecalageEmbaseMs;
                goupilles = true;
                break;
        }

        // Poteaux de 2 m, puis complément (0,50 / 1,00 / 1,50 / 2,00) jusqu'au sommet visé.
        while (sommet - z > Cotes.Tolerance)
        {
            double l = sommet - z >= 2.0 - Cotes.Tolerance ? 2.0 : Cotes.Arrondi(sommet - z);
            double zReel = Cotes.Arrondi(z + decalage);
            t.Add(new TronconPoteau(References.Poteau(p.Modele, l), Famille.Poteau, zReel));
            if (goupilles && z > Cotes.Tolerance)
            {
                if (p.Grutable)
                {
                    // Grutable : vis + écrou à la place de la goupille (v2).
                    t.Add(new TronconPoteau(References.VisGrutable, Famille.Goupille, zReel));
                    t.Add(new TronconPoteau(References.EcrouGrutable, Famille.Goupille, zReel));
                }
                else
                {
                    t.Add(new TronconPoteau(References.Goupille, Famille.Goupille, zReel));
                }
            }

            z = Cotes.Arrondi(z + l);
        }

        return t;
    }
}
