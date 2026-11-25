using System;
using System.Linq;
using MultiDEC.Core.Geometrie;

namespace MultiDEC.Core.Facade;

/// <summary>
/// Colonne « Départ » : hauteur du sol sous chaque maille, pour un échafaudage sur un sol en pente (Laurent, 24/11/2026).
/// La maille commence à cette hauteur : socles, poteaux, lisses de pied et diagonales partent de là, les planchers
/// et pièces plus bas sont retirés. Une file partagée par deux mailles part du plus bas des deux départs.
/// </summary>
public sealed partial class GenerateurFacade
{
    private double[] _departs = Array.Empty<double>();

    /// <summary>Hauteur de départ retenue pour la maille (index), 0 si aucune ou ignorée.</summary>
    private double Depart(int index) => index >= 0 && index < _departs.Length ? _departs[index] : 0;

    /// <summary>Départ d'une file : le plus bas des départs des mailles qui la touchent.</summary>
    private double DepartFile(int file) => IndexVoisines(file).Select(Depart).DefaultIfEmpty(0).Min();

    private void ValiderDeparts()
    {
        _departs = new double[_projet.Mailles.Count];
        for (int i = 0; i < _projet.Mailles.Count; i++)
        {
            var m = _projet.Mailles[i];
            double d = m.HauteurDepart;
            if (d <= Cotes.Tolerance)
            {
                continue;
            }

            string raison =
                !m.DepartValide ? "elle doit être un multiple de 0,50 m"
                : d > m.HauteurMax(_h) - 0.5 + Cotes.Tolerance ? "elle doit rester au moins 0,50 m sous le dernier plancher de la maille"
                : m.LargeurRapportee > Cotes.Tolerance || m.EstEscalier ? "sous-maille rapportée ou escalier posés depuis le sol"
                : m.Stabilisation > Cotes.Tolerance ? "stabilisation posée depuis le sol"
                : m.Poutre > Cotes.Tolerance || PoutreSous(i) != null ? "maille à poutre"
                : string.Empty;
            if (raison.Length > 0)
            {
                _alertes.Add(FormattableString.Invariant($"Maille {i + 1} : hauteur de départ {d:0.00} m ignorée ({raison})."));
                continue;
            }

            _departs[i] = d;
        }
    }

    /// <summary>Retire les pièces d'une maille posées sous sa hauteur de départ (planchers, lisses, accès…).</summary>
    private void RetirerSousDeparts()
    {
        if (_departs.All(d => d <= Cotes.Tolerance))
        {
            return;
        }

        _el.RemoveAll(e => e.NumeroMaille > 0 && e.Origine.Z < Depart(e.NumeroMaille - 1) - Cotes.Tolerance);
    }
}
