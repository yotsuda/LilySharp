// Lily# - Music notation compiler
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System.Linq;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Proposal A: the '.up' / '.down' placement qualifier on '@' annotations
/// (e.g. '@staccato.up') forces an articulation above / below, overriding the
/// automatic (opposite-the-stem) side. It rides the existing '@name(qualifier)'
/// grammar, so the syntax already parsed — only the meaning is new.
/// </summary>
[Trait("Category", "Unit")]
public class ArticulationPlacementTests
{
    private static MultiStaffScore Collect(string src)
    {
        var tree = SyntaxTree.Parse(src);
        Assert.False(tree.HasErrors,
            string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        return SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
    }

    [Fact]
    public void UpDownQualifier_ForcesArticulationSide()
    {
        var score = Collect(
            "part m { clef treble }\n" +
            "section S { m { c'4@staccato.up d'4@staccato.down } }\n" +
            "form main { S }\n" +
            "score main \"o\" { staff m }\n");

        var stac = score.Articulations
            .Where(a => a.Type == ArticulationType.Staccato)
            .OrderBy(a => a.ItemIndex)
            .ToList();

        Assert.Equal(2, stac.Count);
        Assert.True(stac[0].IsAbove, "@staccato.up should be placed above");
        Assert.False(stac[1].IsAbove, "@staccato.down should be placed below");
    }

    [Fact]
    public void QualifierFlipsTheAutomaticSide()
    {
        // The same note: plain '@staccato' takes the automatic side; '.up' / '.down'
        // force the two sides, so at least one of them differs from the automatic one.
        // (This proves the qualifier overrides the default rather than being ignored,
        // without depending on which side the default picks.)
        MultiStaffScore Side(string ann)
        {
            return Collect(
                "part m { clef treble }\n" +
                $"section S {{ m {{ c'4{ann} }} }}\n" +
                "form main { S }\n" +
                "score main \"o\" { staff m }\n");
        }

        bool plain = Side("@staccato").Articulations.Single().IsAbove;
        bool up = Side("@staccato.up").Articulations.Single().IsAbove;
        bool down = Side("@staccato.down").Articulations.Single().IsAbove;

        Assert.True(up);
        Assert.False(down);
        Assert.True(plain == up || plain == down); // plain matches one forced side; the other is a real flip
        Assert.NotEqual(up, down);
    }

    /// <summary>All music-glyph (x, y, char) triples in a rendered SVG.</summary>
    private static List<(double X, double Y, char Glyph)> MusicGlyphs(string svg) =>
        System.Text.RegularExpressions.Regex.Matches(svg,
                "<text class=\"music\" x=\"([-\\d.]+)\" y=\"([-\\d.]+)\"[^>]*>(&#x([0-9A-Fa-f]+);|.)</text>")
            .Select(m => (
                X: double.Parse(m.Groups[1].Value),
                Y: double.Parse(m.Groups[2].Value),
                Glyph: m.Groups[4].Success
                    ? (char)Convert.ToInt32(m.Groups[4].Value, 16)
                    : m.Groups[3].Value[0]))
            .ToList();

    /// <summary>The middle staff line's device Y: the 3rd of the five long horizontals.</summary>
    private static double MiddleLineY(string svg)
    {
        var lineYs = System.Text.RegularExpressions.Regex.Matches(svg,
                "<line x1=\"([-\\d.]+)\" y1=\"([-\\d.]+)\" x2=\"([-\\d.]+)\" y2=\"([-\\d.]+)\"")
            .Where(m => m.Groups[2].Value == m.Groups[4].Value
                && double.Parse(m.Groups[3].Value) - double.Parse(m.Groups[1].Value) > 5)
            .Select(m => double.Parse(m.Groups[2].Value))
            .Distinct().OrderBy(v => v).ToList();
        Assert.Equal(5, lineYs.Count);
        return lineYs[2];
    }

    [Fact]
    public void ForcedUpMarcato_QuantizesIntoTheStaff()
    {
        // The chord-scripts / articulations residual (Δ0.70): a quantize-position
        // script (marcato) snaps its REFPOINT to a staff position and takes NO
        // staff-padding — LilyPond seats a forced-up marcato over c'' at staff
        // POSITION 3, inside the staff, the chevron straddling the top line. Over
        // g'/e'/c' (up stems) the support is the stem tip: 5.4 (past the +5 span
        // gate, unquantized), 5 (rounded 4 = a line, pushed one further), 3.
        // MEASURED against scratch/lpreg/probe-script-y.{ly,svg} — all four exact.
        // LILYPOND-REF: scm/script.scm marcato (quantize-position . #t);
        //   lily/side-position-interface.cc:409-432 quantize_position.
        string svg = LilySharp.Core.Svg.SvgGenerator.Generate(
            SyntaxTree.Parse("time 4/4 c'4@marcato.up g4@marcato.up e4@marcato.up c4@marcato.up"),
            new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false });
        double middle = MiddleLineY(svg);
        var marcatos = MusicGlyphs(svg)
            .Where(g => g.Glyph == '').OrderBy(g => g.X).ToList();
        Assert.Equal(4, marcatos.Count);
        // Device Y-down: origin = middle − (staff-spaces above the middle line).
        Assert.Equal(middle - 1.5, marcatos[0].Y, 2); // c'': position 3, INSIDE
        Assert.Equal(middle - 2.7, marcatos[1].Y, 2); // g': raw 5.4, unquantized
        Assert.Equal(middle - 2.5, marcatos[2].Y, 2); // e': rounded 4 → line → 5
        Assert.Equal(middle - 1.5, marcatos[3].Y, 2); // c': position 3, INSIDE
    }

    [Fact]
    public void FermataFamily_OverAnAccent_ClearsTheAccentOutlinePointwise()
    {
        // fermata-dot-position.ly block B (the accent pairs), measured against the
        // LilyPond twin scratch/lpreg/fermata-dot-b.{ly,lys}: the accent keeps the
        // engraver answer (LP 4.167) and each fermata-family glyph clears the ACCENT'S
        // OUTLINE pointwise — engraver floor from the script-column support chain
        // (pointwise + own padding 0.40), finished by the outside-staff pass
        // (pointwise + 0.46) — giving three LEVELS (LP fermata 4.9496 / short 4.897 /
        // long 4.877). The old per-note box stack (prev box height + 0.2) parked all
        // three ~0.16-0.18 too high (5.12/5.06/5.06) because a box stack cannot see
        // where the accent's wedge slopes away under the fermata's ink.
        // LILYPOND-REF: lily/script-column.cc:168-171 Side_position_interface::add_support
        //   — every priority-less script so far supports the next script on the note.
        string svg = LilySharp.Core.Svg.SvgGenerator.Generate(
            SyntaxTree.Parse("time 4/4 a''4@accent a4@accent@fermata" +
                " a4@accent@shortfermata a4@accent@longfermata"),
            new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false });
        double middle = MiddleLineY(svg);
        var glyphs = MusicGlyphs(svg);
        double YUpOf(char c)
        {
            var g = Assert.Single(glyphs.Where(x => x.Glyph == c));
            return middle - g.Y;
        }
        Assert.Equal(4.95, YUpOf(EmmentalerGlyphs.FermataAbove), 2);      // LP 4.9496
        Assert.Equal(4.90, YUpOf(EmmentalerGlyphs.FermataShortAbove), 2); // LP 4.897
        Assert.Equal(4.87, YUpOf(EmmentalerGlyphs.FermataLongAbove), 2);  // LP 4.877
        var accents = glyphs.Where(x => x.Glyph == EmmentalerGlyphs.ArticAccentAbove).ToList();
        Assert.Equal(4, accents.Count);
        Assert.All(accents, a => Assert.Equal(4.16, middle - a.Y, 2));    // LP 4.167
    }

    [Fact]
    public void ScriptStack_OrdersByScriptPriority_FingeringAndBumpedBowIncluded()
    {
        // script-stack-order1.ly (the stacking-ladder book), measured against the
        // LilyPond twin scratch\lpreg\scriptstack1.{ly,lys}. Three regimes pinned:
        // (1) the FINGERING enters the note's script column at priority 100+position
        //     — over f'' it sits BETWEEN the tenuto and the bow (LP staccato −2.94 /
        //     tenuto −3.42 / finger −4.00 / upbow −5.33), and over e, between the
        //     tenuto and the downbow (LP −2.50 / −3.08 / −4.40); the digit's profile
        //     is its extent BOX (LP: no vertical-skylines declaration on Fingering),
        //     which is why the bow cannot sink into the "0"'s round shoulder.
        // (2) the +0.1 BUMP: over e' the upbow (priority 180, no outside-staff
        //     priority) follows the fermata (175, mover at 75) in the sorted walk,
        //     so it BECOMES a mover at 75.1 and stacks above the fermata — LP
        //     flageolet −6.14 / fermata −7.08 / upbow −8.99.
        // (3) the priority table: flageolet 50 keeps the flageolet under everything.
        // LILYPOND-REF: lily/script-column.cc:160-186 order_grobs — the walk;
        //   lily/new-fingering-engraver.cc:314-340 position_scripts — the fingering.
        string svg = LilySharp.Core.Svg.SvgGenerator.Generate(
            SyntaxTree.Parse("time 4/4 f'4@staccato@tenuto@finger(3)@upbow" +
                " e'@flageolet@fermata@upbow e,@tenuto@finger(0)@downbow r4 |"),
            new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false });
        double middle = MiddleLineY(svg);
        var glyphs = MusicGlyphs(svg);
        double YUpOfSingle(char c)
        {
            var g = Assert.Single(glyphs.Where(x => x.Glyph == c));
            return middle - g.Y;
        }
        // f'' — staccato, tenuto, FINGER 3, upbow, bottom-up:
        Assert.Equal(2.94, YUpOfSingle(EmmentalerGlyphs.ArticStaccatoAbove), 2); // LP 2.94
        var tenutos = glyphs.Where(x => x.Glyph == EmmentalerGlyphs.ArticTenutoAbove)
            .OrderBy(x => x.X).ToList();
        Assert.Equal(2, tenutos.Count);
        Assert.Equal(3.42, middle - tenutos[0].Y, 2);                       // LP 3.42
        Assert.Equal(4.00, YUpOfSingle(EmmentalerGlyphs.FigBassDigit3), 2); // LP 4.00
        var upbows = glyphs.Where(x => x.Glyph == EmmentalerGlyphs.ArticUpBowAbove)
            .OrderBy(x => x.X).ToList();
        Assert.Equal(2, upbows.Count);
        Assert.Equal(5.32, middle - upbows[0].Y, 2);                        // LP 5.33
        // e' — flageolet, fermata, then the BUMPED upbow above the mover:
        Assert.Equal(6.14, YUpOfSingle(EmmentalerGlyphs.ArticFlageolet), 2); // LP 6.14
        Assert.Equal(7.08, YUpOfSingle(EmmentalerGlyphs.FermataAbove), 2);   // LP 7.08
        Assert.Equal(8.99, middle - upbows[1].Y, 2);                        // LP 8.99
        // e, — tenuto, FINGER 0, downbow:
        Assert.Equal(2.50, middle - tenutos[1].Y, 2);                       // LP 2.50
        Assert.Equal(3.08, YUpOfSingle(EmmentalerGlyphs.FigBassDigit0), 2); // LP 3.08
        Assert.Equal(4.40, YUpOfSingle(EmmentalerGlyphs.ArticDownBowAbove), 2); // LP 4.40
    }

    [Fact]
    public void Trill_SitsOnTheStaffPaddingRefpointFloor()
    {
        // The articulations-book residual (Δ0.45): the trill glyph's origin IS its
        // ink bottom (font box Bottom 0.000), and the staff-padding floor binds the
        // REFPOINT — LilyPond seats a trill over c'' at exactly staff ink edge +
        // staff-padding = 2.05 + 0.25 = 2.30 above the middle line. The old
        // ornament-fallback box (Bottom −0.5) parked it at 2.75.
        // LILYPOND-REF: lily/side-position-interface.cc:433-453 staff_padding.
        string svg = LilySharp.Core.Svg.SvgGenerator.Generate(
            SyntaxTree.Parse("time 4/4 c'4@trill"),
            new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false });
        double middle = MiddleLineY(svg);
        var trill = Assert.Single(MusicGlyphs(svg).Where(g => g.Glyph == ''));
        Assert.Equal(middle - 2.30, trill.Y, 2);
    }

    [Fact]
    public void Scripts_AvoidTies_ButChordMemberScriptsDoNot()
    {
        // script-tie-collision.ly: a script on a tie's START or END note takes the
        // drawn bow as a side-position support, so the accent on a tied C6 rides up
        // over the bow's shoulder — start 5.43, end 5.76, against the untied 5.17.
        // A chord MEMBER's script (<g@tenuto c>) is New_fingering_engraver's — its
        // supports are the head, stem/flag and chord heads, with NO tie acknowledger
        // — so it keeps the island answer (4.83) at BOTH tie bounds while the
        // chord-level script on the same chord lifts (measured
        // scratch/lpreg/sctten.ly and sctten2.ly: the split holds on either head).
        // All values pinned against LP on scratch/lpreg/sctchord.{ly,lys}.
        // LILYPOND-REF: lily/script-engraver.cc:204-222 acknowledge_tie
        // LILYPOND-REF: lily/new-fingering-engraver.cc:144-157 add_script
        string svg = LilySharp.Core.Svg.SvgGenerator.Generate(
            SyntaxTree.Parse("time 4/4 r2. c''4@accent~ | c@accent r2. |" +
                " r2. <g@tenuto c@accent>4@tenuto~ |" +
                " <g@tenuto c>4@accent~ <g c@tenuto@portato>4@accent r2 |"),
            new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false });
        double middle = MiddleLineY(svg);
        var glyphs = MusicGlyphs(svg);
        var accents = glyphs.Where(g => g.Glyph == EmmentalerGlyphs.ArticAccentAbove)
            .OrderBy(g => g.X).ToList();
        Assert.Equal(5, accents.Count);
        Assert.Equal(5.42, middle - accents[0].Y, 2); // tie START lift (LP 5.43)
        Assert.Equal(5.76, middle - accents[1].Y, 2); // tie END lift (LP 5.76)
        Assert.Equal(6.39, middle - accents[2].Y, 2); // chord1 member accent — chain over the lifted tenutos (LP 6.40)
        Assert.Equal(5.76, middle - accents[3].Y, 2); // chord2 CHORD accent — the tie-end answer beats the chain (LP 5.76)
        Assert.Equal(5.78, middle - accents[4].Y, 2); // chord3 chord accent (LP 5.79)
        var tenutos = glyphs.Where(g => g.Glyph == EmmentalerGlyphs.ArticTenutoAbove)
            .OrderBy(g => g.X).ThenBy(g => middle - g.Y).ToList();
        Assert.Equal(4, tenutos.Count);
        Assert.Equal(5.35, middle - tenutos[0].Y, 2); // chord1 CHORD tenuto lifts over its own tie (LP 5.35)
        Assert.Equal(5.71, middle - tenutos[1].Y, 2); // chord1 member tenuto — chain only (LP 5.71)
        Assert.Equal(4.82, middle - tenutos[2].Y, 2); // chord2 MEMBER tenuto — no tie support (LP 4.83)
        Assert.Equal(4.82, middle - tenutos[3].Y, 2); // chord3 member tenuto (LP 4.83)
        var portato = Assert.Single(
            glyphs.Where(g => g.Glyph == EmmentalerGlyphs.ArticPortatoBelow));
        Assert.Equal(7.32, middle - portato.Y, 2);    // chord3 member portato — chain (LP 7.31)
    }

    [Fact]
    public void Scripts_RideOffASlur_InsideOnesStayPut()
    {
        // script-stack-order1's slurred note (the "avoid-slur 未実装" ticket): an
        // 'around script on a slurred note takes outside_slur_callback ON TOP of
        // its side-position answer — the accent over the slur-start e' rides off
        // the bow (2.67 with no slur term), and the finger and downbow above it
        // ride up through the support chain, one rigid body — while the slur-END
        // staccato is 'inside and stays on the head (LilyPond bends the SLUR
        // around an inside script instead; that half is the slur scorer's).
        // Against the LP twin (scratch/lpreg/scriptstack1.{ly,lys}) the lifted
        // stack reads 0.12 low — LP's slur END sits one 0.5-grid step higher
        // (its 'inside staccato at the slur end enters the slur's OWN extra
        // encompass and lifts it — the unported half named above), and the
        // avoidance reads the drawn slur.
        // LILYPOND-REF: lily/slur.cc:262-359 outside_slur_callback
        // LILYPOND-REF: scm/script.scm avoid-slur declarations
        string svg = LilySharp.Core.Svg.SvgGenerator.Generate(
            SyntaxTree.Parse("time 4/4 e'4(@accent@finger(0)@downbow c4@staccato)" +
                " d@tenuto@downbow d,@staccato@finger(0)@accent |"),
            new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false });
        double middle = MiddleLineY(svg);
        var glyphs = MusicGlyphs(svg);
        var accents = glyphs.Where(g => g.Glyph == EmmentalerGlyphs.ArticAccentAbove)
            .OrderBy(g => g.X).ToList();
        var downbows = glyphs.Where(g => g.Glyph == EmmentalerGlyphs.ArticDownBowAbove)
            .OrderBy(g => g.X).ToList();
        var fingers = glyphs.Where(g => g.Glyph == EmmentalerGlyphs.FigBassDigit0)
            .OrderBy(g => g.X).ToList();
        var staccatos = glyphs.Where(g => g.Glyph == EmmentalerGlyphs.ArticStaccatoAbove)
            .OrderBy(g => g.X).ToList();
        Assert.Equal(2, accents.Count);
        Assert.Equal(2, downbows.Count);
        Assert.Equal(2, fingers.Count);
        Assert.Equal(2, staccatos.Count);
        Assert.Equal(3.28, middle - accents[0].Y, 2);   // LP 3.40 — off the slur's bow
        Assert.Equal(4.14, middle - fingers[0].Y, 2);   // LP 4.26 — chain over the lifted accent
        Assert.Equal(5.47, middle - downbows[0].Y, 2);  // LP 5.58 — chain top
        Assert.Equal(1.50, middle - staccatos[0].Y, 2); // LP 1.50 — 'inside at the slur end, unmoved
        // The unslurred d keeps its plain chain: tenuto then downbow.
        Assert.Equal(2.78, middle - downbows[1].Y, 2);
    }
}
