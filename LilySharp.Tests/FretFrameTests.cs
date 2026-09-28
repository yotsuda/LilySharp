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
using LilySharp.Core.LilyPond;
using LilySharp.Core.Music;
using LilySharp.Core.Rendering;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A fret diagram (<c>@diagram(…)</c>) stands over its chord whatever the stem, and under it
/// with <c>.down</c> — owner's decision, session 646. It took the side opposite the stem like a
/// staccato until 2026-09-26, and the LilyPond twin dropped every one (Lab
/// probes/complex-lys/06); LilyPond's TextScript default is DOWN, so the twin writes the side.
/// </summary>
[Trait("Category", "Unit")]
public class FretFrameTests
{
    private const string Book = """
        octave absolute
        part gt { clef treble }
        section A { gt { <g,@diagram(320003) d g>2 <g' d'' g''@diagram(320003)>2 | e2 c2@diagram(x32010).down | } }
        form main { A }
        score main { staff gt }
        """;

    /// <summary>
    /// An open string is an <c>O</c> and a muted one an <c>X</c>, set in the sans face over the
    /// string, as LilyPond's fret-diagrams.scm draw-xo sets them (2026-09-29; until then a
    /// stroked ring and two strokes, and before 2026-09-28 a black disc under a white one, which
    /// the preview's dark theme inverted — owner report). 320003 has three open strings (twice),
    /// x32010 two open and one muted.
    /// </summary>
    [Fact]
    public void AnOpenString_IsAnO_AMutedOneAnX_InTheSansFace()
    {
        string svg = SvgGenerator.Generate(SyntaxTree.Parse(Book));
        var os = System.Text.RegularExpressions.Regex.Matches(svg, "<text([^>]*)>O</text>");
        Assert.Equal(3 + 3 + 2, os.Count);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(svg, "<text[^>]*>X</text>"));
        Assert.All(os, m => Assert.Contains("sans-serif", m.Groups[1].Value));
        // No ring and no disc: the dots are the only circles.
        Assert.DoesNotContain("<ellipse", svg);
        Assert.DoesNotMatch("<circle[^>]*fill=\"#FFFFFF\"", svg);
    }

    [Fact]
    public void AFrame_StandsAboveWhateverTheStem_AndBelowWithDown()
    {
        var tree = SyntaxTree.Parse(Book);
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        var sides = new LayoutEngine().Layout(score).ArticulationLayouts
            .OrderBy(a => a.MeasureIndex).ThenBy(a => a.X)
            .Select(a => a.IsAbove).ToArray();
        // a low chord (stem up), a high chord (stem down), a `.down` frame
        Assert.Equal(new[] { true, true, false }, sides);
    }

    private const string TextLength =
        "-\\tweak extra-spacing-width #'(-0.0 . 0.4) -\\tweak extra-spacing-height #'(-inf.0 . +inf.0) ";

    [Fact]
    public void TheTwin_WritesEachFrameAsAFretDiagramOnItsSide()
    {
        string ly = new LilyPondExporter().Export(SyntaxTree.Parse(Book));
        // A member's frame goes after the chord: LilyPond takes no text script on one head.
        // Each carries \textLengthOn's two properties, as the page spaces it.
        Assert.Contains("<g, d g>2" + TextLength + "^\\markup \\fret-diagram-terse \"3;2;o;o;o;3;\"", ly);
        Assert.Contains("c2" + TextLength + "_\\markup \\fret-diagram-terse \"x;3;2;o;1;o;\"", ly);
    }

    // ---- size (owner's decision, session 646: LilyPond's size by default, fonts step scales) ----

    private static System.Collections.Generic.List<ArticulationLayout> Frames(string book)
    {
        var tree = SyntaxTree.Parse(book);
        Assert.False(tree.HasErrors, string.Join(" | ", tree.Diagnostics.Select(d => d.Message)));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        return new LayoutEngine().Layout(score).ArticulationLayouts
            .OrderBy(a => a.MeasureIndex).ThenBy(a => a.X).ToList();
    }

    private const string Pair = """
        octave absolute
        part gt { clef treble }
        section A { gt { <g@diagram(320003) b d'>4 <d'@diagram(xx0232) fis' a'>4 <c'@diagram(x32010) e' g'>4 <a@diagram(x02220) cis' e'>4 | } }
        form main { A }
        score main { staff gt }
        """;

    private static readonly ScoreTextMetrics Fonts = ScoreTextMetrics.Bundled;

    [Fact]
    public void ADefaultFrame_IsLilyPondsSize_OneStaffSpaceAStringAndAFret()
    {
        // Six strings one staff space apart (5 wide) plus the X / O glyphs' reach each side; four
        // frets one space deep, the nut's box over the top line (three line thicknesses less the
        // half under the line), the X / O row xo-padding over it; the strings one fret past the
        // last row and half a line thickness more. LILYPOND-REF: scm/fret-diagrams.scm make-fret-diagram.
        var ink = Frames(Pair)[0].Ink;                       // 320003: three open, none muted
        var m = FretFrameGeometry.Measure("320003", Fonts);
        Assert.Equal(5.0 + 2 * m.XoHalfWidth, ink.Right - ink.Left, 9);
        Assert.Equal(4.0 + (3 * 0.05 - 0.025) + 0.2 + m.XoHeight, ink.Top, 9);
        Assert.Equal(-(1.0 + 0.025), ink.Bottom, 9);
        // The O is the text font's em at magnification 0.4: 2.2 × 0.4.
        Assert.Equal(0.88, m.O!.Value.Em, 9);
        Assert.Null(m.X);
    }

    /// <summary>A shape with every string fretted has no X / O row: its top is the nut's.
    /// A shifted shape has no nut, only fret 0's line, and its "Nfr" label right.</summary>
    [Fact]
    public void WithNoOpenOrMutedString_TheTopIsTheNut_AndAShiftedShapeHasNoNut()
    {
        var full = FretFrameGeometry.Measure("133211", Fonts);
        Assert.Null(full.XoCentre);
        Assert.Equal(4.0 + (3 * 0.05 - 0.025), full.Box.Top, 9);
        Assert.Null(full.FretLabel);
        var shifted = FretFrameGeometry.Measure("x57775", Fonts);    // 5fr
        Assert.Equal(0.025, shifted.AboveTop, 9);
        Assert.Equal("5fr", shifted.FretLabel!.Value.Text);
        Assert.Equal(1.1, shifted.FretLabel!.Value.Em, 9);
        // Centred half a fret under the top line; its ink starts 0.7 right of the last string.
        Assert.Equal(4.0 - 0.5, shifted.LabelCentreY, 9);
        Assert.Equal(2.5 + 0.7 + shifted.FretLabel!.Value.Width / 2, shifted.LabelCentreX, 9);
        Assert.Equal(shifted.LabelCentreX + shifted.FretLabel!.Value.Width / 2, shifted.Box.Right, 9);
    }

    /// <summary>
    /// A predefined shape's spec carries its fingers and barre as a detail suffix; the fingers
    /// are set under the strings only on a FretBoard (a chords row's diagram) — their tops
    /// finger-label-padding under the strings' overhang — and never on a markup diagram.
    /// </summary>
    [Fact]
    public void TheSpecCarriesFingersAndBarres_TheFingersDeepenAFretBoardOnly()
    {
        Assert.True(ChordStructure.TryParseChordEntry("F", out var chord));
        var f = ChordShapes.Default(TuningType.Guitar, chord!)!;
        Assert.Equal("133211|134211|6-1@1", f.FrameSpec);
        Assert.Equal(6, FretFrameGeometry.Strings(f.FrameSpec));
        Assert.Equal(new[] { 1, 3, 4, 2, 1, 1 }, Enumerable.Range(0, 6).Select(i => FretFrameGeometry.FingerAt(f.FrameSpec, i)));
        Assert.Equal(new FretFrameGeometry.BarreSpan(0, 5, 1), Assert.Single(FretFrameGeometry.Barres(f.FrameSpec)));
        Assert.Equal(1, FretFrameGeometry.FretAt(f.FrameSpec, 0));
        Assert.Equal(1, FretFrameGeometry.BaseFret(f.FrameSpec));

        var markup = FretFrameGeometry.Measure(f.FrameSpec, Fonts);
        var board = FretFrameGeometry.Measure(f.FrameSpec, Fonts, fingers: true);
        Assert.All(markup.Fingers, l => Assert.Null(l));
        Assert.Equal(-(1.0 + 0.025), markup.Box.Bottom, 9);
        Assert.Equal(6, board.Fingers.Count(l => l != null));
        Assert.Equal(-(1.0 + 0.3), board.FingerTop, 9);
        double tallest = board.Fingers.Max(l => l!.Value.Height);
        Assert.Equal(board.FingerTop - tallest, board.Box.Bottom, 9);
        Assert.Equal(2.2 * 0.6, board.Fingers[0]!.Value.Em, 9);
        // The same shape written at the chord has no detail: no fingers even on a row.
        Assert.Equal("133211", ChordVoicings.ToFrameSpec(f.Frets));
        Assert.All(FretFrameGeometry.Measure("133211", Fonts, fingers: true).Fingers, l => Assert.Null(l));
        // A written shape's box is the row's box less the fingers.
        Assert.Equal(FretFrameGeometry.Box("133211", Fonts).Top, board.Box.Top, 9);
    }

    [Fact]
    public void FretFrameStepSix_DoublesTheWholeDiagram()
    {
        // The layout's box, not only the drawing: the walk hands the engraver a Score with no
        // font plan, and until 2026-09-26 the box stayed at size 1 under a stepped drawing.
        var plain = Frames(Pair)[0].Ink;
        var big = Frames("fonts { diagram step +6 }\n" + Pair)[0].Ink;
        Assert.Equal(2.0 * plain.Top, big.Top, 9);
        Assert.Equal(2.0 * (plain.Right - plain.Left), big.Right - big.Left, 9);
    }

    [Theory]
    [InlineData("")]
    [InlineData("fonts { diagram step +6 }\n")]
    public void Frames_StandSideBySide_TheBarWideningToFitThem(string fonts)
    {
        // Four quarter-note diagrams in one bar: each clears the next by textLengthOn's 0.4
        // (and the 0.1 rod padding) — never stacked, never overlapping.
        var f = Frames(fonts + Pair);
        Assert.Equal(4, f.Count);
        for (int i = 0; i + 1 < f.Count; i++)
        {
            double gap = (f[i + 1].X + f[i + 1].Ink.Left) - (f[i].X + f[i].Ink.Right);
            Assert.True(gap >= 0.4 + 0.1 - 1e-9, $"frames {i}/{i + 1}: gap {gap}");
        }
    }

    /// <summary>A dash-separated shape (owner's decision 2026-09-28) draws its frets 10–15 on
    /// the page: the grid shifted to the lowest fretted note with its "Nfr" label, four dots —
    /// and the twin's terse markup writes the same frets.</summary>
    [Fact]
    public void ADashSeparatedFrame_DrawsFretsTenToFifteen_WithItsFretLabel()
    {
        const string high = """
            octave absolute
            part gt { clef treble }
            section A { gt { c'2@diagram(x-15-13-12-13-x) c'2@diagram(x-x-10-12-13-11) | } }
            form main { A }
            score main { staff gt }
            """;
        var f = Frames(high);
        Assert.Equal(new[] { "frame:xfdcdx", "frame:xxacdb" }, f.Select(a => a.Glyph));
        Assert.Equal(12, FretFrameGeometry.BaseFret("xfdcdx"));
        Assert.Equal(10, FretFrameGeometry.BaseFret("xxacdb"));
        Assert.Equal(4, FretFrameGeometry.RowCount("xfdcdx"));   // frets 12-15
        string svg = SvgGenerator.Generate(SyntaxTree.Parse(high));
        Assert.Contains(">12fr<", svg);
        Assert.Contains(">10fr<", svg);
        string ly = new LilyPondExporter().Export(SyntaxTree.Parse(high));
        Assert.Contains("\\fret-diagram-terse \"x;15;13;12;13;x;\"", ly);
        Assert.Contains("\\fret-diagram-terse \"x;x;10;12;13;11;\"", ly);
    }

    [Fact]
    public void TheTwin_WritesAFretFrameStepAsTheDiagramsSize()
    {
        // FretBoard.font-size does not reach a \markup diagram (Lab sessions/p646 fr3); the
        // markup's own size is the page's factor, 2^(step/6).
        string ly = new LilyPondExporter().Export(SyntaxTree.Parse("fonts { diagram step +6 }\n" + Pair));
        Assert.Contains("^\\markup \\override #'(size . 2) \\fret-diagram-terse \"3;2;o;o;o;3;\"", ly);
    }
}
