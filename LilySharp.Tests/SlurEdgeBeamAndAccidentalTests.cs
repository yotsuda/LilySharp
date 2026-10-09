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
using LilySharp.Core.Rendering;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using LilySharp.Tests.LpFidelity;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// `b,,8 b,,( dis,)` in bass clef, G major (Butterfly.lys, Lab corpus): the slur leaves the
/// LAST note of an auto-beamed pair and arrives at a sharp. LilyPond 2.26.0 (Lab
/// sessions/p649 bfly, debug-slur-scoring: `L edge=0.80, extra=0.85 TOTAL=1.65 idx=18`)
/// draws it flat — both ends 1.195 above the middle line — over the sharp. Two Lily# defects
/// met here: ⑴ the left end sat on the beam, because "beamed on the inner side" was read off
/// the written `[` `]` marks an automatic beam never carries (lily/slur-scoring.cc:549-554
/// `Stem::get_beaming (stem, -d)`); ⑵ once on the head it ran through the sharp, because no
/// accidental entered the slur's extra objects (lily/slur-engraver.cc:73; slur-scoring.cc
/// :860-877 get_extra_encompass_infos).
/// </summary>
[Trait("Category", "Unit")]
public sealed class SlurEdgeBeamAndAccidentalTests
{
    private const string Book = """
        octave absolute
        key g major
        time 4/4
        part bassline {
          clef bass
          section A { b,,4 b,,8 b,,( dis,) dis, dis, dis, | }
        }
        form { ~A }
        score { staff bassline }
        """;

    private static RecordingDrawingContext Render()
    {
        var tree = SyntaxTree.Parse(Book);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        var layout = new LayoutEngine().Layout(score);
        using var doc = new RecordingDocumentContext();
        SharedRenderer.RenderTo(score, layout, doc);
        return doc.Page;
    }

    [Fact]
    public void TheSlurLeavesTheHead_AndClearsTheSharp_Flat()
    {
        var page = Render();
        var slur = Assert.Single(page.Beziers);
        // D#3 stands on the bass staff's middle line: its head's Y is the middle line's.
        double middle = page.Glyphs.Where(g => g.Glyph == EmmentalerGlyphs.NoteheadBlack)
            .OrderBy(g => g.X).Last().Y;
        // LilyPond: both ends 1.195 above it (device Y down), to the 0.01 the bow sweep
        // counts as equal. The sharp still grazes it there — LP's winner pays extra=0.85.
        Assert.Equal(middle - 1.195, slur.P0.Y, 2);
        Assert.Equal(middle - 1.195, slur.P1.Y, 2);
    }

    // A piece of a slur broken by the line is never bent off a staff line: LilyPond gates
    // avoid_staff_line on both extremes having a staff, and a broken bound has none
    // (slur-configuration.cc:48-50, slur-scoring.cc:221-227). Yesterday Once More.lys (Lab
    // corpus), reduced: LilyPond 2.26.0 draws the second piece's middle control points 0.7056
    // above its ends (Lab sessions/p649 yom; debug-slur-scoring idx=0 TOTAL=0.00) — the
    // generated height itself, unbent.
    [Fact]
    public void ABrokenPiece_IsNotBentOffAStaffLine()
    {
        var tree = SyntaxTree.Parse("""
            octave absolute
            key e major
            time 4/4
            part bassline {
              clef bass
              section A { b,,4 r8 b,,8 b,,2( | break e,4) r8 e, e,4 r8 e, | }
            }
            form { ~A }
            score { staff bassline }
            """);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        var layout = new LayoutEngine().Layout(score);
        using var doc = new RecordingDocumentContext();
        SharedRenderer.RenderTo(score, layout, doc);
        var piece = doc.Page.Beziers.OrderBy(b => b.P0.X).First();
        Assert.Equal(0.7056, piece.P0.Y - piece.Centreline1.Y, 3);
    }

    // A slur that leaves a note beamed on its inner side starts off the BEAM at the drawn
    // stem's x (slur-scoring.cc:549-554 stem_extent_[Y][dir_]) — not at the head centre, which
    // sits 0.65 along a sloped beam. LilyPond 2.26.0 (Lab sessions/p654 beamslur, the S1 book
    // slur-voices reduced): both slurs start 7.2315 above / 5.2315 below the middle line;
    // Lily# read the beam at the head centre until session 654 (0.077 off on the lower one).
    [Fact]
    public void ASlurOffABeam_StartsAtTheStemsX()
    {
        var tree = SyntaxTree.Parse("""
            octave absolute
            part m {
              section A { voice { c''8( b' a' g' f'4 e') } { e8( f g a b4 c') } }
            }
            form { A }
            score { staff m }
            """);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        var layout = new LayoutEngine(score.Paper).Layout(score);
        using var doc = new RecordingDocumentContext();
        SharedRenderer.RenderTo(score, layout, doc);

        double middle = Assert.Single(TwinBeamSweep.StavesOf(doc.Page)).Middle;
        var bows = doc.Page.Beziers.OrderBy(b => b.P0.Y).ToList();
        Assert.Equal(2, bows.Count);
        Assert.Equal(7.231495, middle - bows[0].P0.Y, 0.001);
        Assert.Equal(-5.231495, middle - bows[1].P0.Y, 0.001);
    }
}
