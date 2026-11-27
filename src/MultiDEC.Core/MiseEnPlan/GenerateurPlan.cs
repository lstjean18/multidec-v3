using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MultiDEC.Core.Facade;
using MultiDEC.Core.Geometrie;

namespace MultiDEC.Core.MiseEnPlan;

/// <summary>
/// Mise en plan d'une structure dessinée (Laurent, 27/11/2026) : feuilles A3 avec vue de dessus, élévation de chaque face,
/// vues de profil, vues isométriques et vues de détail des particularités, toutes cotées, plus le cartouche.
/// Ne dépend pas de VectorDraw : le module de rendu crée une présentation par page et une fenêtre par vue.
/// </summary>
public static class GenerateurPlan
{
    /// <summary>Échelles normalisées proposées (dénominateurs).</summary>
    public static readonly int[] Echelles = { 10, 20, 25, 50, 75, 100, 125, 150, 200, 250, 300, 400, 500, 750, 1000 };

    // Place réservée autour d'une vue (mm) : cotes en bas et à gauche (deux rangées), niveaux à droite, titre dessous.
    private const double MargeGauche = 26, MargeBas = 24, MargeDroite = 16, MargeHaut = 6, Titre = 9;
    private const double Rangee = 8;

    private sealed class Contexte
    {
        public Contexte(ProjetStructure projet, StructureFacade structure)
        {
            Projet = projet;
            Structure = structure;
            Poses = GenerateurStructure.Poses(projet);
        }

        public ProjetStructure Projet { get; }
        public StructureFacade Structure { get; }
        public IReadOnlyList<PoseFace> Poses { get; }

        /// <summary>Point local d'une face → point de la structure.</summary>
        public Point3 Global(int face, double x, double y, double z)
        {
            var pose = Poses[face];
            var d = Projection.DirectionFace(pose.Angle);
            return new Point3(pose.Origine.X + x * d.X - y * d.Y, pose.Origine.Y + x * d.Y + y * d.X, z);
        }

        public IEnumerable<Point3> CoinsFace(int face, double yMin, double yMax, double zMin, double zMax, double xMin, double xMax)
        {
            foreach (var x in new[] { xMin, xMax })
            {
                foreach (var y in new[] { yMin, yMax })
                {
                    foreach (var z in new[] { zMin, zMax })
                    {
                        yield return Global(face, x, y, z);
                    }
                }
            }
        }
    }

    public static PlanDeStructure Generer(ProjetStructure projet, StructureFacade structure, Cartouche cartouche)
    {
        var ctx = new Contexte(projet, structure);
        var plan = new PlanDeStructure { Cartouche = cartouche };
        var details = Particularites(ctx);

        var dessus = new PageDePlan("Vue de dessus");
        dessus.Vues.Add(VueDessus(ctx));
        plan.Pages.Add(dessus);

        for (int i = 0; i < projet.Faces.Count; i++)
        {
            var page = new PageDePlan("Élévation " + projet.Faces[i].Nom);
            page.Vues.Add(Elevation(ctx, i, details.Where(d => d.Face == i)));
            plan.Pages.Add(page);
        }

        var profils = new PageDePlan("Vues de profil");
        profils.Vues.Add(Profil(ctx, 0, droit: false));
        profils.Vues.Add(Profil(ctx, projet.Faces.Count - 1, droit: true));
        plan.Pages.Add(profils);

        var iso = new PageDePlan("Vues isométriques");
        iso.Vues.Add(VueIso(ctx, coteBatiment: false));
        iso.Vues.Add(VueIso(ctx, coteBatiment: true));
        plan.Pages.Add(iso);

        // Détails : un par sorte de particularité (la première rencontrée), quatre par feuille.
        var vuesDetail = details.GroupBy(d => d.Sorte).Select(g => g.First().Vue).ToList();
        for (int i = 0; i < vuesDetail.Count; i += 4)
        {
            var page = new PageDePlan("Détails");
            page.Vues.AddRange(vuesDetail.Skip(i).Take(4));
            plan.Pages.Add(page);
        }

        foreach (var page in plan.Pages)
        {
            page.Numero = plan.Pages.IndexOf(page) + 1;
            Placer(page, plan.Format);
        }

        return plan;
    }

    // ------------------------------------------------------------------ vues

    private static Rect2 Emprise(Projection p, IEnumerable<Point3> points, double marge = 0.3) =>
        Rect2.Englobant(points.Select(p.Projeter)).Agrandi(marge);

    private static IEnumerable<Point3> Origines(Contexte ctx, string? face = null) =>
        ctx.Structure.Elements.Where(e => face == null || e.NomFace == face).Select(e => e.Origine);

    private static double Hauteur(FaceEchafaudage f) =>
        f.HauteurTotale + (f.Mailles.Count == 0 ? 0 : f.Mailles.Max(m => m.HauteurMax(f.Parametres.HauteurDernierPlancher)) - f.Parametres.HauteurDernierPlancher);

    private static VueDePlan VueDessus(Contexte ctx)
    {
        var p = Projection.Dessus();
        var vue = new VueDePlan("Vue de dessus", TypeVue.Dessus, p, Emprise(p, Origines(ctx)));
        var faces = ctx.Projet.Faces;
        for (int i = 0; i < faces.Count; i++)
        {
            var f = faces[i];
            double larg = f.Parametres.LargeurMaille;
            var d = Projection.DirectionFace(ctx.Poses[i].Angle);
            var dir = new Point2(d.X, d.Y);
            var xs = f.AbscissesFiles();

            // File extérieure : mailles en chaîne puis longueur de la face (côté extérieur, à droite de la direction).
            Chaine(vue, xs.Select(x => p.Projeter(ctx.Global(i, x, -larg, 0))).ToList(), dir, -Rangee);
            vue.Cotes.Add(new Cote(p.Projeter(ctx.Global(i, 0, -larg, 0)), p.Projeter(ctx.Global(i, f.LongueurObtenue, -larg, 0)), dir, -2 * Rangee));
            vue.Reperes.Add(new Repere(TypeRepere.Niveau, p.Projeter(ctx.Global(i, f.LongueurObtenue / 2, -larg / 2, 0)), f.Nom));
        }

        // Largeurs et distance au mur au début de la structure.
        var f0 = faces[0];
        var n0 = Projection.DirectionFace(ctx.Poses[0].Angle + 90);
        var travers = new Point2(n0.X, n0.Y);
        var ys = new List<double> { f0.Parametres.DistanceFacade, 0 };
        if (ctx.Projet.EstVolumique)
        {
            for (int i = 0; i < faces.Count; i++)
            {
                ys.Add(ctx.Poses[i].Origine.Y - faces[i].Parametres.LargeurMaille);
            }
        }
        else
        {
            ys.Add(-f0.Parametres.LargeurMaille);
        }

        Chaine(vue, ys.Distinct().OrderBy(y => y).Select(y => p.Projeter(ctx.Global(0, 0, y, 0))).ToList(), travers, Rangee);
        return vue;
    }

    private static VueDePlan Elevation(Contexte ctx, int i, IEnumerable<Particularite> details)
    {
        var f = ctx.Projet.Faces[i];
        var p = Projection.Elevation(ctx.Poses[i].Angle);
        double h = Hauteur(f);
        var coins = ctx.CoinsFace(i, -f.Parametres.LargeurMaille, 0, -0.3, h, 0, f.LongueurObtenue);
        var vue = new VueDePlan("Élévation " + f.Nom + " (côté extérieur)", TypeVue.Elevation, p, Emprise(p, coins.Concat(Origines(ctx, f.Nom))))
        {
            Faces = new[] { f.Nom },
        };
        double u0 = p.Projeter(ctx.Global(i, 0, 0, 0)).U;
        double bas = vue.Emprise.VMin;

        // Mailles en chaîne, longueur de la face.
        var xs = f.AbscissesFiles();
        Chaine(vue, xs.Select(x => new Point2(u0 + x, bas)).ToList(), new Point2(1, 0), -Rangee);
        vue.Cotes.Add(new Cote(new Point2(u0, bas), new Point2(u0 + f.LongueurObtenue, bas), new Point2(1, 0), -2 * Rangee));

        CotesNiveaux(vue, f, h);

        // Amarrages de la face.
        foreach (var a in ctx.Structure.Amarrages.Amarrages.Where(a => a.NomFace == f.Nom))
        {
            vue.Reperes.Add(new Repere(TypeRepere.Amarrage, new Point2(u0 + a.X, a.Hauteur), string.Empty));
        }

        // Renvois vers les détails.
        foreach (var d in details)
        {
            var zone = Rect2.Englobant(ctx.CoinsFace(i, -f.Parametres.LargeurMaille, 0, d.ZMin, d.ZMax, d.XMin, d.XMax).Select(p.Projeter));
            vue.Reperes.Add(new Repere(TypeRepere.Detail, zone.Centre, d.Vue.Lettre ?? string.Empty, zone));
        }

        return vue;
    }

    /// <summary>Niveaux à gauche (chaîne + hauteur totale) et repères de niveau à droite.</summary>
    private static void CotesNiveaux(VueDePlan vue, FaceEchafaudage f, double h)
    {
        var niveaux = new List<double> { 0 };
        niveaux.AddRange(f.Niveaux.Select(n => n.Hauteur).Where(z => z > Cotes.Tolerance));
        niveaux.Add(h);
        niveaux = niveaux.Select(Cotes.Arrondi).Distinct().OrderBy(z => z).ToList();
        double gauche = vue.Emprise.UMin;
        Chaine(vue, niveaux.Select(z => new Point2(gauche, z)).ToList(), new Point2(0, 1), Rangee);
        vue.Cotes.Add(new Cote(new Point2(gauche, 0), new Point2(gauche, h), new Point2(0, 1), 2 * Rangee));
        foreach (var z in f.Niveaux.Select(n => n.Hauteur).Where(z => z > Cotes.Tolerance).Distinct())
        {
            vue.Reperes.Add(new Repere(TypeRepere.Niveau, new Point2(vue.Emprise.UMax, z), "+" + Cote.Format(z)));
        }
    }

    private static VueDePlan Profil(Contexte ctx, int i, bool droit)
    {
        var f = ctx.Projet.Faces[i];
        var p = Projection.About(ctx.Poses[i].Angle, droit);
        double h = Hauteur(f);
        double larg = f.Parametres.LargeurMaille;
        double dist = f.Parametres.DistanceFacade;
        var points = ctx.CoinsFace(i, -larg, dist, -0.3, h, 0, f.LongueurObtenue).Concat(Origines(ctx, f.Nom));
        string titre = (droit ? "Profil droit (bout de " : "Profil gauche (bout de ") + f.Nom + ")";
        var vue = new VueDePlan(titre, TypeVue.Profil, p, Emprise(p, points)) { Faces = new[] { f.Nom } };

        // En travers, en bas : mur, file intérieure, file extérieure, puis l'encombrement total (consoles, pare-gravois).
        double bas = vue.Emprise.VMin;
        double x = droit ? f.LongueurObtenue : 0;
        var us = new[] { dist, 0, -larg }.Select(y => p.Projeter(ctx.Global(i, x, y, 0)).U).OrderBy(u => u).ToList();
        Chaine(vue, us.Select(u => new Point2(u, bas)).ToList(), new Point2(1, 0), -Rangee);
        var tout = Origines(ctx, f.Nom).Select(o => p.Projeter(o).U).ToList();
        double mur = p.Projeter(ctx.Global(i, x, dist, 0)).U;
        double loin = droit ? tout.Min() : tout.Max();
        vue.Cotes.Add(new Cote(new Point2(mur, bas), new Point2(loin, bas), new Point2(1, 0), -2 * Rangee, "Encombrement " + Cote.Format(Math.Abs(loin - mur))));
        CotesNiveaux(vue, f, h);
        return vue;
    }

    private static VueDePlan VueIso(Contexte ctx, bool coteBatiment)
    {
        var p = Projection.Iso(ctx.Poses[0].Angle, coteBatiment);
        return new VueDePlan(coteBatiment ? "Vue isométrique côté bâtiment" : "Vue isométrique côté extérieur", TypeVue.Iso, p, Emprise(p, Origines(ctx)));
    }

    private static void Chaine(VueDePlan vue, IReadOnlyList<Point2> points, Point2 direction, double decalage)
    {
        for (int k = 0; k + 1 < points.Count; k++)
        {
            var c = new Cote(points[k], points[k + 1], direction, decalage);
            if (c.Valeur > Cotes.Tolerance)
            {
                vue.Cotes.Add(c);
            }
        }
    }

    // ------------------------------------------------------------------ détails

    private sealed class Particularite
    {
        public int Face { get; set; }
        public string Sorte { get; set; } = string.Empty;
        public double XMin { get; set; }
        public double XMax { get; set; }
        public double ZMin { get; set; }
        public double ZMax { get; set; }
        public VueDePlan Vue { get; set; } = null!;
    }

    /// <summary>
    /// Particularités montrées en détail : accès, escalier, poutre de franchissement, console, pare-gravois, recette,
    /// about remplacé (la première de chaque sorte, sur chaque face).
    /// </summary>
    private static List<Particularite> Particularites(Contexte ctx)
    {
        var liste = new List<Particularite>();
        var faces = ctx.Projet.Faces;
        for (int i = 0; i < faces.Count; i++)
        {
            var f = faces[i];
            double larg = f.Parametres.LargeurMaille;
            double dist = f.Parametres.DistanceFacade;
            double hdp = f.Parametres.HauteurDernierPlancher;
            var xs = f.AbscissesFiles();
            int Index(Func<MailleDefinition, bool> critere) => f.Mailles.FindIndex(m => critere(m));

            void Ajouter(string titre, int maille, double zMin, double zMax, TypeVue sorte, double yMin, double yMax, double marge = 0)
            {
                string nature = titre.Split(' ')[0];
                double x0 = xs[maille] - marge, x1 = xs[maille + 1] + marge;
                var proj = sorte == TypeVue.Elevation ? Projection.Elevation(ctx.Poses[i].Angle)
                    : sorte == TypeVue.Profil ? Projection.About(ctx.Poses[i].Angle, false)
                    : Projection.Iso(ctx.Poses[i].Angle, false);
                var zone = ctx.CoinsFace(i, yMin, yMax, zMin, zMax, x0, x1);
                var vue = new VueDePlan(titre + " (" + f.Nom + ", maille " + (maille + 1) + ")", TypeVue.Detail, proj, Emprise(proj, zone, 0.2));
                liste.Add(new Particularite { Face = i, Sorte = nature, XMin = x0, XMax = x1, ZMin = zMin, ZMax = zMax, Vue = vue });
            }

            int acces = Index(m => m.Type == TypeMaille.Acces);
            if (acces >= 0)
            {
                Ajouter("Accès", acces, -0.3, Math.Min(hdp + 1, 4.6), TypeVue.Iso, -larg, 0);
            }

            int escalier = Index(m => m.EstEscalier);
            if (escalier >= 0)
            {
                Ajouter("Escalier", escalier, -0.3, Math.Min(hdp + 1, 6.6), TypeVue.Iso, -larg, 0);
            }

            int poutre = Index(m => m.Poutre > Cotes.Tolerance);
            if (poutre >= 0)
            {
                double zp = f.Mailles[poutre].Poutre;
                Ajouter("Poutre de franchissement", poutre, -0.3, Math.Min(hdp + 1, zp + 2.5), TypeVue.Elevation, -larg, 0, 1.0);
            }

            foreach (var n in f.Niveaux)
            {
                int k = f.Mailles.FindIndex(m => f.ConsoleDe(m, n) != null);
                if (k >= 0)
                {
                    Ajouter("Console " + f.ConsoleDe(f.Mailles[k], n), k, n.Hauteur - 1.2, n.Hauteur + 1.5, TypeVue.Profil, -larg, dist + 1.2);
                    break;
                }
            }

            foreach (var n in f.Niveaux)
            {
                int k = f.Mailles.FindIndex(m => f.PareGravoisSur(m, n));
                if (k >= 0)
                {
                    Ajouter("Pare-gravois", k, n.Hauteur - 1.0, n.Hauteur + 2.0, TypeVue.Profil, -larg - 2.0, dist);
                    break;
                }
            }

            var recette = f.Niveaux.FirstOrDefault(n => n.Recette);
            if (recette != null)
            {
                int k = Index(m => m.ProfondeurRecette > Cotes.Tolerance);
                Ajouter("Recette", k >= 0 ? k : 0, recette.Hauteur - 1.0, recette.Hauteur + 1.5, TypeVue.Profil, -larg - 2.5, dist);
            }

            if (f.Abouts.Count > 0)
            {
                bool droit = f.Abouts[0].Droit;
                Ajouter("About modifié", droit ? f.Mailles.Count - 1 : 0, -0.3, Math.Min(hdp + 1, 4.6), TypeVue.Iso, -larg - 1, dist);
            }
        }

        // Une lettre par sorte : toutes les occurrences renvoient au même détail.
        var sortes = liste.Select(d => d.Sorte).Distinct().ToList();
        foreach (var d in liste)
        {
            d.Vue.Lettre = ((char)('A' + sortes.IndexOf(d.Sorte))).ToString();
        }

        return liste;
    }

    // ------------------------------------------------------------------ mise en page

    /// <summary>Plus petite échelle normalisée (dénominateur) qui fait tenir la vue dans la place donnée.</summary>
    public static int EchelleQuiTient(Rect2 emprise, double largeurMm, double hauteurMm)
    {
        double mmParM = Math.Min(largeurMm / Math.Max(emprise.Largeur, 0.01), hauteurMm / Math.Max(emprise.Hauteur, 0.01));
        double denominateur = 1000 / mmParM;
        foreach (var e in Echelles)
        {
            if (e >= denominateur - 1e-9)
            {
                return e;
            }
        }

        return Echelles[Echelles.Length - 1];
    }

    private static void Placer(PageDePlan page, FormatFeuille format)
    {
        var zone = format.ZoneDessin;
        int n = page.Vues.Count;
        int colonnes = n <= 1 ? 1 : 2;
        int lignes = (n + colonnes - 1) / colonnes;
        double lc = zone.Largeur / colonnes, hc = zone.Hauteur / lignes;

        // Même échelle pour les vues d'une page (profils), sauf les détails et les isos qui ont chacun la leur.
        bool echelleCommune = page.Vues.All(v => v.Type == TypeVue.Profil);
        int commune = echelleCommune
            ? page.Vues.Max(v => EchelleQuiTient(v.Emprise, lc - MargeGauche - MargeDroite, hc - MargeBas - MargeHaut - Titre))
            : 0;

        for (int k = 0; k < n; k++)
        {
            var v = page.Vues[k];
            int col = k % colonnes, lig = k / colonnes;
            var cellule = new Rect2(zone.UMin + col * lc, zone.VMax - (lig + 1) * hc, zone.UMin + (col + 1) * lc, zone.VMax - lig * hc);
            bool cotee = v.Cotes.Count > 0;
            double mg = cotee ? MargeGauche : 4, md = cotee ? MargeDroite : 4, mb = (cotee ? MargeBas : 4) + Titre, mh = MargeHaut;
            double l = cellule.Largeur - mg - md, h = cellule.Hauteur - mb - mh;
            if (v.EstAxonometrique)
            {
                v.Echelle = 0;
                v.MmParMetre = Math.Min(l / Math.Max(v.Emprise.Largeur, 0.01), h / Math.Max(v.Emprise.Hauteur, 0.01));
            }
            else
            {
                v.Echelle = echelleCommune ? commune : EchelleQuiTient(v.Emprise, l, h);
                v.MmParMetre = 1000.0 / v.Echelle;
            }

            double w = v.Emprise.Largeur * v.MmParMetre, hh = v.Emprise.Hauteur * v.MmParMetre;
            double cu = cellule.UMin + mg + l / 2, cv = cellule.VMin + mb + h / 2;
            v.Cadre = new Rect2(cu - w / 2, cv - hh / 2, cu + w / 2, cv + hh / 2);
        }
    }
}
