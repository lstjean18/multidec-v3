using System;
using System.IO;
using System.Threading.Tasks;
using MultiDEC.App;
using MultiDEC.Core.MiseAJour;
using Xunit;
using Xunit.Abstractions;

namespace MultiDEC.Rendering.Tests;

/// <summary>Mise à jour automatique (27/11/2026) : lecture de la fiche, téléchargement, empreinte, signature.</summary>
public class MiseAJourTests
{
    private readonly ITestOutputHelper _sortie;

    public MiseAJourTests(ITestOutputHelper sortie) => _sortie = sortie;

    [Fact]
    public async Task Mise_a_jour_depuis_un_dossier_avec_controle_de_l_empreinte()
    {
        string dossier = Path.Combine(Path.GetTempPath(), "multidec-maj-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dossier);
        try
        {
            var contenu = new byte[200_000];
            new Random(3).NextBytes(contenu);
            File.WriteAllBytes(Path.Combine(dossier, "MultiDEC-v3-Setup-9.0.0.exe"), contenu);
            var fiche = new FicheVersion
            {
                Version = "9.0.0",
                Libelle = "v9 test",
                Installateur = "MultiDEC-v3-Setup-9.0.0.exe",
                Sha256 = FicheVersion.Empreinte(new MemoryStream(contenu)),
            };
            string adresse = Path.Combine(dossier, FicheVersion.NomFichier);
            File.WriteAllText(adresse, "﻿" + fiche.Ecrire());

            var lue = await MiseAJourApplication.LireFicheAsync(adresse);
            Assert.True(lue.EstPlusRecenteQue(MiseAJourApplication.VersionInstallee));
            string telecharge = await MiseAJourApplication.TelechargerAsync(lue, adresse, null);
            Assert.Equal(contenu, File.ReadAllBytes(telecharge));
            _sortie.WriteLine("MAJ téléchargée : " + telecharge);

            lue.Sha256 = new string('0', 64);
            await Assert.ThrowsAsync<InvalidDataException>(() => MiseAJourApplication.TelechargerAsync(lue, adresse, null));
        }
        finally
        {
            Directory.Delete(dossier, true);
        }
    }

    [Fact]
    public void Signature_des_fichiers_de_l_application()
    {
        string exe = typeof(MiseAJourApplication).Assembly.Location;
        string vd = typeof(global::VectorDraw.Professional.vdObjects.vdDocument).Assembly.Location;
        foreach (var f in new[] { exe, vd })
        {
            _sortie.WriteLine($"SIGNATURE {Path.GetFileName(f)} : signataire « {SignatureAuthenticode.Signataire(f) ?? "aucun"} », valide {SignatureAuthenticode.Valide(f)}");
        }

        // Compilation de test : MultiDEC.exe n'est pas signé, la signature n'est donc pas exigée de l'installateur.
        Assert.Null(SignatureAuthenticode.Signataire(exe));
        Assert.False(SignatureAuthenticode.Valide(exe));
    }
}
