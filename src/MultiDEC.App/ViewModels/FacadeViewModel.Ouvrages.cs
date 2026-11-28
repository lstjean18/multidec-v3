using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using MultiDEC.Core.Facade;
using MultiDEC.Core.TourEscalier;

namespace MultiDEC.App.ViewModels;

/// <summary>
/// Projet multi-ouvrages (Laurent, 28/11/2026) : la façade (faces S1…) et les tours escalier alu (T1, T2…).
/// Ruban « Ouvrages » : + Façade, + Tour escalier alu, supprimer l'ouvrage ; décompte filtrable par ouvrage.
/// </summary>
public sealed partial class FacadeViewModel
{
    /// <summary>Valeur du filtre du décompte pour la façade (toutes ses faces).</summary>
    public const string OuvrageFacade = "Façade";

    private string? _ouvrageDecompte;

    /// <summary>Tours du projet, un onglet chacune après les faces.</summary>
    public ObservableCollection<TourViewModel> Tours { get; } = new ObservableCollection<TourViewModel>();

    /// <summary>Les ouvrages ont changé (façade ajoutée ou retirée, tour ajoutée ou supprimée) : onglets à reconstruire.</summary>
    public event EventHandler? OuvragesChanges;

    /// <summary>Le projet contient la façade (onglets S1…).</summary>
    public bool AvecFacade
    {
        get => _projet.AvecFacade;
        set
        {
            if (value == _projet.AvecFacade || (!value && Tours.Count == 0))
            {
                return; // il reste toujours au moins un ouvrage
            }

            _projet.AvecFacade = value;
            OuvragesModifies();
        }
    }

    public bool PeutAjouterTour => Tours.Count < ProjetStructure.ToursMaxi;

    /// <summary>Ajoute une tour T1, T2… (à gauche des ouvrages existants) et renvoie son ViewModel.</summary>
    public TourViewModel AjouterTour()
    {
        var vm = new TourViewModel(_projet.AjouterTour(), this);
        Tours.Add(vm);
        OuvragesModifies();
        return vm;
    }

    /// <summary>Supprime une tour ; refusé si c'est le dernier ouvrage du projet.</summary>
    public bool SupprimerTour(TourViewModel tour)
    {
        if (!_projet.AvecFacade && Tours.Count == 1)
        {
            return false;
        }

        _projet.Tours.Remove(tour.Tour);
        Tours.Remove(tour);
        OuvragesModifies();
        return true;
    }

    /// <summary>Nouveau projet de tour seule (bouton « Nouveau », choix « Tour escalier alu ») : façade mise de côté.</summary>
    public TourViewModel NouveauProjetTour()
    {
        Nouveau();
        var tour = AjouterTour();
        tour.PositionX = 0;
        _projet.AvecFacade = false;
        OuvragesModifies();
        return tour;
    }

    public TourViewModel? TourNommee(string nom) => Tours.FirstOrDefault(t => t.Nom == nom);

    // ---- Décompte par ouvrage ---------------------------------------------------------------

    /// <summary>Choix du filtre du décompte : tout le projet, la façade, chaque tour.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> ChoixOuvragesDecompte
    {
        get
        {
            var choix = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>(string.Empty, "Tout le projet") };
            if (_projet.AvecFacade)
            {
                choix.Add(new KeyValuePair<string, string>(OuvrageFacade, "Façade"));
            }

            choix.AddRange(Tours.Select(t => new KeyValuePair<string, string>(t.Nom, "Tour escalier alu " + t.Nom)));
            return choix;
        }
    }

    /// <summary>Ouvrage affiché dans le décompte (vide = tout le projet).</summary>
    public string OuvrageDecompte
    {
        get => _ouvrageDecompte ?? string.Empty;
        set
        {
            string? v = string.IsNullOrEmpty(value) ? null : value;
            if (v == _ouvrageDecompte)
            {
                return;
            }

            _ouvrageDecompte = v;
            CalculerDecompte();
            Notifier(nameof(OuvrageDecompte));
            Notifier(nameof(DecompteFiltre));
            Notifier(nameof(DecompteModifiable));
        }
    }

    /// <summary>Le décompte n'affiche qu'un ouvrage : quantités calculées, sans les modifications faites à la main.</summary>
    public bool DecompteFiltre => _ouvrageDecompte != null;

    /// <summary>Quantités modifiables et références ajoutables : seulement sur le décompte de tout le projet.</summary>
    public bool DecompteModifiable => !DecompteFiltre;

    /// <summary>Éléments comptés dans le décompte selon le filtre.</summary>
    private IEnumerable<ElementPlace> ElementsDuDecompte(StructureFacade structure)
    {
        if (_ouvrageDecompte == null)
        {
            return structure.Elements;
        }

        if (_ouvrageDecompte == OuvrageFacade)
        {
            return structure.Elements.Where(e => !_projet.Tours.Any(t => t.Nom == e.NomFace));
        }

        return structure.Elements.Where(e => e.NomFace == _ouvrageDecompte);
    }

    private List<Core.Decompte.CorrectionDecompte> CorrectionsDuDecompte() =>
        _ouvrageDecompte == null ? _projet.CorrectionsDecompte : new List<Core.Decompte.CorrectionDecompte>();

    // ---- Chargement -------------------------------------------------------------------------

    /// <summary>Projet chargé : ViewModels des tours, filtre du décompte remis à tout le projet.</summary>
    private void ChargerOuvrages()
    {
        _ouvrageDecompte = null;
        Tours.Clear();
        foreach (var t in _projet.Tours)
        {
            Tours.Add(new TourViewModel(t, this));
        }

        OuvragesChanges?.Invoke(this, EventArgs.Empty);
    }

    private void OuvragesModifies()
    {
        if (_ouvrageDecompte != null && !ChoixOuvragesDecompte.Any(c => c.Key == _ouvrageDecompte))
        {
            _ouvrageDecompte = null;
            CalculerDecompte();
        }

        Notifier(nameof(AvecFacade));
        Notifier(nameof(PeutAjouterTour));
        Notifier(nameof(ChoixOuvragesDecompte));
        Notifier(nameof(OuvrageDecompte));
        Notifier(nameof(DecompteFiltre));
        Notifier(nameof(DecompteModifiable));
        OuvragesChanges?.Invoke(this, EventArgs.Empty);
    }
}
