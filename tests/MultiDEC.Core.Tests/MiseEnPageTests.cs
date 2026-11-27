using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using MultiDEC.Core.Facade;
using MultiDEC.Core.MiseEnPlan;
using Xunit;

namespace MultiDEC.Core.Tests;

/// <summary>Habillage des feuilles : cadre, cartouche, titres, cotes et repères en primitives.</summary>
public class MiseEnPageTests
{
    private static (PlanDeStructure Plan, StructureFacade Structure) Plan(ProjetStructure projet)
    {
        var s = GenerateurStructure.Generer(projet);
        var c = Cartouche.De(projet, s, "MultiDEC v3.0 bêta 1 – build 999", 2345, null, o => o.ToString()!);
        c.EtabliPar = "L. Saint-Jean";
        return (GenerateurPlan.Generer(projet, s, c), s);
    }

    private static ProjetStructure Droite(double longueur = 15, double hauteur = 6) =>
        ProjetStructure.Nouveau(face: new ParametresFacade { LongueurTotaleSouhaitee = longueur, LongueurMaillePreferee = 2.5, HauteurDernierPlancher = hauteur });

    [Fact]
    public void Chaque_feuille_a_cadre_cartouche_et_folio()
    {
        var (plan, _) = Plan(Droite());
        var feuilles = MiseEnPage.Feuilles(plan);
        Assert.Equal(plan.Pages.Count, feuilles.Count);
        foreach (var f in feuilles)
        {
            Assert.Contains(f.Textes, t => t.Contenu == $"Page {f.Page.Numero} / {plan.Pages.Count}");
            Assert.Contains(f.Textes, t => t.Contenu == "MultiDEC v3.0 bêta 1 – build 999");
            Assert.Contains(f.Textes, t => t.Contenu == "L. Saint-Jean");
            Assert.Contains(f.Primitives.OfType<ImageFeuille>(), i => i.Source == "RETOTUB");
            Assert.All(f.Primitives.OfType<Trait>(), t =>
            {
                Assert.InRange(t.A.U, 0, 420);
                Assert.InRange(t.B.V, 0, 297);
            });
        }
    }

    [Fact]
    public void Textes_des_cotes_egaux_aux_cotes_du_plan()
    {
        var (plan, _) = Plan(Droite());
        var f = MiseEnPage.Feuilles(plan)[1];
        var cotes = f.Page.Vues.SelectMany(v => v.Cotes).Select(c => c.Texte).OrderBy(t => t).ToList();
        var textes = f.Textes.Where(t => t.Encre == Encre.Cote && t.Hauteur == MiseEnPage.HauteurTexteCote && !t.Contenu.StartsWith("+", StringComparison.Ordinal))
            .Select(t => t.Contenu).OrderBy(t => t).ToList();
        Assert.Equal(cotes, textes);
    }

    [Fact]
    public void Cote_horizontale_sous_la_vue_texte_lisible()
    {
        var (plan, _) = Plan(Droite());
        var vue = plan.Pages[1].Vues[0];
        var f = new FeuilleDessinee(plan.Pages[1], plan.Format);
        var c = vue.Cotes.First(k => k.DecalageMm == -16);
        MiseEnPage.CoteDe(f, vue, c);
        var t = Assert.Single(f.Textes);
        Assert.Equal(0, t.Angle, 6);
        Assert.True(t.Position.V < vue.Cadre.VMin - 14, "ligne de cote sous la vue");
    }

    [Fact]
    public void Cote_verticale_texte_de_bas_en_haut()
    {
        var (plan, _) = Plan(Droite());
        var vue = plan.Pages[1].Vues[0];
        var f = new FeuilleDessinee(plan.Pages[1], plan.Format);
        MiseEnPage.CoteDe(f, vue, vue.Cotes.First(k => k.DecalageMm == 16));
        var t = Assert.Single(f.Textes);
        Assert.Equal(90, t.Angle, 6);
        Assert.True(t.Position.U < vue.Cadre.UMin - 14, "ligne de cote à gauche de la vue");
    }

    /// <summary>Aperçu SVG des feuilles (MULTIDEC_APERCU=dossier), pour contrôler la mise en page sans VectorDraw.</summary>
    [Fact]
    public void Apercu_svg()
    {
        var dossier = Environment.GetEnvironmentVariable("MULTIDEC_APERCU");
        if (string.IsNullOrEmpty(dossier))
        {
            return;
        }

        foreach (var (nom, projet) in new[] { ("droite", Droite()), ("L", FormeL()) })
        {
            var (plan, s) = Plan(projet);
            foreach (var f in MiseEnPage.Feuilles(plan))
            {
                File.WriteAllText(Path.Combine(dossier, $"{nom}-{f.Page.Numero}.svg"), Svg(f, s));
            }
        }
    }

    private static ProjetStructure FormeL()
    {
        var p = Droite(12, 8);
        p.Forme = FormeStructure.EnL;
        p.AjusterFaces();
        return p;
    }

    private static string Svg(FeuilleDessinee f, StructureFacade s)
    {
        var inv = CultureInfo.InvariantCulture;
        string X(double u) => u.ToString("0.###", inv);
        string Y(double v) => (f.Format.Hauteur - v).ToString("0.###", inv);
        string Couleur(Encre e) => e switch { Encre.Gris => "#888", Encre.Cote => "#0050a0", Encre.Rouge => "#c00", Encre.Bleu => "#06c", _ => "#000" };
        var sb = new StringBuilder();
        sb.Append($"<svg xmlns='http://www.w3.org/2000/svg' width='{X(f.Format.Largeur * 4)}' height='{X(f.Format.Hauteur * 4)}' viewBox='0 0 {X(f.Format.Largeur)} {X(f.Format.Hauteur)}'><rect width='100%' height='100%' fill='white'/>");
        foreach (var vue in f.Page.Vues)
        {
            var c = vue.Cadre;
            sb.Append($"<rect x='{X(c.UMin)}' y='{Y(c.VMax)}' width='{X(c.Largeur)}' height='{X(c.Hauteur)}' fill='#f4f4f4' stroke='#ddd' stroke-width='0.2'/>");
            foreach (var e in s.Elements.Where(e => vue.Faces == null || vue.Faces.Contains(e.NomFace)))
            {
                var p = vue.VersFeuille(vue.Projection.Projeter(e.Origine));
                if (p.U >= c.UMin && p.U <= c.UMax && p.V >= c.VMin && p.V <= c.VMax)
                {
                    sb.Append($"<circle cx='{X(p.U)}' cy='{Y(p.V)}' r='0.3' fill='#999'/>");
                }
            }
        }

        foreach (var p in f.Primitives)
        {
            string col = Couleur(p.Encre);
            switch (p)
            {
                case Trait t:
                    sb.Append($"<line x1='{X(t.A.U)}' y1='{Y(t.A.V)}' x2='{X(t.B.U)}' y2='{Y(t.B.V)}' stroke='{col}' stroke-width='{X(t.Epaisseur)}'{(t.Tirets ? " stroke-dasharray='2 1'" : "")}/>");
                    break;
                case Cercle k:
                    sb.Append($"<circle cx='{X(k.Centre.U)}' cy='{Y(k.Centre.V)}' r='{X(k.Rayon)}' fill='{(k.Plein ? col : "none")}' stroke='{col}' stroke-width='0.25'{(k.Tirets ? " stroke-dasharray='2 1'" : "")}/>");
                    break;
                case Polygone g:
                    sb.Append($"<polygon points='{string.Join(" ", g.Sommets.Select(q => X(q.U) + "," + Y(q.V)))}' fill='{(g.Plein ? col : "none")}' stroke='{col}' stroke-width='0.2'/>");
                    break;
                case ImageFeuille i:
                    sb.Append($"<rect x='{X(i.Cadre.UMin)}' y='{Y(i.Cadre.VMax)}' width='{X(i.Cadre.Largeur)}' height='{X(i.Cadre.Hauteur)}' fill='#fde' stroke='#c9a'/><text x='{X(i.Cadre.Centre.U)}' y='{Y(i.Cadre.Centre.V)}' font-size='2.5' text-anchor='middle'>{i.Source}</text>");
                    break;
                case Texte t:
                    string ancre = t.Alignement switch { Alignement.BasCentre or Alignement.Centre => "middle", Alignement.BasDroite or Alignement.DroiteMilieu => "end", _ => "start" };
                    string base_ = t.Alignement is Alignement.Centre or Alignement.GaucheMilieu or Alignement.DroiteMilieu ? " dominant-baseline='central'" : "";
                    sb.Append($"<text x='{X(t.Position.U)}' y='{Y(t.Position.V)}' font-family='Arial' font-size='{X(t.Hauteur * 1.4)}' fill='{col}' text-anchor='{ancre}'{base_}{(t.Gras ? " font-weight='bold'" : "")} transform='rotate({X(-t.Angle)} {X(t.Position.U)} {Y(t.Position.V)})'>{System.Security.SecurityElement.Escape(t.Contenu)}</text>");
                    break;
            }
        }

        sb.Append("</svg>");
        return sb.ToString();
    }
}
