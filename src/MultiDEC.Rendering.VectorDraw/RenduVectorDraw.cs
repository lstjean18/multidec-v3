using System;
using System.Collections.Generic;
using System.Linq;
using MultiDEC.Core.Facade;
using VectorDraw.Geometry;
using VectorDraw.Professional.vdFigures;
using VectorDraw.Professional.vdObjects;
using VectorDraw.Professional.vdPrimaries;

namespace MultiDEC.Rendering.VectorDraw;

/// <summary>Compte rendu d'un dessin.</summary>
public sealed class RapportRendu
{
    public int Inseres { get; internal set; }

    /// <summary>Références sans bloc dans la bibliothèque (non dessinées).</summary>
    public SortedSet<string> BlocsManquants { get; } = new SortedSet<string>(StringComparer.Ordinal);
}

/// <summary>
/// Dessine une <see cref="StructureFacade"/> dans un document VectorDraw 8.8006.
/// La bibliothèque Blocs-EM-3D.dxf est ouverte une fois ; ses blocs sont ensuite insérés par nom.
/// À VÉRIFIER SUR LE SERVEUR DE DEV : ce fichier n'a pas pu être compilé sans les DLL VectorDraw.
/// </summary>
public sealed class RenduVectorDraw
{
    /// <summary>Calque des volumes 3D des blocs (même nom que dans l'ancienne version).</summary>
    public const string CalqueRealiste = "Structure-realiste";

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

            var ins = new vdInsert();
            ins.SetUnRegisterDocument(_doc);
            ins.setDocumentDefaults();
            ins.Block = bloc;
            ins.InsertionPoint = new gPoint(0, 0, 0);
            ins.Label = e.NumeroMaille.ToString();
            ins.Transformby(VersMatrix(orientations.Matrice(e)));
            _doc.Model.Entities.AddItem(ins);
            rapport.Inseres++;
        }

        _doc.Model.ZoomExtents();
        _doc.Redraw(true);
        return rapport;
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

    /// <summary>Recopie la matrice calculée par <see cref="TableOrientations"/> dans une Matrix VectorDraw.</summary>
    private static Matrix VersMatrix(double[,] m)
    {
        var r = new Matrix();
        r.A00 = m[0, 0]; r.A01 = m[0, 1]; r.A02 = m[0, 2]; r.A03 = m[0, 3];
        r.A10 = m[1, 0]; r.A11 = m[1, 1]; r.A12 = m[1, 2]; r.A13 = m[1, 3];
        r.A20 = m[2, 0]; r.A21 = m[2, 1]; r.A22 = m[2, 2]; r.A23 = m[2, 3];
        r.A30 = 0; r.A31 = 0; r.A32 = 0; r.A33 = 1;
        return r;
    }
}
