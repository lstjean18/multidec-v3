namespace MultiDEC.Core.Facade.Regles;

/// <summary>
/// Plinthe proposée quand l'utilisateur choisit une protection (Laurent, 24/11/2026) :
/// garde-corps MDS 2 → plinthe bois, lisses → plinthe axée, sans protection → sans plinthe.
/// L'utilisateur peut ensuite changer la plinthe à la main.
/// </summary>
public static class ReglePlinthes
{
    /// <summary>Plinthe associée à la protection, ou null pour garder le choix actuel.</summary>
    public static TypePlinthe? Proposer(TypeSecurite securite) => securite switch
    {
        TypeSecurite.GardeCorpsMds2 => TypePlinthe.Bois,
        TypeSecurite.Lisses => TypePlinthe.Axees,
        TypeSecurite.Sans => TypePlinthe.Sans,
        _ => null,
    };
}
