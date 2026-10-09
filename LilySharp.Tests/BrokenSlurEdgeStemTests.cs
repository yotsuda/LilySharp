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
/// A slur piece broken by the line starts (or ends) at its bound column's WHOLE extent on the
/// slur's side — the stem included when it points the slur's way — plus half a space
/// (lily/slur-scoring.cc:600-616). `c2( e | break g2 c'')`: LilyPond 2.26.0 starts the second
/// piece 3.0 above the middle line = the up stem of `g2` (tip 2.5) + 0.5, and ends the first
/// at 2.15 over `e2`'s up stem (Lab sessions/p653, the S1 book slur-break). Until session 653
/// Lily# read only the head there (0.195). A piece whose only column is its other, real bound
/// keeps that bound's y instead (:613-614) — ten exact pairs of the reader's corpus.
/// </summary>
[Trait("Category", "Unit")]
public sealed class BrokenSlurEdgeStemTests
{
    [Fact]
    public void TheSecondPiece_StartsOverTheStemOfItsFirstColumn()
    {
        var tree = SyntaxTree.Parse("""
            octave absolute
            part m {
              section A { c2( e | break g2 c'') | }
            }
            form { A }
            score { staff m }
            """);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        var layout = new LayoutEngine(score.Paper).Layout(score);
        using var doc = new RecordingDocumentContext();
        SharedRenderer.RenderTo(score, layout, doc);

        var page = doc.Page;
        var staves = TwinBeamSweep.StavesOf(page).OrderBy(s => s.Middle).ToList();
        Assert.Equal(2, staves.Count);
        var pieces = page.Beziers.OrderBy(b => b.P0.Y).ToList();
        Assert.Equal(2, pieces.Count);
        // Device Y grows downward: "above the middle" is middle − y.
        Assert.Equal(3.0, staves[1].Middle - pieces[1].P0.Y, 3);
        Assert.Equal(2.15, staves[0].Middle - pieces[0].P1.Y, 3);
        // …and the first piece's curve is lifted over e's up stem, which LilyPond keeps among
        // its avoid points: only the slur's EXTREMES leave them (slur-scoring.cc:668-670), and
        // a broken side has none. LilyPond (Lab sessions/p654 sbred, this book): control points
        // 0.4948 and 4.1200 above the middle — the fit factor 1.1117 over e's stem.
        Assert.Equal(0.4948, staves[0].Middle - pieces[0].Centreline1.Y, 3);
        Assert.Equal(4.1200, staves[0].Middle - pieces[0].Centreline2.Y, 3);
    }

    [Fact]
    public void APieceWhoseOnlyColumnIsItsOwnBound_EndsAtThatBoundsY()
    {
        // `e2( | break c''2)`: the down stem of c'' turns the slur UP, so e's up stem points
        // the slur's way — but the first piece holds e alone, which is its real left bound,
        // and LilyPond ends the piece at that bound's y (flat at 0.805 below the middle line,
        // Lab sessions/p653 onecol) rather than over e's stem (+1.5).
        var tree = SyntaxTree.Parse("""
            octave absolute
            part m {
              section A { c2 e( | break c''2) r2 | }
            }
            form { A }
            score { staff m }
            """);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        var layout = new LayoutEngine(score.Paper).Layout(score);
        using var doc = new RecordingDocumentContext();
        SharedRenderer.RenderTo(score, layout, doc);

        var page = doc.Page;
        var staves = TwinBeamSweep.StavesOf(page).OrderBy(s => s.Middle).ToList();
        var first = page.Beziers.OrderBy(b => b.P0.Y).First();
        Assert.Equal(-0.805, staves[0].Middle - first.P0.Y, 3);
        Assert.Equal(-0.805, staves[0].Middle - first.P1.Y, 3);
    }
}
