using System.Security.Cryptography;
using System.Text.Json;
using MultiDEC.Core.Droits;
using Xunit;

namespace MultiDEC.Core.Tests;

/// <summary>Droits d'utilisation (Laurent, 27/11/2026) : code du poste, liste signée, tolérance hors ligne de 30 jours.</summary>
public class DroitsTests
{
    private static readonly RSA Cle = RSA.Create(2048);
    private static readonly string ClePublique = ClePubliqueXml(Cle);
    private static readonly DateTime Maintenant = new DateTime(2026, 12, 1, 9, 0, 0, DateTimeKind.Utc);
    private static readonly string Code = CodePoste.Calculer(new[] { "4c4c4544-0042-3510-8051-b4c04f4e4d32", "1F2E3D4C-0000-1111-2222-333344445555" });

    private static string ClePubliqueXml(RSA rsa)
    {
        var p = rsa.ExportParameters(false);
        return $"<RSAKeyValue><Modulus>{Convert.ToBase64String(p.Modulus!)}</Modulus><Exponent>{Convert.ToBase64String(p.Exponent!)}</Exponent></RSAKeyValue>";
    }

    private static string Liste(DateTime emise, params (string code, string? expiration)[] postes)
    {
        var contenu = JsonSerializer.Serialize(new
        {
            format = 1,
            emise = emise.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            postes = postes.Select(p => new { code = p.code, expiration = p.expiration }).ToArray(),
        });
        return ListePostes.Signer(contenu, Cle);
    }

    [Fact]
    public void Code_du_poste_stable_bien_forme_et_controle()
    {
        Assert.Matches("^[A-Z2-9]{4}-[A-Z2-9]{4}-[A-Z2-9]{4}-[A-Z2-9]{4}$", Code);
        Assert.Equal(Code, CodePoste.Calculer(new[] { " 4C4C4544-0042-3510-8051-B4C04F4E4D32 ", "1f2e3d4c-0000-1111-2222-333344445555" }));
        Assert.NotEqual(Code, CodePoste.Calculer(new[] { "4c4c4544-0042-3510-8051-b4c04f4e4d32", "autre carte mère" }));
        Assert.DoesNotContain('O', Code);
        Assert.Equal(Code, CodePoste.Normaliser(Code.Replace("-", string.Empty).ToLowerInvariant()));

        // Toute faute de frappe sur un caractère est détectée par la clé de contrôle.
        var brut = Code.Replace("-", string.Empty);
        for (int i = 0; i < brut.Length; i++)
        {
            foreach (char c in CodePoste.Alphabet.Where(c => c != brut[i]))
            {
                Assert.False(CodePoste.EstValide(brut.Substring(0, i) + c + brut.Substring(i + 1)));
            }
        }

        Assert.False(CodePoste.EstValide("ABCD-EFGH"));
        Assert.Throws<ArgumentException>(() => CodePoste.Calculer(new[] { "", null }));
    }

    [Fact]
    public void Liste_signee_lue_et_signature_controlee()
    {
        string enveloppe = Liste(Maintenant, (Code, "2027-06-30"), ("AAAA-BBBB-CCCC-DDDD", null));
        var liste = ListePostes.LireSignee(enveloppe, ClePublique);
        Assert.Equal(Maintenant, liste.Emise);
        Assert.Equal(new DateTime(2027, 6, 30), liste.Trouver(Code.ToLowerInvariant())!.Expiration);

        // Contenu modifié : refusé.
        var e = JsonDocument.Parse(enveloppe).RootElement;
        string falsifie = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(e.GetProperty("contenu").GetString()!)).Replace("2027-06-30", "2099-12-31");
        string modifiee = JsonSerializer.Serialize(new { format = 1, contenu = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(falsifie)), signature = e.GetProperty("signature").GetString() });
        Assert.Throws<CryptographicException>(() => ListePostes.LireSignee(modifiee, ClePublique));

        // Liste signée avec une autre clé (fausse liste servie par un tiers) : refusée.
        using var autre = RSA.Create(2048);
        Assert.Throws<CryptographicException>(() => ListePostes.LireSignee(ListePostes.Signer("{\"format\":1,\"emise\":\"2026-12-01T00:00:00Z\",\"postes\":[]}", autre), ClePublique));
        Assert.Throws<InvalidDataException>(() => ListePostes.LireSignee("<html>portail wifi</html>", ClePublique));
    }

    [Fact]
    public void En_ligne_poste_autorise_puis_memorise()
    {
        var r = ControleDroits.Evaluer(Code, ClePublique, Liste(Maintenant.AddDays(-2), (Code, null)), Maintenant, null, Maintenant);
        Assert.True(r.Autorise);
        Assert.Equal(MotifDroits.AutoriseEnLigne, r.Motif);
        Assert.Equal(Maintenant, r.NouvelEtat!.DerniereVerification);
        Assert.Null(r.Avertissement);
    }

    [Fact]
    public void En_ligne_poste_absent_ou_expire_refuse()
    {
        var absent = ControleDroits.Evaluer(Code, ClePublique, Liste(Maintenant, ("AAAA-BBBB-CCCC-DDDD", null)), null, null, Maintenant);
        Assert.False(absent.Autorise);
        Assert.Equal(MotifDroits.NonAutorise, absent.Motif);

        var expire = ControleDroits.Evaluer(Code, ClePublique, Liste(Maintenant, (Code, "2026-11-30")), null, null, Maintenant);
        Assert.False(expire.Autorise);
        Assert.Equal(MotifDroits.Expire, expire.Motif);

        // Dernier jour inclus, avec préavis.
        var dernierJour = ControleDroits.Evaluer(Code, ClePublique, Liste(Maintenant, (Code, "2026-12-01")), null, null, Maintenant);
        Assert.True(dernierJour.Autorise);
        Assert.NotNull(dernierJour.Avertissement);
    }

    [Fact]
    public void Retrait_du_poste_supprime_aussi_le_droit_hors_ligne()
    {
        var ok = ControleDroits.Evaluer(Code, ClePublique, Liste(Maintenant.AddDays(-1), (Code, null)), null, null, Maintenant);
        var retire = ControleDroits.Evaluer(Code, ClePublique, Liste(Maintenant, ("AAAA-BBBB-CCCC-DDDD", null)), null, ok.NouvelEtat, Maintenant.AddDays(1));
        Assert.False(retire.Autorise);
        var ensuiteHorsLigne = ControleDroits.Evaluer(Code, ClePublique, null, null, retire.NouvelEtat, Maintenant.AddDays(2));
        Assert.False(ensuiteHorsLigne.Autorise);
        Assert.Equal(MotifDroits.NonAutorise, ensuiteHorsLigne.Motif);
    }

    [Fact]
    public void Hors_ligne_trente_jours_de_tolerance()
    {
        var etat = ControleDroits.Evaluer(Code, ClePublique, Liste(Maintenant, (Code, null)), null, null, Maintenant).NouvelEtat;

        var jour10 = ControleDroits.Evaluer(Code, ClePublique, null, null, etat, Maintenant.AddDays(10));
        Assert.True(jour10.Autorise);
        Assert.Equal(MotifDroits.AutoriseHorsLigne, jour10.Motif);
        Assert.Equal(20, jour10.JoursHorsLigneRestants);
        Assert.Null(jour10.Avertissement);

        var jour25 = ControleDroits.Evaluer(Code, ClePublique, null, null, jour10.NouvelEtat, Maintenant.AddDays(25));
        Assert.True(jour25.Autorise);
        Assert.NotNull(jour25.Avertissement);

        var jour31 = ControleDroits.Evaluer(Code, ClePublique, null, null, jour25.NouvelEtat, Maintenant.AddDays(31));
        Assert.False(jour31.Autorise);
        Assert.Equal(MotifDroits.VerificationNecessaire, jour31.Motif);

        // Première utilisation sans connexion : refus.
        Assert.Equal(MotifDroits.VerificationNecessaire, ControleDroits.Evaluer(Code, ClePublique, null, null, null, Maintenant).Motif);
    }

    [Fact]
    public void Horloge_reculee_refusee()
    {
        var etat = ControleDroits.Evaluer(Code, ClePublique, Liste(Maintenant, (Code, null)), null, null, Maintenant).NouvelEtat;
        var jour20 = ControleDroits.Evaluer(Code, ClePublique, null, null, etat, Maintenant.AddDays(20)).NouvelEtat;

        // Horloge ramenée au jour 5 pour prolonger le délai hors ligne.
        var recule = ControleDroits.Evaluer(Code, ClePublique, null, null, jour20, Maintenant.AddDays(5));
        Assert.False(recule.Autorise);
        Assert.Equal(MotifDroits.HorlogeIncoherente, recule.Motif);

        // Horloge reculée pour échapper à l'expiration en ligne : l'heure du serveur fait foi.
        var expire = ControleDroits.Evaluer(Code, ClePublique, Liste(Maintenant, (Code, "2026-12-10")), Maintenant.AddDays(15), null, Maintenant.AddDays(-30));
        Assert.Equal(MotifDroits.Expire, expire.Motif);
    }

    [Fact]
    public void Cache_falsifie_ou_liste_rejouee_ignores()
    {
        var etat = ControleDroits.Evaluer(Code, ClePublique, Liste(Maintenant, ("AAAA-BBBB-CCCC-DDDD", null)), null, null, Maintenant).NouvelEtat!;

        // Ancienne liste (où le poste figurait) rejouée : ignorée, le poste reste refusé.
        var rejouee = ControleDroits.Evaluer(Code, ClePublique, Liste(Maintenant.AddDays(-60), (Code, null)), null, etat, Maintenant.AddDays(1));
        Assert.False(rejouee.Autorise);

        // État local réécrit avec une liste non signée par RETOTUB : ignoré.
        using var autre = RSA.Create(2048);
        var fauxEtat = new EtatDroits
        {
            ListeSignee = ListePostes.Signer($"{{\"format\":1,\"emise\":\"2026-12-01T00:00:00Z\",\"postes\":[{{\"code\":\"{Code}\"}}]}}", autre),
            DerniereVerification = Maintenant,
        };
        Assert.False(ControleDroits.Evaluer(Code, ClePublique, null, null, fauxEtat, Maintenant).Autorise);
        Assert.Equal(fauxEtat.DerniereVerification, EtatDroits.Lire(fauxEtat.Ecrire()).DerniereVerification);
    }

    [Fact]
    public void Sans_cle_publique_controle_inactif()
    {
        var r = ControleDroits.Evaluer(Code, null, null, null, null, Maintenant);
        Assert.True(r.Autorise);
        Assert.Equal(MotifDroits.ControleInactif, r.Motif);
    }

    [Theory]
    [InlineData("https://www.retotub.com/multidec/version.json", "https://www.retotub.com/multidec/postes-autorises.json")]
    [InlineData("https://www.retotub.com/multidec/version.json?x=1", "https://www.retotub.com/multidec/postes-autorises.json")]
    [InlineData("https://www.retotub.com/multidec/", "https://www.retotub.com/multidec/postes-autorises.json")]
    [InlineData(@"C:\publication\version.json", @"C:\publication\postes-autorises.json")]
    [InlineData(@"\\serveur\partage", @"\\serveur\partage\postes-autorises.json")]
    [InlineData(null, null)]
    public void Adresse_de_la_liste_a_cote_de_la_fiche_des_mises_a_jour(string? fiche, string? attendue)
    {
        Assert.Equal(attendue, ControleDroits.AdresseListe(fiche));
    }
}
