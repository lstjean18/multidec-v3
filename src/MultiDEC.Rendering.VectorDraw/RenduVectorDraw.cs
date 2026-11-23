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
    public int Inseres { get; internal set; }

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

    /// <summary>Calque des axes filaires des blocs.</summary>
    public const string CalqueAxes = "Structure";

    /// <summary>Nom du bloc provisoire d'amarrage (cercle barré d'une croix, décision du 23/11/2026).</summary>
    public const string BlocAmarrageProvisoire = "MDC-AMARRAGE-PROVISOIRE";

    private readonly vdDocument _doc;

    public RenduVectorDraw(vdDocument document)
    {
        _doc = document ?? throw new ArgumentNullException(nameof(document));
    }

    /// <summary>
    /// Ouvre la bibliothèque de blocs et vide l'espace objet (les définitions de blocs restent).
    /// </summary>
    public void ChargerBibliotheque(string cheminDxf)
    {
        if (!_doc.Open(cheminDxf))
        {
            throw new InvalidOperationException("Impossible d'ouvrir la bibliothèque de blocs : " + cheminDxf);
        }

        _doc.Model.Entities.RemoveAll();
        var calque = _doc.Layers.FindName(CalqueRealiste);
        if (calque != null)
        {
            calque.On = true;
        }

        _doc.ActiveLayOut = _doc.Model;
        _doc.Model.Update();
        AxesFilaires(false);
        Rendu(ModeRendu.OmbreAretes);
    }

    /// <summary>Efface le dessin courant et dessine la structure.</summary>
    public RapportRendu Dessiner(StructureFacade structure, TableOrientations orientations)
    {
        var rapport = new RapportRendu();
        _doc.Model.Entities.RemoveAll();

        foreach (var e in structure.Elements)
        {
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

            ins.Label = e.NumeroMaille.ToString();
            _doc.Model.Entities.AddItem(ins);
            rapport.Inseres++;
        }

        _doc.Model.Entities.Update();
        Vue(Vues.Iso);
        return rapport;
    }

    /// <summary>Place la caméra : Z toujours vertical à l'écran, X le long de la façade, Y en profondeur.</summary>
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

    /// <summary>Affiche ou masque le calque « Structure » (axes filaires des blocs), gênant en rendu ombré.</summary>
    public void AxesFilaires(bool visibles)
    {
        var calque = _doc.Layers.FindName(CalqueAxes);
        if (calque != null)
        {
            calque.On = visibles;
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
