namespace MultiDEC.Core.Facade;

/// <summary>
/// Paramètres communs à toutes les faces de la structure (onglet « Général ») : matériel, protections, amarrages.
/// Recopiés dans chaque face avant la génération (<see cref="ParametresFacade.AppliquerGeneraux"/>).
/// Mêmes noms et mêmes valeurs par défaut que dans <see cref="ParametresFacade"/>.
/// </summary>
public sealed class ParametresGeneraux
{
    // ---- Matériel ----------------------------------------------------------------------------

    public ModeleEchafaudage Modele { get; set; } = ModeleEchafaudage.MultiSystem;

    public GammePlateaux Plateaux { get; set; } = GammePlateaux.StandardNF;

    /// <summary>Qualité plateau (1 ou 2), utilisée par la gamme Standard pour 2,50 et 3,00 m.</summary>
    public int QualitePlateaux { get; set; } = 1;

    // ---- Protections -------------------------------------------------------------------------

    public TypeSecurite SecuriteExterieure { get; set; } = TypeSecurite.GardeCorpsMds2;

    public TypeSecurite SecuriteInterieure { get; set; } = TypeSecurite.Sans;

    public TypeSecurite SecuriteAbouts { get; set; } = TypeSecurite.GardeCorpsMds2;

    public TypePlinthe PlintheExterieure { get; set; } = TypePlinthe.Axees;

    public TypePlinthe PlintheInterieure { get; set; } = TypePlinthe.Sans;

    public TypePlinthe PlintheAbouts { get; set; } = TypePlinthe.Axees;

    public bool AboutGauche { get; set; } = true;

    public bool AboutDroit { get; set; } = true;

    // ---- Amarrages (NT-Echafaudage p. 8-13) --------------------------------------------------

    public bool Bache { get; set; }

    public double HauteurPremierAmarrage { get; set; } = 2.0;

    public double EspacementAmarrageExtremite { get; set; } = 4.0;

    public double EspacementAmarrageCourant { get; set; } = 8.0;

    public double EspacementAmarrageCourantBache { get; set; } = 4.0;

    public double SeuilNoteDeCalcul { get; set; } = 24.0;
}
