using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace MultiDEC.Core.Facade;

/// <summary>Forme de la structure (onglet « Général »), qui fixe le nombre de faces.</summary>
public enum FormeStructure
{
    /// <summary>Une face, S1.</summary>
    Droite,

    /// <summary>Deux faces en équerre, S1 et S2.</summary>
    EnL,

    /// <summary>Trois faces, S1 à S3.</summary>
    EnU,

    /// <summary>Quatre faces fermées, S1 à S4.</summary>
    Fermee,
}

/// <summary>
/// Le projet enregistré (.mdc3) : informations d'étude, paramètres généraux, forme et faces de la structure.
/// </summary>
public sealed class ProjetStructure
{
    public string Chantier { get; set; } = string.Empty;

    public string Client { get; set; } = string.Empty;

    public string ReferenceEtude { get; set; } = string.Empty;

    public DateTime Date { get; set; } = DateTime.Today;

    public ParametresGeneraux General { get; set; } = new ParametresGeneraux();

    public FormeStructure Forme { get; set; } = FormeStructure.Droite;

    /// <summary>Faces de la structure, S1 d'abord.</summary>
    public List<FaceEchafaudage> Faces { get; set; } = new List<FaceEchafaudage>();

    /// <summary>Nombre de faces imposé par la forme.</summary>
    public static int NombreDeFaces(FormeStructure forme) => forme switch
    {
        FormeStructure.EnL => 2,
        FormeStructure.EnU => 3,
        FormeStructure.Fermee => 4,
        _ => 1,
    };

    [JsonIgnore]
    public FaceEchafaudage S1 => Faces[0];

    /// <summary>Recopie les paramètres généraux dans chaque face (avant génération ou affichage).</summary>
    public void AppliquerGeneraux()
    {
        foreach (var f in Faces)
        {
            f.Parametres.AppliquerGeneraux(General);
        }
    }

    /// <summary>
    /// Ajuste la liste des faces à la forme : les faces manquantes sont créées sur le modèle de la dernière
    /// (paramètres et grilles copiés), les faces en trop sont retirées.
    /// </summary>
    public void AjusterFaces()
    {
        int n = NombreDeFaces(Forme);
        while (Faces.Count > n)
        {
            Faces.RemoveAt(Faces.Count - 1);
        }

        while (Faces.Count < n)
        {
            // Nouvelle face : mêmes paramètres que la précédente, grilles proposées (avec le carré d'angle).
            var modele = Faces.LastOrDefault();
            var face = new FaceEchafaudage
            {
                Nom = "S" + (Faces.Count + 1),
                Parametres = modele?.Parametres.Copie() ?? new ParametresFacade(),
            };
            Faces.Add(face);
            ProposerFace(Faces.Count - 1);
        }

        for (int i = 0; i < Faces.Count; i++)
        {
            Faces[i].Nom = "S" + (i + 1);
        }

        AppliquerGeneraux();
    }

    /// <summary>
    /// La face commence par le carré d'angle qui la raccorde à la précédente : S2, S3, S4,
    /// et S1 dans une structure fermée (raccord avec S4).
    /// </summary>
    public bool CommenceParCarreDAngle(int indexFace) =>
        indexFace > 0 || (Forme == FormeStructure.Fermee && Faces.Count == 4);

    /// <summary>Remplace les grilles d'une face par la proposition calculée depuis ses paramètres.</summary>
    public void ProposerFace(int indexFace)
    {
        var face = Faces[indexFace];
        face.Parametres.AppliquerGeneraux(General);
        face.Niveaux = ProposeurStructure.ProposerNiveaux(face.Parametres);
        face.Mailles = ProposeurStructure.ProposerMailles(face.Parametres, CommenceParCarreDAngle(indexFace));
    }

    /// <summary>Recopie les paramètres propres à S1 et ses grilles dans une autre face (bouton « Copier depuis S1 »).</summary>
    public void CopierDepuisS1(int indexFace)
    {
        if (indexFace <= 0 || indexFace >= Faces.Count)
        {
            return;
        }

        var copie = S1.Copie();
        copie.Nom = Faces[indexFace].Nom;
        if (CommenceParCarreDAngle(indexFace) && !CommenceParCarreDAngle(0) && copie.Mailles.Count > 0)
        {
            copie.Mailles.Insert(0, new MailleDefinition(copie.Parametres.LargeurMaille, TypeMaille.Travail));
        }

        Faces[indexFace] = copie;
        copie.Parametres.AppliquerGeneraux(General);
    }

    /// <summary>Projet d'une face droite, avec les grilles proposées depuis les paramètres.</summary>
    public static ProjetStructure Nouveau(ParametresGeneraux? general = null, ParametresFacade? face = null)
    {
        var projet = new ProjetStructure { General = general ?? new ParametresGeneraux() };
        var p = face ?? new ParametresFacade();
        p.AppliquerGeneraux(projet.General);
        projet.Faces.Add(ProposeurStructure.NouveauProjet(p));
        return projet;
    }
}
