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
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A glissando broken across a line break (the OMR's request of 2026-10-20, item 2): each
/// piece stands on ITS OWN system, every piece takes its share of ONE continuous slope, and a
/// later piece spanning no musical time is not drawn — LilyPond 2.26.0's answers
/// (Lab sessions/p915/lp/gliss.ly). Until then the later piece was placed against the first
/// system (above it, at the page's left) and the pieces were horizontals.
/// </summary>
[Trait("Category", "Unit")]
public class GlissandoAcrossBreakTests
{
    private static string Book(string bar3)
        => "octave absolute\ntime 4/4\nkey c major\npart p1 { clef treble }\nsection Piece {\n" +
           "  p1 {\n    c'4 d'4 e'4 f'4 |\n    g'2 e'2@glissando | break\n    " + bar3 + "\n    c''1 |\n  }\n}\n" +
           "form { ~Piece }\nscore { staff p1 \"Staff\" }\n";

    private static ScoreLayout Render(string book)
    {
        var tree = SyntaxTree.Parse(book);
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        return new LayoutEngine().Layout(score);
    }

    [Fact]
    public void ALaterPiece_StandsOnItsOwnSystem_OnTheSameSlope()
    {
        var layout = Render(Book("r4 c'''4 b''2 |"));
        var pieces = layout.GlissandoLayouts.OrderBy(g => g.PlacedMeasureIndex).ToList();
        Assert.Equal(2, pieces.Count);
        Assert.Equal(1, pieces[0].PlacedMeasureIndex);
        // The second piece is placed by bar 3, the first of the second system, while its
        // source link stays the start note's bar.
        Assert.Equal(2, pieces[1].PlacedMeasureIndex);
        Assert.Equal(1, pieces[1].MeasureIndex);
        var second = layout.AllSystems.Single(s => s.Measures.Any(m => m.MeasureIndex == 2));
        Assert.True(pieces[1].StartX > second.Measures[0].X, "the piece starts past the line start");
        // One continuous slope: the first piece rises toward the target, and both rise alike.
        double Slope(GlissandoLayout g) => (g.EndYUp - g.StartYUp) / (g.EndX - g.StartX);
        Assert.True(Slope(pieces[0]) > 0, "the first piece is not a horizontal");
        Assert.Equal(Slope(pieces[0]), Slope(pieces[1]), 6);
        Assert.True(pieces[0].EndYUp < pieces[1].StartYUp + 1e-9, "the second piece goes on from where the first one left off");
    }

    [Fact]
    public void ALaterPiece_SpanningNoTime_IsNotDrawn()
    {
        // The target is the next system's first note: LilyPond kills that piece
        // (kill-zero-spanned-time) and draws only the first, sloping toward the target.
        var layout = Render(Book("c'''2 b''2 |"));
        var piece = Assert.Single(layout.GlissandoLayouts);
        Assert.Equal(1, piece.PlacedMeasureIndex);
        Assert.True(piece.EndYUp > piece.StartYUp, "the first piece slopes up toward c'''");
    }
}
