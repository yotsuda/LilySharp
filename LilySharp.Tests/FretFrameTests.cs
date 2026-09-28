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
    /// An open string is a RING — a stroked circle with no fill, as LilyPond's
    /// fret-diagrams.scm draws it — not a black disc under a white one: the preview's dark
    /// theme inverts the page (the white core turned black) and its caret highlight repaints
    /// fills, so the old pair read as a black dot (owner report 2026-09-28). 320003 has three
    /// open strings (twice), x32010 two.
    /// </summary>
    [Fact]
    public void AnOpenString_IsAnUnfilledRing()
    {
        string svg = SvgGenerator.Generate(SyntaxTree.Parse(Book));
        int rings = System.Text.RegularExpressions.Regex.Matches(svg, "<ellipse[^>]*fill=\"none\"[^>]*stroke=").Count;
        Assert.Equal(3 + 3 + 2, rings);
        // No white disc anywhere (the page's own background rect is white — not a circle).
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

    [Fact]
    public void ADefaultFrame_IsLilyPondsSize_OneStaffSpaceAStringAndAFret()
    {
        // Six strings one staff space apart (5 wide) plus the o / x reach each side, four frets
        // one space deep plus the header. LILYPOND-REF: scm/fret-diagrams.scm make-fret-diagram.
        var ink = Frames(Pair)[0].Ink;
        Assert.Equal(5.0 + 2 * 0.32, ink.Right - ink.Left, 9);
        Assert.Equal(4.0 + 0.68 + 0.32, ink.Top, 9);
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
