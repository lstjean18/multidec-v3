using System;
using System.Collections.Generic;
using System.Linq;
using MultiDEC.Core.Facade;
using VectorDraw.Geometry;
using VectorDraw.Professional.vdFigures;
using VectorDraw.Professional.vdObjects;
using VectorDraw.Professional.vdPrimaries;

namespace MultiDEC.Rendering.VectorDraw;

/// <summary>Vues prédéfinies.</summary>
public enum Vues
{
    Iso,
    Facade,
    Dessus,
    CoteGauche,
    CoteDroit,
}

/// <summary>Modes de rendu proposés à l'utilisateur.</summary>
public enum ModeRendu
{
    Filaire,
    LignesCachees,
    Ombre,
    OmbreAretes,
    Realiste,
}

/// <summary>Compte rendu d'un dessin.</summary>
public sealed class RapportRendu
{
    public int Inseres => Inserts.Count;

    /// <summary>Chaque élément dessiné et le vdInsert créé (sert aux contrôles et aux futures sélections).</summary>
    public List<(ElementPlace Element, vdInsert Insert)> Inserts { get; } = new List<(ElementPlace, vdInsert)>();

    /// <summary>Références sans bloc dans la bibliothèque (non dessinées).</summary>
    public SortedSet<string> BlocsManquants { get; } = new SortedSet<string>(StringComparer.Ordinal);
}

/// <summary>
/// Dessine une <see cref="StructureFacade"/> dans un document VectorDraw 9.9003.
/// La bibliothèque Blocs-EM-3D.dxf est ouverte une fois ; ses blocs sont ensuite insérés par nom.
/// </summary>
public sealed class RenduVectorDraw
{
    /// <summary>Calque des volumes 3D des blocs (même nom que dans l'ancienne version).</summary>
    public const string CalqueRealiste = "Structure-realiste";

    /// <summary>Calque des axes filaires des blocs… et du volume des blocs qui n'ont pas de calque réaliste (plateaux).</summary>
    public const string CalqueAxes = "Structure";

    /// <summary>Nom du bloc provisoire d'amarrage (cercle barré d'une croix, décision du 23/11/2026).</summary>
    public const string BlocAmarrageProvisoire = "MDC-AMARRAGE-PROVISOIRE";

    private readonly vdDocument _doc;
    private RapportRendu? _dernierRendu;

    public RenduVectorDraw(vdDocument document)
    {
        _doc = document ?? throw new ArgumentNullException(nameof(document));
    }

    /// <summary>Calque créé au chargement : axes filaires des blocs qui ont aussi des volumes (masqué par défaut).</summary>
    public const string CalqueAxesMdc = "MDC-Axes";

    /// <summary>Calque des murs du bâtiment (case « Dessiner façade »).</summary>
    public const string CalqueFacade = "MDC-Facade";

    /// <summary>Épaisseur dessinée des murs de façade (m), côté bâtiment.</summary>
    public const double EpaisseurMur = 0.10;

    /// <summary>Couleur des murs de façade.</summary>
    public static readonly System.Drawing.Color CouleurMur = System.Drawing.Color.FromArgb(214, 204, 188);

    /// <summary>Messages de préparation de la bibliothèque (repère, calques, couleurs) : utiles aux tests du serveur.</summary>
    public List<string> Diagnostic { get; } = new List<string>();

    /// <summary>
    /// Ouvre la bibliothèque de blocs, vide l'espace objet (les définitions de blocs restent) et la prépare :
    /// repère général (le DXF arrive avec un SCU actif où X = Y monde et Y = Z monde, ce qui couchait la vue),
    /// séparation des axes filaires et des volumes, couleurs par famille, fond blanc, rendu ombré avec arêtes (le rendu réaliste OpenGL reste au choix).
    /// </summary>
    public void ChargerBibliotheque(string cheminDxf)
    {
        if (!_doc.Open(cheminDxf))
        {
            throw new InvalidOperationException("Impossible d'ouvrir la bibliothèque de blocs : " + cheminDxf);
        }

        Diagnostic.Clear();
        _doc.Model.Entities.RemoveAll();
        _doc.ActiveLayOut = _doc.Model;
        RepereGeneral();
        PreparerBlocs();

        foreach (var nom in new[] { CalqueRealiste, CalqueAxes })
        {
            var calque = _doc.Layers.FindName(nom);
            if (calque != null)
            {
                calque.On = true;
            }
        }

        FondBlanc();
        MasquerPresentations();
        _doc.EnableAutoGripOn = true; // un clic sélectionne un bloc (poignées), pour le supprimer au clic droit

        // Objet sous le curseur surligné en rouge (comme la v2, clDocDessin.vb ; Laurent 25/11/2026).
        _doc.GlobalRenderProperties.SelectionPreview = global::VectorDraw.Render.vdRenderGlobalProperties.SelectionPreviewFlags.ON;
        _doc.GlobalRenderProperties.SelectionPreviewColor = System.Drawing.Color.Red;
        _doc.GlobalRenderProperties.GripSelectionHighlight = global::VectorDraw.Render.vdRenderGlobalProperties.GripSelectionHighlightFlags.ON;
        _doc.Model.Update();

        // Réglages par défaut demandés par Laurent (23/11/2026) : rendu ombré, axes filaires visibles.
        AxesFilaires(true);
        Rendu(ModeRendu.Ombre);
    }

    /// <summary>
    /// Remet le SCU sur le repère général. Mesuré sur le serveur : après ouverture de Blocs-EM-3D.dxf,
    /// World2UserMatrix du document et de l'espace objet vaut (X→Z, Y→X, Z→Y), et les vues prédéfinies
    /// (VISE…) sont calculées dans ce SCU : la longueur de la façade montait à l'écran.
    /// </summary>
    public void RepereGeneral()
    {
        Diagnostic.Add("SCU à l'ouverture : World2User " + DecrireMatrice(_doc.World2UserMatrix));
        _doc.World2UserMatrix = new Matrix();
        foreach (vdLayout presentation in _doc.LayOuts)
        {
            presentation.World2UserMatrix = new Matrix();
        }

        _doc.Model.World2UserMatrix = new Matrix();
        Diagnostic.Add("SCU après remise à zéro : World2User " + DecrireMatrice(_doc.World2UserMatrix) + ", espace objet " + DecrireMatrice(_doc.Model.World2UserMatrix));
    }

    private static string DecrireMatrice(Matrix m)
    {
        var x = m.Transform(new gPoint(1, 0, 0));
        var o = m.Transform(new gPoint(0, 0, 0));
        var y = m.Transform(new gPoint(0, 1, 0));
        return string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "X→({0:0.##};{1:0.##};{2:0.##}) Y→({3:0.##};{4:0.##};{5:0.##})",
            x.x - o.x, x.y - o.y, x.z - o.z, y.x - o.x, y.y - o.y, y.z - o.z);
    }

    /// <summary>
    /// Prépare les définitions de blocs pour le rendu :
    /// <list type="bullet">
    /// <item>dans un bloc qui a des volumes sur « Structure-realiste », les entités des calques « Structure » et
    /// « 1_AXES » ne sont que des axes filaires : elles passent sur <see cref="CalqueAxesMdc"/>, masqué par défaut.
    /// Dans les autres blocs (plateaux, cales…), « Structure » porte le volume lui-même et reste visible ;</item>
    /// <item>toutes les entités passent en couleur « DuBloc » : la couleur de l'insertion (par famille) colore la pièce.</item>
    /// </list>
    /// La bibliothèque n'est modifiée qu'en mémoire.
    /// </summary>
    public void PreparerBlocs()
    {
        var axes = _doc.Layers.FindName(CalqueAxesMdc);
        if (axes == null)
        {
            axes = new vdLayer();
            axes.SetUnRegisterDocument(_doc);
            axes.setDocumentDefaults();
            axes.Name = CalqueAxesMdc;
            _doc.Layers.AddItem(axes);
        }

        int blocs = 0, deplaces = 0, colores = 0;
        foreach (vdBlock bloc in _doc.Blocks)
        {
            bool volumes = false;
            foreach (vdFigure f in bloc.Entities)
            {
                if (string.Equals(f.Layer?.Name, CalqueRealiste, StringComparison.OrdinalIgnoreCase))
                {
                    volumes = true;
                    break;
                }
            }

            foreach (vdFigure f in bloc.Entities)
            {
                string calque = f.Layer?.Name ?? string.Empty;
                if (volumes && (string.Equals(calque, CalqueAxes, StringComparison.OrdinalIgnoreCase) || string.Equals(calque, "1_AXES", StringComparison.OrdinalIgnoreCase)))
                {
                    f.Layer = axes;
                    deplaces++;
                }

                f.PenColor.ByBlock = true;
                colores++;
            }

            blocs++;
        }

        Diagnostic.Add($"Blocs préparés : {blocs}, entités d'axes déplacées : {deplaces}, entités « DuBloc » : {colores}");
    }

    /// <summary>
    /// Masque les présentations venues de la bibliothèque (TES-ALU, TES-LIMON, Façade, Présentation1…) :
    /// seul l'espace objet est montré. La mise en plan viendra plus tard. Le contrôle doit ensuite
    /// reconstruire ses onglets (vdScrollableControl.ReFillTabs).
    /// </summary>
    public void MasquerPresentations()
    {
        int n = 0;
        foreach (vdLayout presentation in _doc.LayOuts)
        {
            if (ReferenceEquals(presentation, _doc.Model) || string.Equals(presentation.Name, "Model", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            presentation.VisibleOnForms = false;
            n++;
        }

        _doc.ActiveLayOut = _doc.Model;
        Diagnostic.Add($"Présentations masquées : {n}");
    }

    /// <summary>
    /// Élément du dernier dessin désigné par le clic droit : l'objet sélectionné s'il est seul, sinon celui sous
    /// le curseur (<paramref name="pointControle"/> en pixels dans le contrôle). Null si aucun.
    /// </summary>
    public ElementPlace? ElementDesigne(System.Drawing.Point pointControle)
    {
        if (_dernierRendu == null)
        {
            return null;
        }

        vdFigure? figure = null;
        var selection = _doc.GetGripSelection();
        if (selection != null && selection.Count == 1)
        {
            foreach (vdFigure f in selection)
            {
                figure = f;
            }
        }

        if (figure == null)
        {
            try
            {
                figure = _doc.ActiveLayOut.GetEntityFromPoint(pointControle, 6, false);
            }
            catch (Exception ex)
            {
                Diagnostic.Add("Clic droit : " + ex.Message);
            }
        }

        if (figure == null)
        {
            return null;
        }

        foreach (var (element, insert) in _dernierRendu.Inserts)
        {
            if (ReferenceEquals(insert, figure))
            {
                return element;
            }
        }

        return null;
    }

    /// <summary>Rend l'espace objet dans une image (hors écran : captures du manuel utilisateur).</summary>
    public void RendreDans(System.Drawing.Bitmap image) => _doc.Model.RenderToBitmap(image);

    /// <summary>Nombre d'objets sélectionnés (poignées) dans le dessin.</summary>
    public int NombreSelectionnes
    {
        get
        {
            var selection = _doc.GetGripSelection();
            return selection == null ? 0 : selection.Count;
        }
    }

    /// <summary>
    /// Supprime du dessin les blocs sélectionnés (clic, fenêtre de sélection) et renvoie les éléments
    /// correspondants, pour que le décompte suive.
    /// </summary>
    public List<ElementPlace> SupprimerSelection()
    {
        var retires = new List<ElementPlace>();
        var selection = _doc.GetGripSelection();
        if (selection == null || selection.Count == 0)
        {
            return retires;
        }

        var figures = new List<vdFigure>();
        foreach (vdFigure f in selection)
        {
            figures.Add(f);
        }

        foreach (var f in figures)
        {
            f.Deleted = true;
        }

        if (_dernierRendu != null)
        {
            foreach (var (element, insert) in _dernierRendu.Inserts)
            {
                if (figures.Any(f => ReferenceEquals(f, insert)))
                {
                    retires.Add(element);
                }
            }

            _dernierRendu.Inserts.RemoveAll(x => figures.Any(f => ReferenceEquals(f, x.Insert)));
        }

        selection.RemoveAll();
        _doc.Model.Entities.Update();
        _doc.Redraw(true);
        return retires;
    }

    /// <summary>Affiche tout le modèle, centré (bouton « Zoom tout »).</summary>
    public void ZoomTout()
    {
        _doc.Model.ZoomExtents();
        _doc.Redraw(true);
    }

    /// <summary>Fond blanc comme la v2 (propriété Palette.Background, recherchée par réflexion).</summary>
    private void FondBlanc()
    {
        try
        {
            var palette = _doc.GetType().GetProperty("Palette")?.GetValue(_doc);
            var fond = palette?.GetType().GetProperty("Background");
            if (fond != null && fond.CanWrite && fond.PropertyType == typeof(System.Drawing.Color))
            {
                fond.SetValue(palette, System.Drawing.Color.White);
                Diagnostic.Add("Fond : blanc");
            }
            else
            {
                Diagnostic.Add("Fond : propriété Palette.Background introuvable");
            }
        }
        catch (Exception ex)
        {
            Diagnostic.Add("Fond : " + ex.GetBaseException().Message);
        }
    }

    /// <summary>Efface le dessin courant et dessine la structure.</summary>
    public RapportRendu Dessiner(StructureFacade structure, TableOrientations orientations)
    {
        var rapport = new RapportRendu();
        _dernierRendu = rapport;
        _doc.Model.Entities.RemoveAll();

        foreach (var e in structure.Elements)
        {
            if (MultiDEC.Core.Catalogue.References.NonDessinee(e.Reference))
            {
                continue; // pitons et chevilles : décompte seulement
            }

            var bloc = _doc.Blocks.FindName(e.Reference);
            if (bloc == null && e.Famille == Famille.Amarrage)
            {
                bloc = BlocAmarrage();
            }

            if (bloc == null)
            {
                rapport.BlocsManquants.Add(e.Reference);
                continue;
            }

            // Insertion par vecteur d'extrusion + rotation (repère objet = axe arbitraire DXF).
            // InsertionPoint est en coordonnées MONDE : vérifié sur le serveur par le test de calibrage
            // (MultiDEC.Rendering.Tests), qui mesure la boîte englobante obtenue pour chaque orientation.
            var pose = PoseInsert.Depuis(orientations.Matrice(e));
            var o = pose.Insertion;
            var ins = new vdInsert();
            ins.SetUnRegisterDocument(_doc);
            ins.setDocumentDefaults();
            ins.Block = bloc;
            ins.InsertionPoint = new gPoint(o.X, o.Y, o.Z);
            ins.ExtrusionVector = new Vector(pose.Extrusion.X, pose.Extrusion.Y, pose.Extrusion.Z);
            ins.Rotation = pose.Rotation;
            if (pose.EchelleZ < 0)
            {
                ins.Zscale = -1;
            }

            ins.PenColor = new vdColor(PaletteFamilles.Couleur(e));
            ins.Label = e.NumeroMaille.ToString();
            _doc.Model.Entities.AddItem(ins);
            rapport.Inserts.Add((e, ins));
        }

        _doc.Model.Entities.Update();
        Vue(Vues.Iso);
        return rapport;
    }

    /// <summary>Place la caméra : Z toujours vertical à l'écran, X le long de la façade, Y en profondeur.</summary>
    /// <summary>
    /// Met une face en évidence (couleurs des familles) et estompe les autres en gris clair.
    /// Null : toutes les faces en couleur.
    /// </summary>
    public void MettreEnEvidence(string? nomFace)
    {
        if (_dernierRendu == null)
        {
            return;
        }

        bool plusieurs = _dernierRendu.Inserts.Select(x => x.Element.NomFace).Distinct().Skip(1).Any();
        foreach (var (element, insert) in _dernierRendu.Inserts)
        {
            bool active = nomFace == null || !plusieurs || element.NomFace == nomFace;
            insert.PenColor = new vdColor(active ? PaletteFamilles.Couleur(element) : CouleurEstompee);
        }

        _doc.Redraw(true);
    }

    /// <summary>Couleur des faces qui ne sont pas en cours d'édition.</summary>
    public static readonly System.Drawing.Color CouleurEstompee = System.Drawing.Color.FromArgb(200, 204, 210);

    public void Vue(Vues vue)
    {
        string commande = vue switch
        {
            Vues.Facade => "VFRONT", // depuis l'extérieur (Y négatifs), regard vers la façade
            Vues.Dessus => "VTOP",
            Vues.CoteGauche => "VLEFT",
            Vues.CoteDroit => "VRIGHT",
            _ => "VISE",             // isométrique sud-est : extérieur, à droite, en hauteur
        };
        _doc.CommandAction.View3D(commande);
        _doc.Model.ZoomExtents();
        _doc.Redraw(true);
    }

    /// <summary>Change le mode de rendu de l'espace objet.</summary>
    public void Rendu(ModeRendu mode)
    {
        var m = mode switch
        {
            ModeRendu.Filaire => global::VectorDraw.Render.vdRender.Mode.Wire3d,
            ModeRendu.LignesCachees => global::VectorDraw.Render.vdRender.Mode.Hide,
            ModeRendu.Ombre => global::VectorDraw.Render.vdRender.Mode.Shade,
            ModeRendu.OmbreAretes => global::VectorDraw.Render.vdRender.Mode.ShadeOn,
            _ => global::VectorDraw.Render.vdRender.Mode.Render,
        };
        _doc.Model.RenderMode = m;
        _doc.RenderMode = m;
        _doc.Redraw(true);
    }

    /// <summary>Affiche ou masque les axes filaires des blocs (calque <see cref="CalqueAxesMdc"/>), gênants en rendu ombré.</summary>
    public void AxesFilaires(bool visibles)
    {
        var calque = _doc.Layers.FindName(CalqueAxesMdc);
        if (calque != null)
        {
            calque.On = visibles;
            _doc.Redraw(true);
        }
    }

    /// <summary>
    /// Dessine les murs du bâtiment (<see cref="MursFacade"/>) sur le calque <see cref="CalqueFacade"/>,
    /// juste après <see cref="Dessiner"/> (qui vide le dessin), puis affiche ou masque le calque.
    /// </summary>
    public void DessinerFacade(IEnumerable<MurFacade> murs, bool visible)
    {
        var calque = _doc.Layers.FindName(CalqueFacade);
        if (calque == null)
        {
            calque = new vdLayer();
            calque.SetUnRegisterDocument(_doc);
            calque.setDocumentDefaults();
            calque.Name = CalqueFacade;
            _doc.Layers.AddItem(calque);
        }

        foreach (var mur in murs)
        {
            // Angle ramené entre 0 et 2π : un angle négatif (structure inversée, -90°) était ignoré par CreateBox (rendu serveur 24/11).
            double angle = mur.Angle < 0 ? mur.Angle + 2 * Math.PI : mur.Angle;
            double ux = Math.Cos(mur.Angle), uy = Math.Sin(mur.Angle);

            // Un bloc par pan plein (le mur est ouvert sous les poutres) ; épaisseur vers le bâtiment (à gauche du sens Début → Fin).
            foreach (var b in mur.Blocs())
            {
                var boite = new vdPolyface();
                boite.SetUnRegisterDocument(_doc);
                boite.setDocumentDefaults();
                boite.CreateBox(new gPoint(mur.Debut.X + ux * b.Position, mur.Debut.Y + uy * b.Position, b.Z), b.Longueur, EpaisseurMur, b.Hauteur, angle);
                boite.Layer = calque;
                boite.PenColor = new vdColor(CouleurMur);
                _doc.Model.Entities.AddItem(boite);
            }
        }

        AfficherFacade(visible);
    }

    /// <summary>Affiche ou masque les murs de façade sans redessiner la structure.</summary>
    public void AfficherFacade(bool visible)
    {
        var calque = _doc.Layers.FindName(CalqueFacade);
        if (calque != null)
        {
            calque.On = visible;
            _doc.Redraw(true);
        }
    }

    /// <summary>
    /// Bloc provisoire d'amarrage, créé à la première utilisation : cercle Ø 0,30 m barré d'une croix
    /// et petit cercle central (« lunette de visée »), dessiné dans le plan XY puis relevé en façade.
    /// À remplacer par les blocs HMTUBAMA quand ils seront dans la bibliothèque.
    /// </summary>
    public vdBlock BlocAmarrage()
    {
        var existant = _doc.Blocks.FindName(BlocAmarrageProvisoire);
        if (existant != null)
        {
            return existant;
        }

        var bloc = new vdBlock();
        bloc.SetUnRegisterDocument(_doc);
        bloc.setDocumentDefaults();
        bloc.Name = BlocAmarrageProvisoire;

        bloc.Entities.AddItem(Cercle(0.15));
        bloc.Entities.AddItem(Cercle(0.04));
        bloc.Entities.AddItem(Ligne(-0.20, 0, 0.20, 0));
        bloc.Entities.AddItem(Ligne(0, -0.20, 0, 0.20));

        _doc.Blocks.AddItem(bloc);
        return bloc;
    }

    private vdCircle Cercle(double rayon)
    {
        var c = new vdCircle();
        c.SetUnRegisterDocument(_doc);
        c.setDocumentDefaults();
        c.Center = new gPoint(0, 0, 0);
        c.Radius = rayon;
        c.PenColor = new vdColor(System.Drawing.Color.Red);
        return c;
    }

    private vdLine Ligne(double x1, double y1, double x2, double y2)
    {
        var l = new vdLine();
        l.SetUnRegisterDocument(_doc);
        l.setDocumentDefaults();
        l.StartPoint = new gPoint(x1, y1, 0);
        l.EndPoint = new gPoint(x2, y2, 0);
        l.PenColor = new vdColor(System.Drawing.Color.Red);
        return l;
    }
}
