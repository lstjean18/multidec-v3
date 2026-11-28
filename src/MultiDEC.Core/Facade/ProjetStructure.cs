using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;
using MultiDEC.Core.Geometrie;

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

    /// <summary>
    /// Structure volumique (v2) : plusieurs modules M1, M2… côte à côte en profondeur, M1 contre la façade,
    /// mêmes mailles, largeur propre à chaque module, poteaux communs entre modules.
    /// </summary>
    Volumique,
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

    /// <summary>Nombre maximum de modules d'une structure volumique.</summary>
    public const int ModulesMaxi = 12;

    /// <summary>Nombre de modules d'une structure volumique (2 à <see cref="ModulesMaxi"/>).</summary>
    public int NombreModules { get; set; } = 2;

    /// <summary>Structure volumique : planchers en damier (une maille sur deux, décalée d'un module à l'autre, v2).</summary>
    public bool PlanchersDamier { get; set; }

    /// <summary>
    /// Structure inversée (L, U, fermée ; Laurent, 24/11/2026) : le bâtiment est de l'autre côté de l'échafaudage,
    /// dans l'angle rentrant (cour intérieure). Les faces tournent vers la droite (-90°) au lieu de la gauche ;
    /// chaque face garde son côté mur côté bâtiment, donc garde-corps et sous-mailles rapportées passent de l'autre côté.
    /// </summary>
    public bool Inversee { get; set; }

    /// <summary>Inversion applicable : structure en L, en U ou fermée.</summary>
    [JsonIgnore]
    public bool InversionPossible => Forme == FormeStructure.EnL || Forme == FormeStructure.EnU || Forme == FormeStructure.Fermee;

    /// <summary>Faces tournées vers la droite (inversion cochée et applicable).</summary>
    [JsonIgnore]
    public bool FacesInversees => Inversee && InversionPossible;

    /// <summary>Faces de la structure, S1 d'abord.</summary>
    public List<FaceEchafaudage> Faces { get; set; } = new List<FaceEchafaudage>();

    /// <summary>Pièces supprimées à la main dans le dessin (point 54) : retirées à chaque génération, enregistrées dans le .mdc3.</summary>
    public List<PieceSupprimee> PiecesSupprimees { get; set; } = new List<PieceSupprimee>();

    /// <summary>
    /// Quantités modifiées et références ajoutées à la main dans le décompte (remarque 61) : appliquées à chaque calcul
    /// du décompte, enregistrées dans le .mdc3.
    /// </summary>
    public List<Decompte.CorrectionDecompte> CorrectionsDecompte { get; set; } = new List<Decompte.CorrectionDecompte>();

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

    [JsonIgnore]
    public bool EstVolumique => Forme == FormeStructure.Volumique;

    /// <summary>Nombre de faces (ou de modules) voulu pour la forme.</summary>
    [JsonIgnore]
    public int NombreDeFacesVoulu => EstVolumique ? Math.Max(2, Math.Min(ModulesMaxi, NombreModules)) : NombreDeFaces(Forme);

    /// <summary>Nom d'une face : S1…S4, ou M1, M2… pour les modules d'une structure volumique.</summary>
    public string NomFace(int index) => (EstVolumique ? "M" : "S") + (index + 1);

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
        int n = NombreDeFacesVoulu;
        while (Faces.Count > n)
        {
            Faces.RemoveAt(Faces.Count - 1);
        }

        while (Faces.Count < n)
        {
            // Nouvelle face : mêmes paramètres que la précédente, grilles proposées (avec le carré d'angle) ;
            // nouveau module d'une structure volumique : mêmes grilles que M1.
            var modele = Faces.LastOrDefault();
            var face = new FaceEchafaudage
            {
                Nom = NomFace(Faces.Count),
                Parametres = modele?.Parametres.Copie() ?? new ParametresFacade(),
            };
            Faces.Add(face);
            if (EstVolumique && Faces.Count > 1)
            {
                CopierDepuisS1(Faces.Count - 1);
                Faces[Faces.Count - 1].Parametres.LargeurMaille = face.Parametres.LargeurMaille;
                SansAcces(Faces[Faces.Count - 1]);
                MarquerValidee(Faces.Count - 1);
            }
            else
            {
                ProposerFace(Faces.Count - 1);
            }
        }

        for (int i = 0; i < Faces.Count; i++)
        {
            Faces[i].Nom = NomFace(i);
        }

        AppliquerGeneraux();
    }

    /// <summary>
    /// La face commence par le carré d'angle qui la raccorde à la précédente : S2, S3, S4,
    /// et S1 dans une structure fermée (raccord avec S4).
    /// </summary>
    public bool CommenceParCarreDAngle(int indexFace) =>
        !EstVolumique && (indexFace > 0 || (Forme == FormeStructure.Fermee && Faces.Count == 4));

    /// <summary>Remplace les grilles d'une face par la proposition calculée depuis ses paramètres.</summary>
    public void ProposerFace(int indexFace)
    {
        var face = Faces[indexFace];
        face.Parametres.AppliquerGeneraux(General);
        if (EstVolumique && indexFace > 0)
        {
            face.Parametres.NombreAcces = 0;
        }

        face.Niveaux = ProposeurStructure.ProposerNiveaux(face.Parametres);
        face.Mailles = ProposeurStructure.ProposerMailles(face.Parametres, CommenceParCarreDAngle(indexFace), LargeurFacePrecedente(indexFace));
        MarquerValidee(indexFace);
    }

    // ---- « Valider la structure » (Laurent, 24/11/2026) ----------------------------------------

    /// <summary>
    /// Paramètres dont dépend la proposition des grilles d'une face : longueur, largeur, maille préférée,
    /// accès, dernier plancher, carré d'angle (et largeur de la face précédente), structure volumique.
    /// </summary>
    public string SignatureProposition(int indexFace)
    {
        var p = Faces[indexFace].Parametres;
        bool carre = CommenceParCarreDAngle(indexFace);
        return string.Join("|", new[]
        {
            Texte(p.LongueurTotaleSouhaitee), Texte(p.LargeurMaille), Texte(p.LongueurMaillePreferee),
            p.NombreAcces.ToString(CultureInfo.InvariantCulture), Texte(p.HauteurDernierPlancher),
            carre ? "angle " + Texte(LargeurFacePrecedente(indexFace)) : "-", EstVolumique ? "volumique" : "-",
        });
    }

    /// <summary>Un paramètre de la face a changé depuis sa dernière validation : le bouton passe au rouge.</summary>
    public bool FaceAValider(int indexFace) =>
        Faces[indexFace].SignatureValidee is string s && s != SignatureProposition(indexFace);

    /// <summary>Première face à valider, -1 si toutes sont à jour.</summary>
    public int PremiereFaceAValider()
    {
        for (int i = 0; i < Faces.Count; i++)
        {
            if (FaceAValider(i))
            {
                return i;
            }
        }

        return -1;
    }

    public void MarquerValidee(int indexFace) => Faces[indexFace].SignatureValidee = SignatureProposition(indexFace);

    /// <summary>Projet ouvert sans signature (enregistré avant le bouton) : faces considérées comme validées.</summary>
    public void MarquerFacesSansSignature()
    {
        for (int i = 0; i < Faces.Count; i++)
        {
            if (Faces[i].SignatureValidee == null)
            {
                MarquerValidee(i);
            }
        }
    }

    private static string Texte(double v) => Cotes.Arrondi(v).ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>Largeur de la face à laquelle se raccorde le carré d'angle de la face <paramref name="indexFace"/>.</summary>
    public double LargeurFacePrecedente(int indexFace)
    {
        int precedente = indexFace == 0 ? Faces.Count - 1 : indexFace - 1;
        return Faces[precedente].Parametres.LargeurMaille;
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
            copie.Mailles.Insert(0, new MailleDefinition(LargeurFacePrecedente(indexFace), TypeMaille.Travail));
        }

        Faces[indexFace] = copie;
        copie.Parametres.AppliquerGeneraux(General);
        MarquerValidee(indexFace);
    }

    /// <summary>
    /// Module volumique au-delà de M1 : les accès ne sont proposés que sur M1 (Laurent, 24/11/2026) ;
    /// l'utilisateur peut ensuite en ajouter dans la grille.
    /// </summary>
    private static void SansAcces(FaceEchafaudage face)
    {
        face.Parametres.NombreAcces = 0;
        foreach (var m in face.Mailles.Where(m => m.Type == TypeMaille.Acces))
        {
            m.Type = TypeMaille.Travail;
        }
    }

    /// <summary>Projet d'une face droite, avec les grilles proposées depuis les paramètres.</summary>
    public static ProjetStructure Nouveau(ParametresGeneraux? general = null, ParametresFacade? face = null)
    {
        var projet = new ProjetStructure { General = general ?? new ParametresGeneraux() };
        var p = face ?? new ParametresFacade();
        p.AppliquerGeneraux(projet.General);
        projet.Faces.Add(ProposeurStructure.NouveauProjet(p));
        projet.MarquerValidee(0);
        return projet;
    }
}
