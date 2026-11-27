using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MultiDEC.Core.MiseEnPlan;
using VectorDraw.Geometry;
using VectorDraw.Professional.vdFigures;
using VectorDraw.Professional.vdObjects;
using VectorDraw.Professional.vdPrimaries;

namespace MultiDEC.Rendering.VectorDraw;

/// <summary>
/// Mise en plan VectorDraw (Laurent, 27/11/2026) : une présentation A3 par feuille, une fenêtre de vue par vue (matrice
/// de vue imposée par le cœur, échelle exacte), l'habillage (cadre, cartouche, cotes, repères) tracé en espace papier,
/// puis un PDF de toutes les feuilles. Calage vérifié sur le serveur (sonde MiseEnPlanSondeTests du 27/11) : la fenêtre
/// montre le modèle avec les lignes [U; V; W] de la matrice, ViewCenter = centre de l'emprise (U, V), ViewSize = hauteur
/// de l'emprise en mètres ; la présentation doit garder une vue de dessus (matrice identité).
/// </summary>
public sealed class MiseEnPlanVectorDraw
{
    /// <summary>Préfixe des présentations créées (« Plan 1 », « Plan 2 »…), remplacées à chaque mise en plan.</summary>
    public const string PrefixePresentation = "Plan ";

    private const string StyleTexte = "MDC-Plan";

    private readonly vdDocument _doc;

    public MiseEnPlanVectorDraw(vdDocument document)
    {
        _doc = document;
    }

    /// <summary>Résolution des vues ombrées dans le PDF (points par pouce).</summary>
    public int Resolution { get; set; } = 300;

    /// <summary>Mode de rendu des vues (ombré + arêtes, comme l'écran).</summary>
    public global::VectorDraw.Render.vdRender.Mode ModeVues { get; set; } = global::VectorDraw.Render.vdRender.Mode.ShadeOn;

    /// <summary>Dossier des logos intégrés (RETOTUB, MultiRETO, MultiSYSTEM).</summary>
    public string DossierLogos { get; set; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logos");

    public List<string> Diagnostic { get; } = new List<string>();

    /// <summary>Crée les présentations des feuilles (les anciennes « Plan n » sont supprimées).</summary>
    public List<vdLayout> Composer(IReadOnlyList<FeuilleDessinee> feuilles)
    {
        Supprimer();
        var style = Style();
        var pages = new List<vdLayout>();
        foreach (var f in feuilles)
        {
            var lay = new vdLayout(_doc, PrefixePresentation + f.Page.Numero);
            lay.setDocumentDefaults();
            _doc.LayOuts.AddItem(lay);
            lay.World2UserMatrix = new Matrix();
            lay.World2ViewMatrix = new Matrix();
            lay.BkColorEx = System.Drawing.Color.White;
            lay.VisibleOnForms = false;
            Papier(lay, f.Format);

            foreach (var vue in f.Page.Vues)
            {
                Fenetre(lay, vue);
            }

            foreach (var p in f.Primitives)
            {
                try
                {
                    Tracer(lay, p, style);
                }
                catch (Exception ex)
                {
                    Diagnostic.Add($"{lay.Name} : {p.GetType().Name} non tracé ({ex.GetBaseException().Message})");
                }
            }

            lay.ZoomExtents();
            lay.Update();
            pages.Add(lay);
        }

        _doc.ActiveLayOut = _doc.Model;
        return pages;
    }

    /// <summary>Supprime les présentations d'une mise en plan précédente.</summary>
    public void Supprimer()
    {
        var anciennes = _doc.LayOuts.Cast<vdLayout>().Where(l => l.Name.StartsWith(PrefixePresentation, StringComparison.Ordinal)).ToList();
        var retirer = _doc.LayOuts.GetType().GetMethods().FirstOrDefault(m => m.Name == "RemoveItem" && m.GetParameters().Length == 1);
        foreach (var l in anciennes)
        {
            l.Entities.RemoveAll();
            if (retirer != null)
            {
                retirer.Invoke(_doc.LayOuts, new object[] { l });
            }
            else
            {
                l.Name = "MDC-ancien-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            }
        }
    }

    /// <summary>
    /// Feuilles sans vue 3D (liste du matériel, 27/11/2026) : PDF écrit avec un document VectorDraw à part, sans toucher au
    /// dessin de la structure. Renvoie le diagnostic (journal).
    /// </summary>
    public static List<string> ExporterFeuillesPdf(IReadOnlyList<FeuilleDessinee> feuilles, string chemin, string? dossierLogos = null)
    {
        using var composant = new global::VectorDraw.Professional.Components.vdDocumentComponent();
        var mep = new MiseEnPlanVectorDraw(composant.Document);
        if (dossierLogos != null)
        {
            mep.DossierLogos = dossierLogos;
        }

        var pages = mep.Composer(feuilles);
        mep.ExporterPdf(pages, chemin);
        return mep.Diagnostic;
    }

    /// <summary>Écrit le PDF de toutes les feuilles (une page par présentation, au format de sa feuille).</summary>
    public void ExporterPdf(IReadOnlyList<vdLayout> pages, string chemin)
    {
        if (File.Exists(chemin))
        {
            File.Delete(chemin);
        }

        foreach (var lay in pages)
        {
            lay.Printer.Resolution = Resolution;
            lay.Printer.PrintScale.Numerator = 1;
            lay.Printer.PrintScale.Denumerator = 1;
        }

        // Toutes les feuilles dans un seul PDF (vdDocument.ExportMultiPagePDF(vdPrintArray, fichier)).
        var methode = typeof(vdDocument).GetMethod("ExportMultiPagePDF");
        var typeTableau = methode.GetParameters()[0].ParameterType;
        object tableau = Activator.CreateInstance(typeTableau);
        var ajouter = typeTableau.GetMethods().FirstOrDefault(m => (m.Name == "AddItem" || m.Name == "Add") && m.GetParameters().Length == 1
                                                                   && m.GetParameters()[0].ParameterType.IsAssignableFrom(typeof(vdPrint)));
        if (ajouter == null)
        {
            throw new MissingMethodException(typeTableau.FullName + " : pas de méthode d'ajout. Méthodes : "
                + string.Join(", ", typeTableau.GetMethods().Select(m => m.Name + "(" + string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name)) + ")").Distinct()));
        }

        foreach (var lay in pages)
        {
            ajouter.Invoke(tableau, new object[] { lay.Printer });
        }

        var resultat = methode.Invoke(_doc, new[] { tableau, chemin });
        Diagnostic.Add($"ExportMultiPagePDF : {resultat ?? "(void)"}, {pages.Count} pages, {(File.Exists(chemin) ? new FileInfo(chemin).Length + " octets" : "pas de fichier")}");
        if (!File.Exists(chemin))
        {
            throw new IOException("Le PDF de la mise en plan n'a pas été créé : " + chemin);
        }
    }

    // ------------------------------------------------------------------ présentation

    private void Papier(vdLayout lay, FormatFeuille format)
    {
        var imprimante = lay.Printer;
        var papier = imprimante.GetPapers().Cast<object>().Select(o => o.ToString()).FirstOrDefault(p => p == format.Nom);
        if (papier != null)
        {
            imprimante.SelectPaper(papier);
        }
        else
        {
            Diagnostic.Add("Papier " + format.Nom + " absent de la liste de l'imprimante");
        }

        imprimante.LandScape = format.Largeur > format.Hauteur;
        imprimante.PrintWindow = new Box(new gPoint(0, 0), new gPoint(format.Largeur, format.Hauteur));
    }

    private void Fenetre(vdLayout lay, VueDePlan vue)
    {
        var p = vue.Projection;
        if (Math.Abs(Math.Abs(p.Vers.Z) - 1) < 1e-9)
        {
            // Vue de dessus : avec une matrice identité la fenêtre restait vide (serveur, 27/11). Inclinaison de 1e-4 rad,
            // invisible sur la feuille (moins d'un centième de mm).
            p = new Projection(new Vecteur3(0, -1e-4, 1));
        }

        var m = new Matrix();
        m.A00 = p.U.X; m.A01 = p.U.Y; m.A02 = p.U.Z;
        m.A10 = p.V.X; m.A11 = p.V.Y; m.A12 = p.V.Z;
        m.A20 = p.Vers.X; m.A21 = p.Vers.Y; m.A22 = p.Vers.Z;

        var vp = new vdViewport(_doc);
        vp.setDocumentDefaults();
        vp.Width = vue.Cadre.Largeur;
        vp.Height = vue.Cadre.Hauteur;
        vp.Center = new gPoint(vue.Cadre.Centre.U, vue.Cadre.Centre.V);
        vp.World2ViewMatrix = m;
        vp.ViewCenter = new gPoint(vue.Emprise.Centre.U, vue.Emprise.Centre.V, 0);
        vp.ViewSize = vue.Emprise.Hauteur;
        vp.RenderMode = ModeVues;
        vp.PenColor = new vdColor(System.Drawing.Color.Transparent);

        // Élévations, profils et détails : seulement la (les) face(s) de la vue.
        if (vue.Faces != null)
        {
            var geles = _doc.Layers.Cast<vdLayer>()
                .Where(c => c.Name.StartsWith(RenduVectorDraw.PrefixeCalqueFace, StringComparison.Ordinal)
                            && !vue.Faces.Contains(c.Name.Substring(RenduVectorDraw.PrefixeCalqueFace.Length)))
                .Select(c => c.Name).ToList();
            if (vue.Type == TypeVue.Elevation)
            {
                geles.Add(RenduVectorDraw.CalqueFacade);
            }

            if (geles.Count > 0)
            {
                // FrozenLayerList (liste de noms de calques gelés dans la fenêtre), construite par réflexion.
                var propriete = typeof(vdViewport).GetProperty("FrozenLayerList");
                object liste = Activator.CreateInstance(propriete.PropertyType);
                var ajouter = propriete.PropertyType.GetMethods().First(mi => (mi.Name == "AddItem" || mi.Name == "Add") && mi.GetParameters().Length == 1 && mi.GetParameters()[0].ParameterType == typeof(string));
                foreach (var g in geles)
                {
                    ajouter.Invoke(liste, new object[] { g });
                }

                propriete.SetValue(vp, liste);
            }
        }

        lay.Entities.Add(vp);
        vp.Update();
    }

    // ------------------------------------------------------------------ primitives

    private vdTextstyle Style()
    {
        var style = _doc.TextStyles.FindName(StyleTexte);
        if (style == null)
        {
            style = new vdTextstyle();
            style.SetUnRegisterDocument(_doc);
            style.setDocumentDefaults();
            style.Name = StyleTexte;
            style.FontFile = "Arial";
            _doc.TextStyles.AddItem(style);
        }

        return style;
    }

    private static System.Drawing.Color Couleur(Encre encre) => encre switch
    {
        Encre.Gris => System.Drawing.Color.FromArgb(110, 110, 110),
        Encre.Cote => System.Drawing.Color.FromArgb(0, 80, 160),
        Encre.Rouge => System.Drawing.Color.FromArgb(200, 0, 0),
        Encre.Bleu => System.Drawing.Color.FromArgb(0, 102, 204),
        Encre.Blanc => System.Drawing.Color.FromArgb(254, 254, 254), // pas 255 : le blanc pur peut être inversé en noir sur fond blanc
        Encre.FondFonce => System.Drawing.Color.FromArgb(96, 96, 96),
        Encre.FondFamille => System.Drawing.Color.FromArgb(225, 240, 214),
        Encre.FondClair => System.Drawing.Color.FromArgb(244, 244, 244),
        _ => System.Drawing.Color.Black,
    };

    private static gPoint G(Point2 p) => new gPoint(p.U, p.V, 0);

    private void Ajouter(vdLayout lay, vdFigure figure, Encre encre)
    {
        figure.PenColor = new vdColor(Couleur(encre));
        lay.Entities.AddItem(figure);
    }

    private void Tracer(vdLayout lay, Primitive primitive, vdTextstyle style)
    {
        switch (primitive)
        {
            case Trait t:
            {
                // Tirets dessinés trait par trait (3 mm, jour de 1,5 mm) : indépendant des types de ligne du document.
                var morceaux = new List<(Point2, Point2)>();
                if (t.Tirets)
                {
                    double longueur = (t.B - t.A).Longueur;
                    var d = (t.B - t.A).Norme();
                    for (double s = 0; s < longueur; s += 4.5)
                    {
                        morceaux.Add((t.A + d * s, t.A + d * Math.Min(s + 3, longueur)));
                    }
                }
                else
                {
                    morceaux.Add((t.A, t.B));
                }

                foreach (var (a, b) in morceaux)
                {
                    var l = new vdLine(_doc, G(a), G(b)); // constructeur employé par la v2 (clMiseEnPlan)
                    l.PenWidth = t.Epaisseur;
                    Ajouter(lay, l, t.Encre);
                }

                break;
            }

            case Cercle c:
            {
                var k = new vdCircle(_doc, G(c.Centre), c.Rayon);
                k.PenWidth = 0.25;
                if (c.Tirets)
                {
                    Tirets(k);
                }

                Ajouter(lay, k, c.Encre);
                break;
            }

            case Polygone g:
            {
                var pl = new vdPolyline();
                pl.SetUnRegisterDocument(_doc);
                pl.setDocumentDefaults();
                foreach (var s in g.Sommets)
                {
                    pl.VertexList.Add(G(s));
                }

                pl.Flag = global::VectorDraw.Professional.Constants.VdConstPlineFlag.PlFlagCLOSE;
                pl.PenWidth = 0.2;
                if (g.Plein)
                {
                    pl.HatchProperties = new vdHatchProperties(global::VectorDraw.Professional.Constants.VdConstFill.VdFillModeSolid);
                }

                Ajouter(lay, pl, g.Encre);
                break;
            }

            case Texte x:
                TracerTexte(lay, x, style, 0);
                if (x.Gras)
                {
                    // Gras simulé : le texte repassé avec un léger décalage (fonctionne avec toutes les polices).
                    TracerTexte(lay, x, style, Math.Max(0.08, x.Hauteur * 0.035));
                }

                break;

            case ImageFeuille i:
                TracerImage(lay, i);
                break;
        }
    }

    private void Tirets(vdFigure figure)
    {
        var type = _doc.LineTypes.FindName("DASHED") ?? _doc.LineTypes.FindName("CACHE") ?? _doc.LineTypes.FindName("HIDDEN");
        if (type != null)
        {
            figure.LineType = type;
            figure.LineTypeScale = 0.1;
        }
    }

    private void TracerTexte(vdLayout lay, Texte x, vdTextstyle style, double decalage)
    {
        var t = new vdText(_doc);
        t.Style = style;
        t.TextString = x.Contenu;
        t.Height = x.Hauteur;
        t.Rotation = x.Angle * Math.PI / 180;
        switch (x.Alignement)
        {
            case Alignement.BasCentre:
                t.HorJustify = global::VectorDraw.Professional.Constants.VdConstHorJust.VdTextHorCenter;
                break;
            case Alignement.Centre:
                t.HorJustify = global::VectorDraw.Professional.Constants.VdConstHorJust.VdTextHorCenter;
                t.VerJustify = global::VectorDraw.Professional.Constants.VdConstVerJust.VdTextVerCen;
                break;
            case Alignement.GaucheMilieu:
                t.VerJustify = global::VectorDraw.Professional.Constants.VdConstVerJust.VdTextVerCen;
                break;
            case Alignement.DroiteMilieu:
                t.HorJustify = global::VectorDraw.Professional.Constants.VdConstHorJust.VdTextHorRight;
                t.VerJustify = global::VectorDraw.Professional.Constants.VdConstVerJust.VdTextVerCen;
                break;
            case Alignement.BasDroite:
                t.HorJustify = global::VectorDraw.Professional.Constants.VdConstHorJust.VdTextHorRight;
                break;
        }

        // Décalage du gras le long du texte.
        double a = x.Angle * Math.PI / 180;
        t.InsertionPoint = new gPoint(x.Position.U + decalage * Math.Cos(a), x.Position.V + decalage * Math.Sin(a), 0);
        Ajouter(lay, t, x.Encre);
    }

    private void TracerImage(vdLayout lay, ImageFeuille i)
    {
        string chemin = i.Source;
        if (!File.Exists(chemin))
        {
            chemin = Path.Combine(DossierLogos, i.Source + ".png");
        }

        if (!File.Exists(chemin))
        {
            Diagnostic.Add("Logo introuvable : " + i.Source);
            return;
        }

        int largeurPx, hauteurPx;
        using (var image = System.Drawing.Image.FromFile(chemin))
        {
            largeurPx = image.Width;
            hauteurPx = image.Height;
        }

        // Proportions conservées, centré dans le cadre.
        double k = Math.Min(i.Cadre.Largeur / largeurPx, i.Cadre.Hauteur / hauteurPx);
        double l = largeurPx * k, h = hauteurPx * k;
        // Comme la v2 (clMiseEnPlan.InsereImage) : définition d'image nommée, image insérée à sa largeur.
        var def = new vdImageDef(_doc, "MDC-" + Path.GetFileNameWithoutExtension(chemin)) { FileName = chemin };
        var img = new vdImage(_doc, def, new gPoint(i.Cadre.Centre.U - l / 2, i.Cadre.Centre.V - h / 2, 0), l, 0);
        img.PenColor = new vdColor(System.Drawing.Color.Transparent);
        lay.Entities.AddItem(img);
    }
}
