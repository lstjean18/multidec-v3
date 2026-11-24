using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using MultiDEC.Core.Facade;
using MultiDEC.Rendering.VectorDraw;
using VectorDraw.Professional.Components;
using VectorDraw.Professional.vdObjects;
using VectorDraw.Professional.vdFigures;
using VectorDraw.Professional.vdPrimaries;
using Xunit;
using Xunit.Abstractions;

namespace MultiDEC.Rendering.Tests;

/// <summary>
/// Bibliothèque DWG fournie par Laurent (Bloc3D.dwg, 23/11/2026) : VectorDraw la lit par vdxFcnv,
/// qui convertit les solides 3D. Le test décrit ce qui est chargé, dessine la façade de test
/// et écrit une image du rendu dans le journal (base64) pour contrôle visuel.
/// </summary>
public class BibliothequeDwgTests
{
    private readonly ITestOutputHelper _sortie;

    public BibliothequeDwgTests(ITestOutputHelper sortie) => _sortie = sortie;

    /// <summary>Bloc3D.dwg livré avec les tests, puis les DWG déposés dans actions-runner\bibliotheque (ou bibliothèque).</summary>
    private static List<string> Fichiers()
    {
        var liste = new List<string> { Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Donnees", "Bloc3D.dwg") };
        var ws = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        if (!string.IsNullOrEmpty(ws))
        {
            var runner = Path.GetFullPath(Path.Combine(ws, "..", "..", ".."));
            foreach (var d in Directory.GetDirectories(runner).Where(d => Path.GetFileName(d).StartsWith("biblioth", StringComparison.OrdinalIgnoreCase)))
            {
                liste.AddRange(Directory.GetFiles(d, "*.dwg"));
            }
        }

        return liste;
    }

    [Fact]
    public void Facades_avec_les_dwg()
    {
        foreach (var f in Fichiers())
        {
            _sortie.WriteLine("===== " + f + " (" + new FileInfo(f).Length + " octets)");
            try
            {
                Facade_avec_le_dwg(f);
            }
            catch (Exception ex)
            {
                _sortie.WriteLine("ERREUR : " + ex.GetBaseException());
            }
        }
    }

    private void Facade_avec_le_dwg(string dwg)
    {
        var fichier = Path.GetFileName(dwg);
        var doc = new vdDocumentComponent().Document;
        var rendu = new RenduVectorDraw(doc);
        rendu.ChargerBibliotheque(dwg);
        foreach (var l in rendu.Diagnostic)
        {
            _sortie.WriteLine(l);
        }

        var types = new Dictionary<string, int>();
        int blocs = 0;
        foreach (vdBlock b in doc.Blocks)
        {
            blocs++;
            foreach (vdFigure f in b.Entities)
            {
                var k = f.GetType().Name + "@" + f.Layer?.Name;
                types[k] = types.TryGetValue(k, out var c) ? c + 1 : 1;
            }
        }

        _sortie.WriteLine($"{blocs} blocs ; entités : {string.Join(", ", types.OrderByDescending(x => x.Value).Take(20).Select(x => x.Value + "×" + x.Key))}");
        _sortie.WriteLine("Blocs : " + string.Join(" ", doc.Blocks.Cast<vdBlock>().Select(b => b.Name).Where(n => !n.StartsWith("*")).Take(600)));

        foreach (var nom in new[] { "HMPLA250298CL6", "HMPOT20", "HMGCSE250", "HMSOCREG" })
        {
            var b = doc.Blocks.FindName(nom);
            _sortie.WriteLine(b == null ? $"{nom} : absent" : $"{nom} : {string.Join(", ", b.Entities.Cast<vdFigure>().GroupBy(f => f.GetType().Name + "@" + f.Layer?.Name).Select(g => g.Count() + "×" + g.Key))}");
        }

        var p = new ParametresFacade { LongueurTotaleSouhaitee = 10, LongueurMaillePreferee = 2.5, NombreAcces = 1, HauteurDernierPlancher = 6 };
        var projet = ProposeurStructure.NouveauProjet(p);
        foreach (var n in projet.Niveaux.Where(n => n.Type == TypePlancher.Complet && n.Hauteur > 0))
        {
            n.Console = "HMCON700E";
            projet.Mailles[0].DefinirConsole(n.Hauteur, true);
        }
        var rapport = rendu.Dessiner(GenerateurFacade.Generer(projet), TableOrientations.ParDefaut());
        _sortie.WriteLine($"{rapport.Inseres} insérés, manquants : {string.Join(", ", rapport.BlocsManquants)}");
        foreach (var g in rapport.Inserts.GroupBy(x => x.Element.Famille))
        {
            var bx = g.Select(x => x.Insert.BoundingBox).ToList();
            _sortie.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0,-20} {1,3}  X {2:0.00}..{3:0.00}  Y {4:0.00}..{5:0.00}  Z {6:0.00}..{7:0.00}",
                g.Key, g.Count(), bx.Min(b => b.Min.x), bx.Max(b => b.Max.x), bx.Min(b => b.Min.y), bx.Max(b => b.Max.y), bx.Min(b => b.Min.z), bx.Max(b => b.Max.z)));
        }

        foreach (var mode in new[] { ModeRendu.OmbreAretes, ModeRendu.Realiste })
        {
            rendu.Rendu(mode);
            rendu.Vue(Vues.Iso);
            Image(doc, Path.GetFileNameWithoutExtension(fichier) + "-" + mode);
        }

        BoitesDesBlocs(doc);
        StructureEnL(rendu, doc, Path.GetFileNameWithoutExtension(fichier));
        SousMailles(rendu, doc, Path.GetFileNameWithoutExtension(fichier));
        PoutreEtPignon(rendu, doc, Path.GetFileNameWithoutExtension(fichier));
        Escaliers(rendu, doc, Path.GetFileNameWithoutExtension(fichier));
        VolumiqueEtHauteurs(rendu, doc, Path.GetFileNameWithoutExtension(fichier));
        AngleEnL(rendu, doc, Path.GetFileNameWithoutExtension(fichier));
    }

    /// <summary>
    /// Petit L (S1 5 m, S2 3 m, 2 niveaux) vu de près sous plusieurs angles : sens des cadres d'about au carré d'angle,
    /// poteaux de chaque côté des garde-corps, stabilisation et portillon.
    /// </summary>
    private void AngleEnL(RenduVectorDraw rendu, vdDocument doc, string nom)
    {
        var projet = ProjetStructure.Nouveau(face: new ParametresFacade { LongueurTotaleSouhaitee = 5, LongueurMaillePreferee = 2.5, HauteurDernierPlancher = 4, NombreAcces = 0 });
        projet.Forme = FormeStructure.EnL;
        projet.AjusterFaces();
        projet.Faces[1].Parametres.LongueurTotaleSouhaitee = 3;
        projet.ProposerFace(1);
        var s = GenerateurStructure.Generer(projet);
        rendu.Dessiner(s, TableOrientations.ParDefaut());
        foreach (var e in s.Elements.Where(e => e.Axe == Axe.Y && (e.Famille == Famille.GardeCorps || e.Famille == Famille.Plinthe)))
        {
            _sortie.WriteLine(string.Format(CultureInfo.InvariantCulture, "AN {0} {1,-16} ({2:0.00}; {3:0.00}; {4:0.00}) inv {5} angle {6}", e.NomFace, e.Reference, e.Origine.X, e.Origine.Y, e.Origine.Z, e.Inverse, e.AngleFace));
        }

        rendu.Rendu(ModeRendu.OmbreAretes);
        foreach (var v in new[] { "VISE", "VISW", "VINE", "VINW" })
        {
            doc.CommandAction.View3D(v);
            doc.Model.ZoomExtents();
            doc.Redraw(true);
            Image(doc, nom + "-AN-" + v);
        }
    }

    /// <summary>
    /// Volumique 3 modules (M2 plus long que M1 de deux mailles, M3 de 2,00 m de large) ;
    /// façade droite avec une maille arrêtée à 4 m et une montée à 8 m (colonne « Ht max »).
    /// </summary>
    private void VolumiqueEtHauteurs(RenduVectorDraw rendu, vdDocument doc, string nom)
    {
        var projet = ProjetStructure.Nouveau(face: new ParametresFacade { LongueurTotaleSouhaitee = 10, HauteurDernierPlancher = 6, NombreAcces = 1 });
        projet.Forme = FormeStructure.Volumique;
        projet.NombreModules = 3;
        projet.AjusterFaces();
        projet.Faces[1].Mailles.Add(new MailleDefinition(2.5));
        projet.Faces[1].Mailles.Add(new MailleDefinition(2.5));
        projet.Faces[2].Parametres.LargeurMaille = 2.0;
        var s = GenerateurStructure.Generer(projet);
        var rapport = rendu.Dessiner(s, TableOrientations.ParDefaut());
        _sortie.WriteLine($"VO : {rapport.Inseres} insérés, alertes : {string.Join(" | ", s.Alertes)}");
        rendu.Rendu(ModeRendu.OmbreAretes);
        rendu.Vue(Vues.Iso);
        Image(doc, nom + "-VO-iso");
        rendu.Vue(Vues.Dessus);
        Image(doc, nom + "-VO-dessus");

        var droite = ProjetStructure.Nouveau(face: new ParametresFacade { LongueurTotaleSouhaitee = 15, HauteurDernierPlancher = 6, NombreAcces = 0 });
        droite.S1.Mailles[1].EcartHauteur = -2;
        droite.S1.Mailles[4].EcartHauteur = 2;
        var h = GenerateurStructure.Generer(droite);
        rapport = rendu.Dessiner(h, TableOrientations.ParDefaut());
        _sortie.WriteLine($"HT : mailles {string.Join(" ", droite.S1.Mailles)} ; {rapport.Inseres} insérés, alertes : {string.Join(" | ", h.Alertes)}");
        rendu.Rendu(ModeRendu.OmbreAretes);
        rendu.Vue(Vues.Iso);
        Image(doc, nom + "-HT-iso");
        rendu.Vue(Vues.Facade);
        Image(doc, nom + "-HT-facade");
    }

    /// <summary>Façade de 15 m, dernier plancher 6 m : escalier 1,50 m (maille 2) et demi-escalier 1,00 m (maille 5).</summary>
    private void Escaliers(RenduVectorDraw rendu, vdDocument doc, string nom)
    {
        var projet = ProjetStructure.Nouveau(face: new ParametresFacade { LongueurTotaleSouhaitee = 15, HauteurDernierPlancher = 6, NombreAcces = 0 });
        var face = projet.S1;
        face.Mailles[1].Type = TypeMaille.Escalier;
        face.Mailles[4].Type = TypeMaille.DemiEscalier100;
        var s = GenerateurStructure.Generer(projet);
        var rapport = rendu.Dessiner(s, TableOrientations.ParDefaut());
        _sortie.WriteLine($"ES : mailles {string.Join(" ", face.Mailles)} ; {rapport.Inseres} insérés, alertes : {string.Join(" | ", s.Alertes)}");
        foreach (var x in rapport.Inserts.Where(x => x.Element.Famille == Famille.Escalier || x.Element.Reference.StartsWith("HMMONTGC", StringComparison.Ordinal)))
        {
            var b = x.Insert.BoundingBox;
            _sortie.WriteLine(string.Format(CultureInfo.InvariantCulture, "ES {0,-18} o=({1:0.00},{2:0.00},{3:0.00}) inv={4}  X {5:0.00}..{6:0.00}  Y {7:0.00}..{8:0.00}  Z {9:0.00}..{10:0.00}",
                x.Element.Reference, x.Element.Origine.X, x.Element.Origine.Y, x.Element.Origine.Z, x.Element.Inverse, b.Min.x, b.Max.x, b.Min.y, b.Max.y, b.Min.z, b.Max.z));
        }

        rendu.Rendu(ModeRendu.OmbreAretes);
        rendu.Vue(Vues.Iso);
        Image(doc, nom + "-ES-iso");
        rendu.Vue(Vues.Facade);
        Image(doc, nom + "-ES-facade");
        rendu.Vue(Vues.Dessus);
        Image(doc, nom + "-ES-dessus");
        rendu.Vue(Vues.CoteGauche);
        Image(doc, nom + "-ES-cote");
    }

    /// <summary>Façade de 15 m : poutre de franchissement à 4 m sur deux mailles, pignon de +4 m sur la maille d'accès.</summary>
    private void PoutreEtPignon(RenduVectorDraw rendu, vdDocument doc, string nom)
    {
        var projet = ProjetStructure.Nouveau(face: new ParametresFacade { LongueurTotaleSouhaitee = 15, HauteurDernierPlancher = 6, NombreAcces = 1 });
        var face = projet.S1;
        int acces = face.Mailles.FindIndex(m => m.Type == TypeMaille.Acces);
        face.Mailles[acces].EcartHauteur = 4;
        int i = acces >= 3 ? 1 : acces + 2; // deux mailles de travail hors des extrémités
        face.Mailles[i].Poutre = 4;
        var s = GenerateurStructure.Generer(projet);
        var rapport = rendu.Dessiner(s, TableOrientations.ParDefaut());
        _sortie.WriteLine($"PF : mailles {string.Join(" ", face.Mailles)} ; poutre sur {i + 1}, {rapport.Inseres} insérés, alertes : {string.Join(" | ", s.Alertes)}");
        foreach (var x in rapport.Inserts.Where(x => x.Element.Famille == Famille.Poutre))
        {
            var b = x.Insert.BoundingBox;
            _sortie.WriteLine(string.Format(CultureInfo.InvariantCulture, "PF {0,-12} o=({1:0.00},{2:0.00},{3:0.00}) r={4:0.0}  X {5:0.00}..{6:0.00}  Y {7:0.00}..{8:0.00}  Z {9:0.00}..{10:0.00}",
                x.Element.Reference, x.Element.Origine.X, x.Element.Origine.Y, x.Element.Origine.Z, x.Element.Rotation, b.Min.x, b.Max.x, b.Min.y, b.Max.y, b.Min.z, b.Max.z));
        }

        rendu.Rendu(ModeRendu.OmbreAretes);
        rendu.Vue(Vues.Iso);
        Image(doc, nom + "-PF-iso");
        rendu.Vue(Vues.Facade);
        Image(doc, nom + "-PF-facade");
        rendu.Vue(Vues.Dessus);
        Image(doc, nom + "-PF-dessus");
    }

    /// <summary>
    /// Façade droite avec sous-mailles rapportées : stabilisation (maille 1), accès rapporté (maille 2),
    /// module d'appro (maille 4), recette de 1,50 m au niveau 4 (maille 5).
    /// </summary>
    private void SousMailles(RenduVectorDraw rendu, vdDocument doc, string nom)
    {
        var projet = ProjetStructure.Nouveau(face: new ParametresFacade { LongueurTotaleSouhaitee = 15, HauteurDernierPlancher = 6, NombreAcces = 0 });
        var face = projet.S1;
        face.Mailles[0].Stabilisation = 1.5;
        face.Mailles[1].Type = TypeMaille.AccesRapporte;
        face.Mailles[3].Type = TypeMaille.Appro;
        face.Mailles[4].Type = TypeMaille.Recette;
        face.Mailles[4].ProfondeurRecette = 1.5;
        face.Niveaux.First(n => n.Hauteur == 4).Recette = true;
        var s = GenerateurStructure.Generer(projet);
        var rapport = rendu.Dessiner(s, TableOrientations.ParDefaut());
        _sortie.WriteLine($"SM : {rapport.Inseres} insérés, alertes : {string.Join(" | ", s.Alertes)}");
        foreach (var g in rapport.Inserts.Where(x => x.Element.Reference.StartsWith("HMGCAPPRO", StringComparison.Ordinal)).GroupBy(x => x.Element.Reference))
        {
            foreach (var x in g)
            {
                var b = x.Insert.BoundingBox;
                _sortie.WriteLine(string.Format(CultureInfo.InvariantCulture, "SM {0,-16} o=({1:0.00},{2:0.00},{3:0.00})  X {4:0.00}..{5:0.00}  Y {6:0.00}..{7:0.00}  Z {8:0.00}..{9:0.00}",
                    g.Key, x.Element.Origine.X, x.Element.Origine.Y, x.Element.Origine.Z, b.Min.x, b.Max.x, b.Min.y, b.Max.y, b.Min.z, b.Max.z));
            }
        }

        rendu.Rendu(ModeRendu.OmbreAretes);
        rendu.Vue(Vues.Iso);
        Image(doc, nom + "-SM-iso");
        rendu.Vue(Vues.Dessus);
        Image(doc, nom + "-SM-dessus");
        rendu.Vue(Vues.CoteGauche);
        Image(doc, nom + "-SM-cote");
    }

    /// <summary>
    /// Boîte englobante de blocs à caler (socles, vérins, portillons, poutres…) insérés debout à l'origine,
    /// comme le rendu insère un élément vertical : sert à relever leurs points de base dans la bibliothèque.
    /// </summary>
    private void BoitesDesBlocs(vdDocument doc)
    {
        var table = TableOrientations.ParDefaut();
        var verticaux = new[] { "HMCALEBOIS", "HMSOCREG", "HMSOCREG800", "HMSOCROU", "HMSOCROU200-1200", "HMSOCROUFC", "HMVERORI38780", "HMVERORI38500B019", "HMEMB", "HMSUPSUNI", "HMCOLLEV", "VSZ10070", "VEF10", "OECGOUP8S", "HMGCAPPROSUP", "HMCOLF49", "HMPIQLIS-E", "HMMONTGCE" };
        var long_x = new[] { "HMPORTILLONPLIN070", "HMPORTILLONPLIN100", "HMPARGRA", "HMTOG200", "HMCROCHE", "HMECH100LIS", "HMPOUT500", "HMPTRL250", "HMLISR250", "HMLIS250", "HMTRAP250", "HMPLAA250", "HMTRA070", "HMGCAPPRO-C", "HMGCAPPROGC200", "HMPOUT500E", "HMTUB300", "HMESCALU250200", "HMESCALU300200", "HMESCALU175100", "HMESCALU25020088D", "HMESCALU17510088D", "HMGCESCALU200", "HMGCESCALU100", "HMGCESCALUEXTR", "HMESCHA2515LIM", "HMESCHADEPLIM", "HMPLALECHELLE" };
        foreach (var (refs, axe) in new[] { (verticaux, Axe.Z), (long_x, Axe.X) })
        {
            foreach (var nom in refs)
            {
                var b = doc.Blocks.FindName(nom);
                if (b == null)
                {
                    _sortie.WriteLine($"BOITE {nom} : absent");
                    continue;
                }

                var pose = PoseInsert.Depuis(table.Matrice(new ElementPlace(nom, Famille.Socle, new MultiDEC.Core.Geometrie.Point3(0, 0, 0), axe)));
                var ins = new vdInsert();
                ins.SetUnRegisterDocument(doc);
                ins.setDocumentDefaults();
                ins.Block = b;
                ins.InsertionPoint = new global::VectorDraw.Geometry.gPoint(0, 0, 0);
                ins.ExtrusionVector = new global::VectorDraw.Geometry.Vector(pose.Extrusion.X, pose.Extrusion.Y, pose.Extrusion.Z);
                ins.Rotation = pose.Rotation;
                ins.Update();
                var bx = ins.BoundingBox;
                _sortie.WriteLine(string.Format(CultureInfo.InvariantCulture, "BOITE {0,-20} axe {1}  X {2:0.000}..{3:0.000}  Y {4:0.000}..{5:0.000}  Z {6:0.000}..{7:0.000}",
                    nom, axe, bx.Min.x, bx.Max.x, bx.Min.y, bx.Max.y, bx.Min.z, bx.Max.z));
            }
        }
    }

    /// <summary>Structure en L (S1 de 10 m, S2 de 6 m) : image pour contrôler le carré d'angle et la rotation des blocs.</summary>
    private void StructureEnL(RenduVectorDraw rendu, vdDocument doc, string nom)
    {
        var projet = ProjetStructure.Nouveau(face: new ParametresFacade { LongueurTotaleSouhaitee = 10, HauteurDernierPlancher = 4 });
        projet.Forme = FormeStructure.EnL;
        projet.AjusterFaces();
        projet.Faces[1].Parametres.LongueurTotaleSouhaitee = 6;
        projet.ProposerFace(1);
        projet.Faces[1].Niveaux.First(n => n.Hauteur == 4).DiagoH = true;
        projet.S1.Niveaux.First(n => n.Hauteur == 2).PareGravois = true;
        projet.S1.Parametres.TrappesAcier = true;
        projet.S1.Niveaux.Add(new Niveau(1.0, TypePlancher.Complet)); // portillon d'about
        projet.Faces[1].Niveaux.Add(new Niveau(1.0, TypePlancher.Complet)); // premier plancher commun aux faces (exigé)
        projet.General.Socle = "HMSOCROU";
        var s = GenerateurStructure.Generer(projet);
        var rapport = rendu.Dessiner(s, TableOrientations.ParDefaut());
        _sortie.WriteLine($"L : {rapport.Inseres} insérés, alertes : {string.Join(" | ", s.Alertes)}");
        foreach (var g in rapport.Inserts.GroupBy(x => x.Element.NomFace + " " + x.Element.Famille))
        {
            var bx = g.Select(x => x.Insert.BoundingBox).ToList();
            _sortie.WriteLine(string.Format(CultureInfo.InvariantCulture, "L {0,-24} {1,3}  X {2:0.00}..{3:0.00}  Y {4:0.00}..{5:0.00}  Z {6:0.00}..{7:0.00}",
                g.Key, g.Count(), bx.Min(b => b.Min.x), bx.Max(b => b.Max.x), bx.Min(b => b.Min.y), bx.Max(b => b.Max.y), bx.Min(b => b.Min.z), bx.Max(b => b.Max.z)));
        }

        rendu.Rendu(ModeRendu.OmbreAretes);
        rendu.Vue(Vues.Iso);
        Image(doc, nom + "-L-iso");
        rendu.Vue(Vues.Dessus);
        Image(doc, nom + "-L-dessus");
        rendu.Vue(Vues.Facade);
        Image(doc, nom + "-L-facade");
        rendu.Vue(Vues.CoteGauche);
        Image(doc, nom + "-L-cote");
        rendu.MettreEnEvidence("S2");
        rendu.Vue(Vues.Iso);
        Image(doc, nom + "-L-S2");
    }

    private void Image(vdDocument doc, string nom)
    {
        try
        {
            using var bmp = new Bitmap(1400, 800);
            doc.Model.RenderToBitmap(bmp);
            using var petit = new MemoryStream();
            bmp.Save(petit, ImageFormat.Png);
            var b64 = Convert.ToBase64String(petit.ToArray());
            _sortie.WriteLine($"IMAGE-DEBUT {nom} {b64.Length}");
            for (int i = 0; i < b64.Length; i += 4000)
            {
                _sortie.WriteLine("IMG " + b64.Substring(i, Math.Min(4000, b64.Length - i)));
            }

            _sortie.WriteLine("IMAGE-FIN " + nom);
        }
        catch (Exception ex)
        {
            _sortie.WriteLine($"Image {nom} : {ex.GetBaseException().GetType().Name} : {ex.GetBaseException().Message}");
        }
    }
}
