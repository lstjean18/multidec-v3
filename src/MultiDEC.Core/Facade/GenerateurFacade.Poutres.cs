using System;
using System.Collections.Generic;
using System.Linq;
using MultiDEC.Core.Catalogue;
using MultiDEC.Core.Facade.Regles;
using MultiDEC.Core.Geometrie;

namespace MultiDEC.Core.Facade;

/// <summary>
/// Poutres de franchissement (port de clPoutre v2) : une poutre posée sous un plancher couvre deux mailles de même
/// longueur ; la file entre les deux est supprimée sous ce plancher (passage). Deux poutres HMPOUT (file intérieure et
/// extérieure) à 0,50 m sous le plancher, traverses à leurs extrémités et au milieu, un tube en diagonale horizontale
/// par maille, diagonales verticales au-dessus, abouts sur les mailles voisines sous la poutre.
/// Règles v2 : ni sur la première ni sur la dernière maille, mailles de travail de même longueur, une maille au moins
/// entre deux poutres.
/// </summary>
public sealed partial class GenerateurFacade
{
    /// <summary>Hauteur d'une poutre de franchissement (sa membrure haute porte le plancher).</summary>
    public const double HauteurPoutre = 0.50;

    /// <summary>Poutres valides : index de la maille de gauche → hauteur du plancher porté.</summary>
    private readonly Dictionary<int, double> _poutres = new Dictionary<int, double>();

    private void ValiderPoutres()
    {
        var mailles = _projet.Mailles;
        for (int i = 0; i < mailles.Count; i++)
        {
            double zp = mailles[i].Poutre;
            if (zp <= Cotes.Tolerance)
            {
                continue;
            }

            string? refus = null;
            if (i == 0 || i + 1 >= mailles.Count - 1)
            {
                refus = "ni sur la première ni sur la dernière maille";
            }
            else if (mailles[i].Type != TypeMaille.Travail || mailles[i + 1].Type != TypeMaille.Travail)
            {
                refus = "seulement sur deux mailles de travail simples";
            }
            else if (!Cotes.Egal(mailles[i].Longueur, mailles[i + 1].Longueur))
            {
                refus = "les deux mailles doivent avoir la même longueur";
            }
            else if (_poutres.ContainsKey(i - 1) || _poutres.ContainsKey(i - 2))
            {
                refus = "il faut une maille de séparation entre deux poutres";
            }
            else if (!_projet.Niveaux.Any(n => Cotes.Egal(n.Hauteur, zp)) || zp < 2.0 - Cotes.Tolerance)
            {
                refus = "la hauteur doit être celle d'un niveau de plancher, 2 m au moins";
            }
            else if (!new[] { 3.0, 4.0, 5.0, 6.0 }.Any(l => Cotes.Egal(l, 2 * mailles[i].Longueur)))
            {
                refus = "poutres de 3, 4, 5 ou 6 m seulement (mailles de 1,50 à 3,00 m)";
            }

            if (refus != null)
            {
                _alertes.Add(FormattableString.Invariant($"Maille {i + 1} : poutre à {zp:0.00} m ignorée ({refus})."));
                continue;
            }

            _poutres[i] = Cotes.Arrondi(zp);
        }
    }

    /// <summary>Plancher porté par une poutre qui couvre la maille (null sinon) : rien n'est posé dessous.</summary>
    private double? PoutreSous(int indexMaille) =>
        _poutres.TryGetValue(indexMaille, out double z) || _poutres.TryGetValue(indexMaille - 1, out z) ? z : (double?)null;

    /// <summary>File supprimée sous une poutre (file du milieu) : hauteur du plancher porté, null sinon.</summary>
    private double? PoutreSousFile(int file) => _poutres.TryGetValue(file - 1, out double z) ? z : (double?)null;

    /// <summary>L'élément d'une maille à la cote z est supprimé par une poutre (dessous).</summary>
    private bool SousPoutre(int indexMaille, double z) => PoutreSous(indexMaille) is double zp && z < zp - Cotes.Tolerance;

    private void PlacerPoutres(List<Niveau> niveaux)
    {
        foreach (var kv in _poutres)
        {
            int i = kv.Key;
            double zp = kv.Value;
            double lm = _projet.Mailles[i].Longueur;
            double xa = _x[i];
            double xm = _x[i + 1];
            double xb = _x[i + 2];
            double zb = Cotes.Arrondi(zp - HauteurPoutre);
            string poutre = References.PoutreFranchissement(_p.Modele, Cotes.Arrondi(xb - xa));

            Ajouter(poutre, Famille.Poutre, new Point3(xa, 0, zb), Axe.X, Face.Interieure, i + 1);
            Ajouter(poutre, Famille.Poutre, new Point3(xa, -_l, zb), Axe.X, Face.Exterieure, i + 1);
            foreach (double x in new[] { xa, xm, xb })
            {
                Ajouter(References.LisseOuTraverse(_l), Famille.Traverse, new Point3(x, 0, zb), Axe.Y, Face.Aucune, i + 1);
            }

            // Tube en diagonale horizontale par maille, d'un coin extérieur gauche au coin intérieur droit, 2 colliers.
            double d = Math.Sqrt(lm * lm + _l * _l);
            double angle = Math.Atan2(_l, lm) * 180 / Math.PI;
            double? lTube = References.LongueurTube(d + 0.20);
            double zt = Cotes.Arrondi(zb + 0.18);
            foreach (var (x0, num) in new[] { (xa, i + 1), (xm, i + 2) })
            {
                if (lTube == null)
                {
                    _alertes.Add($"Maille {num} : diagonale de poutre trop longue pour un tube de 6 m.");
                    continue;
                }

                string tube = References.Tube(lTube.Value);
                double a = angle * Math.PI / 180;
                double recul = (lTube.Value - d) / 2; // tube centré sur la diagonale
                var origine = new Point3(Cotes.Arrondi(x0 - recul * Math.Cos(a) - 0.06 * Math.Sin(a)), Cotes.Arrondi(-_l - recul * Math.Sin(a) + 0.06 * Math.Cos(a)), zt);
                Ajouter(tube, Famille.Poutre, origine, Axe.X, Face.Aucune, num, rotation: angle);
                Ajouter(References.CollierFixe, Famille.Poutre, new Point3(x0, -_l, zt), Axe.Z, Face.Aucune, num, rotation: angle);
                Ajouter(References.CollierFixe, Famille.Poutre, new Point3(x0 + lm, 0, zt), Axe.Z, Face.Aucune, num, rotation: angle);
            }

            // Diagonales verticales au-dessus de la poutre, en V (v2 AjouteDiagoV), si un niveau suit.
            if (zp <= _h - 2.0 + Cotes.Tolerance)
            {
                string dia = References.DiagonaleVerticale(lm);
                Ajouter(dia, Famille.DiagonaleVerticale, new Point3(xm, -_l - DemiTube, zp), Axe.X, Face.Exterieure, i + 1, inverse: true);
                Ajouter(dia, Famille.DiagonaleVerticale, new Point3(xm, -_l - DemiTube, zp), Axe.X, Face.Exterieure, i + 2);
                Ajouter(dia, Famille.DiagonaleVerticale, new Point3(xm, DemiTube, zp), Axe.X, Face.Interieure, i + 1, inverse: true);
                Ajouter(dia, Famille.DiagonaleVerticale, new Point3(xm, DemiTube, zp), Axe.X, Face.Interieure, i + 2);
            }

            // Abouts des mailles voisines sous la poutre (passage), niveaux avec plancher.
            foreach (var n in niveaux.Where(n => n.Hauteur > Cotes.Tolerance && n.Hauteur < zp - Cotes.Tolerance))
            {
                if (!n.EstAccesSeul || _projet.Mailles[i - 1].Type == TypeMaille.Acces)
                {
                    PlacerAbout(xa, n.Hauteur, _l, gauche: false, console: false, i);
                }

                if (i + 2 < _projet.Mailles.Count && (!n.EstAccesSeul || _projet.Mailles[i + 2].Type == TypeMaille.Acces))
                {
                    PlacerAbout(xb, n.Hauteur, _l, gauche: true, console: false, i + 3);
                }
            }
        }
    }

    /// <summary>Amarrages d'une file supprimée sous une poutre : retirés.</summary>
    private BilanAmarrages SansAmarragesSousPoutres(BilanAmarrages bilan)
    {
        if (_poutres.Count == 0)
        {
            return bilan;
        }

        var restants = bilan.Amarrages.Where(a => !(PoutreSousFile(a.File) is double zp && a.Hauteur < zp - Cotes.Tolerance)).ToList();
        return new BilanAmarrages(restants, bilan.Surface, bilan.MinimumParSurface, bilan.NoteDeCalculObligatoire);
    }
}
