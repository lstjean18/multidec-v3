using System.Security.Cryptography;
using MultiDEC.Core.Droits;
using Xunit;

namespace MultiDEC.Core.Tests;

/// <summary>MultiDEC Admin (Laurent, 27/11/2026) : registre des licences, clé de signature protégée, liste publiée.</summary>
public class AdministrationLicencesTests
{
    private static readonly DateTime Maintenant = new DateTime(2026, 12, 1, 9, 0, 0, DateTimeKind.Utc);
    private static readonly string CodeA = CodePoste.Calculer(new[] { "poste A", "uuid A" });
    private static readonly string CodeB = CodePoste.Calculer(new[] { "poste B", "uuid B" });
    private static readonly string CodeC = CodePoste.Calculer(new[] { "poste C", "uuid C" });

    [Fact]
    public void Registre_enregistre_normalise_et_relit()
    {
        var r = new RegistreLicences();
        r.Enregistrer(new Licence { Code = CodeA.Replace("-", "").ToLowerInvariant(), Demandeur = " Jean Dupont ", Entreprise = "Échafaudages Dupont", Email = "jean@exemple.fr" }, Maintenant);
        Assert.Equal(CodeA, r.Licences.Single().Code);
        Assert.Equal("Jean Dupont", r.Licences.Single().Demandeur);

        // Même code : mise à jour, date de création conservée.
        r.Enregistrer(new Licence { Code = CodeA, Demandeur = "Jean Dupont", Expiration = new DateTime(2027, 12, 31) }, Maintenant.AddDays(3));
        var l = Assert.Single(r.Licences);
        Assert.Equal(Maintenant, l.Creation);
        Assert.Equal(Maintenant.AddDays(3), l.Modification);

        Assert.Throws<ArgumentException>(() => r.Enregistrer(new Licence { Code = "ABCD-EFGH-JKLM-NPQR" }, Maintenant));

        var relu = RegistreLicences.Lire(r.Ecrire());
        Assert.Equal(new DateTime(2027, 12, 31), relu.Trouver(CodeA)!.Expiration);
        Assert.True(relu.APublier);
        relu.DernierePublication = Maintenant.AddDays(4);
        Assert.False(relu.APublier);
        Assert.True(relu.Supprimer(CodeA, Maintenant.AddDays(5)));
        Assert.Empty(relu.Licences);
        Assert.True(relu.APublier);
    }

    [Fact]
    public void Liste_publiee_sans_coordonnees_ni_licences_suspendues_ou_expirees()
    {
        var r = new RegistreLicences();
        r.Enregistrer(new Licence { Code = CodeA, Demandeur = "Jean Dupont", Entreprise = "Dupont SA", Email = "jean@exemple.fr", Expiration = new DateTime(2027, 6, 30) }, Maintenant);
        r.Enregistrer(new Licence { Code = CodeB, Demandeur = "Suspendu", Suspendue = true }, Maintenant);
        r.Enregistrer(new Licence { Code = CodeC, Demandeur = "Expiré", Expiration = new DateTime(2026, 11, 1) }, Maintenant);

        string contenu = r.ContenuPublic(Maintenant);
        Assert.DoesNotContain("Dupont", contenu);
        Assert.DoesNotContain("@", contenu);

        using var cle = RSA.Create(2048);
        string cleXml = CoffreCle.ClePubliqueXml(cle);
        var liste = ListePostes.LireSignee(ListePostes.Signer(contenu, cle), cleXml);
        Assert.Equal(Maintenant, liste.Emise);
        var poste = Assert.Single(liste.Postes);
        Assert.Equal(CodeA, poste.Code);
        Assert.Equal(new DateTime(2027, 6, 30), poste.Expiration);
        Assert.True(ControleDroits.Evaluer(CodeA, cleXml, ListePostes.Signer(contenu, cle), null, null, Maintenant).Autorise);
        Assert.False(ControleDroits.Evaluer(CodeB, cleXml, ListePostes.Signer(contenu, cle), null, null, Maintenant).Autorise);
    }

    [Fact]
    public void Cle_de_signature_protegee_par_mot_de_passe()
    {
        using var rsa = CoffreCle.Generer();
        Assert.Equal(CoffreCle.TailleCle, rsa.KeySize);
        string coffre = CoffreCle.Chiffrer(rsa, "mot de passe solide");
        Assert.DoesNotContain(Convert.ToBase64String(rsa.ExportParameters(true).D!), coffre);
        Assert.Equal(CoffreCle.ClePubliqueXml(rsa), CoffreCle.ClePublique(coffre));

        using var ouverte = CoffreCle.Ouvrir(coffre, "mot de passe solide");
        string signee = ListePostes.Signer("{\"format\":1,\"emise\":\"2026-12-01T00:00:00Z\",\"postes\":[]}", ouverte);
        Assert.Empty(ListePostes.LireSignee(signee, CoffreCle.ClePublique(coffre)).Postes);

        Assert.Throws<UnauthorizedAccessException>(() => CoffreCle.Ouvrir(coffre, "mauvais mot de passe"));
        Assert.Throws<ArgumentException>(() => CoffreCle.Chiffrer(rsa, "court"));
        Assert.True(CoffreCle.EstSuffisante(CoffreCle.ClePublique(coffre)));

        // Clé trop courte (1 024 bits, comme RSACryptoServiceProvider sous .NET Framework) : refusée.
        Assert.Throws<CryptographicException>(() => CoffreCle.Generer(() => RSA.Create(1024)));
        using var courte = RSA.Create(1024);
        Assert.False(CoffreCle.EstSuffisante(CoffreCle.ClePubliqueXml(courte)));
        Assert.Equal(1024, CoffreCle.TailleBits(CoffreCle.ClePubliqueXml(courte)));
        Assert.Throws<ArgumentException>(() => CoffreCle.Chiffrer(courte, "mot de passe solide"));
        using var fabrique = CoffreCle.Generer(() => RSA.Create(CoffreCle.TailleCle));
        Assert.Equal(CoffreCle.TailleCle, fabrique.KeySize);
        Assert.Equal(CoffreCle.Empreinte(CoffreCle.ClePubliqueXml(rsa)), CoffreCle.Empreinte(CoffreCle.ClePublique(coffre)));
    }

    [Fact]
    public void Code_extrait_d_un_e_mail_de_demande()
    {
        string courriel = $"Bonjour,\r\nMerci d'autoriser ce poste.\r\nCode du poste : {CodeA}\r\nNom de l'ordinateur : PC-BUREAU";
        Assert.Equal(CodeA, RegistreLicences.ExtraireCode(courriel));
        Assert.Equal(CodeA, RegistreLicences.ExtraireCode(CodeA.Replace("-", " ")));
        Assert.Null(RegistreLicences.ExtraireCode("Code : ABCD-EFGH-JKLM-NPQR"));
    }
}
